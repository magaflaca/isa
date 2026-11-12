using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace IsaRefactor;

/// <summary>
/// Turns the chat command switches of a dispatcher class into one named handler method per command, placed in the module
/// the command works on, plus a catalog that registers them in the original dispatch order.
///
///   refactor extract-commands &lt;plan&gt; [--dry]
///
/// Plan lines, in dispatch order:
///   switch   Type.Method        a method "switch (text) { case "a": ... default: return false; } [tail]"
///   ifs      Type.Method        the "if (text == "a" || text == "b") { ... }" blocks of a method
///   parser   Expression         a module that parses its own commands, e.g. AiAssistant.HandleCommand(c.Name, c.Text, c.Parts)
///   name     Type.Method a b c  an existing CommandHandler method registered under these names
///   catalog  Ns.Class           where the generated catalog goes
/// </summary>
public static class ExtractCommands
{
	private const string Param = "command";

	/// <summary>Chosen handler names by command name (plan lines "method name HandlerName").</summary>
	private static readonly Dictionary<string, string> MethodNames = new();

	private sealed class Command
	{
		public List<string> Names = new();
		public string Source;
		public string TargetType;
		public string MethodName;
		public string Body;
		public HashSet<string> Usings = new();
		public bool Skip;
	}

	private static readonly string[] ExcludedTargets =
	{
		"IsaClient.Core.ClientChat", "IsaClient.Core.ClientLog", "IsaClient.Core.ClientSettings", "IsaClient.Core.ChatCommands"
	};

	public static async Task Run(SourceProject project, string[] plan, bool dry)
	{
		var solution = project.Workspace.CurrentSolution;
		var compilation = await solution.Projects.Single().GetCompilationAsync();
		var vocabulary = Vocabulary.Build(project.Root);
		var catalogLines = new List<string>();
		var commands = new List<Command>();
		var registered = new HashSet<string>();
		var removals = new Dictionary<DocumentId, List<TextSpan>>();
		string catalogClass = null;
		var problems = new List<string>();

		foreach (var raw in plan)
		{
			var line = raw.Split('#')[0].Trim();
			if (line.Length == 0)
			{
				continue;
			}
			var words = line.Split((char[])null, 2, StringSplitOptions.RemoveEmptyEntries);
			var kind = words[0];
			var arg = words[1].Trim();
			switch (kind)
			{
			case "catalog":
				catalogClass = arg;
				break;
			case "method":
			{
				var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
				MethodNames[parts[0]] = parts[1];
				break;
			}
			case "parser":
				catalogLines.Add($"\t\tCommandRegistry.RegisterParser(c => {arg});");
				break;
			case "name":
			{
				var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
				var fresh = parts.Skip(1).Where(registered.Add).ToList();
				if (fresh.Count > 0)
				{
					catalogLines.Add($"\t\tCommandRegistry.Register({parts[0]}, {string.Join(", ", fresh.Select(n => "\"" + n + "\""))});");
				}
				break;
			}
			case "switch":
			case "ifs":
			case "ifs-first":
			case "ifs-rest":
			{
				var (type, methodName) = Symbols.SplitMember(compilation, arg);
				var method = type.GetMembers(methodName).OfType<IMethodSymbol>().Single();
				var syntax = (MethodDeclarationSyntax)await method.DeclaringSyntaxReferences[0].GetSyntaxAsync();
				var document = solution.GetDocument(syntax.SyntaxTree);
				var model = await document.GetSemanticModelAsync();
				var found = kind == "switch" ? FromSwitch(syntax, model, problems) : FromIfs(syntax, model, problems);
				if (kind == "ifs-first")
				{
					found = found.Take(1).ToList();
				}
				else if (kind == "ifs-rest")
				{
					found = found.Skip(1).ToList();
				}
				foreach (var (names, statements, tail, removal) in found)
				{
					var command = Build(names, statements, tail, syntax, model, vocabulary, problems, arg);
					if (command == null)
					{
						continue;
					}
					var fresh = command.Names.Where(registered.Add).ToList();
					if (fresh.Count == 0)
					{
						problems.Add($"dropped (all names handled earlier): .{string.Join(" .", names)} in {arg}");
						command.Skip = true;
					}
					else
					{
						command.Names = fresh;
						commands.Add(command);
					}
					if (removal != default)
					{
						if (!removals.TryGetValue(document.Id, out var list))
						{
							removals[document.Id] = list = new List<TextSpan>();
						}
						list.Add(removal);
					}
					if (!command.Skip)
					{
						catalogLines.Add($"\t\tCommandRegistry.Register({ShortName(command.TargetType)}.{command.MethodName}, {string.Join(", ", command.Names.Select(n => "\"" + n + "\""))});");
					}
				}
				break;
			}
			default:
				throw new InvalidOperationException("unknown plan line: " + line);
			}
		}

		// Unique method names per target type.
		foreach (var group in commands.GroupBy(c => c.TargetType))
		{
			var type = compilation.GetTypeByMetadataName(group.Key);
			var taken = new HashSet<string>(type.GetMembers().Select(m => m.Name));
			foreach (var command in group)
			{
				var name = command.MethodName;
				for (int i = 2; taken.Contains(name); i++)
				{
					name = command.MethodName.Replace("Command", "") + i + "Command";
				}
				if (name != command.MethodName)
				{
					var old = $"{ShortName(command.TargetType)}.{command.MethodName}, {string.Join(", ", command.Names.Select(n => "\"" + n + "\""))});";
					var index = catalogLines.FindIndex(l => l.EndsWith(old));
					catalogLines[index] = catalogLines[index].Replace($"{ShortName(command.TargetType)}.{command.MethodName},", $"{ShortName(command.TargetType)}.{name},");
					command.MethodName = name;
				}
				taken.Add(name);
			}
		}

		Console.WriteLine($"{commands.Count} command handler(s), {catalogLines.Count} catalog line(s)");
		foreach (var group in commands.GroupBy(c => c.TargetType).OrderByDescending(g => g.Count()))
		{
			Console.WriteLine($"  {group.Key}: {group.Count()}  ({string.Join(" ", group.Take(8).Select(c => c.MethodName))}{(group.Count() > 8 ? " ..." : "")})");
		}
		foreach (var problem in problems)
		{
			Console.WriteLine("  ! " + problem);
		}
		if (dry)
		{
			return;
		}

		// Remove the converted sections / if blocks.
		foreach (var (id, spans) in removals)
		{
			var text = await solution.GetDocument(id).GetTextAsync();
			solution = solution.WithDocumentText(id, text.WithChanges(spans.Distinct().OrderBy(s => s.Start).Select(s => new TextChange(s, ""))));
		}

		// Append handlers to their modules.
		foreach (var group in commands.GroupBy(c => c.TargetType))
		{
			var currentCompilation = await solution.Projects.Single().GetCompilationAsync();
			var type = currentCompilation.GetTypeByMetadataName(group.Key);
			var reference = type.DeclaringSyntaxReferences.OrderBy(r => r.SyntaxTree.FilePath.Length).First();
			var declaration = (TypeDeclarationSyntax)await reference.GetSyntaxAsync();
			var document = solution.GetDocument(reference.SyntaxTree);
			var text = await document.GetTextAsync();
			var newLine = SourceProject.NewLineOf(text.ToString());
			var sb = new StringBuilder();
			sb.Append(newLine).Append("\t#region Chat commands").Append(newLine);
			foreach (var command in group)
			{
				sb.Append(newLine);
				sb.Append($"\t/// <summary>.{string.Join(" / .", command.Names)}</summary>").Append(newLine);
				sb.Append($"\tinternal static void {command.MethodName}(CommandContext {Param})").Append(newLine);
				sb.Append("\t{").Append(newLine);
				sb.Append(command.Body.Replace("\r\n", "\n").Replace("\n", newLine));
				sb.Append("\t}").Append(newLine);
			}
			sb.Append(newLine).Append("\t#endregion").Append(newLine);
			var root = (CompilationUnitSyntax)await document.GetSyntaxRootAsync();
			var existing = root.Usings.Select(u => u.ToString()).ToHashSet();
			var usings = group.SelectMany(c => c.Usings).Append("using IsaClient.Core;").Distinct()
				.Where(u => !existing.Contains(u) && u != $"using {type.ContainingNamespace.ToDisplayString()};").OrderBy(u => u).ToList();
			var changes = new List<TextChange> { new TextChange(new TextSpan(declaration.CloseBraceToken.SpanStart, 0), sb.ToString()) };
			if (usings.Count > 0)
			{
				int position = root.Usings.Count > 0 ? root.Usings.Last().FullSpan.End : 0;
				changes.Add(new TextChange(new TextSpan(position, 0), string.Concat(usings.Select(u => u + newLine))));
			}
			solution = solution.WithDocumentText(document.Id, text.WithChanges(changes));
		}

		// The catalog.
		int dot = catalogClass.LastIndexOf('.');
		string ns = catalogClass[..dot], cls = catalogClass[(dot + 1)..];
		var usingsForCatalog = commands.Select(c => c.TargetType[..c.TargetType.LastIndexOf('.')]).Distinct().Where(n => n != ns).OrderBy(n => n);
		var catalog = new StringBuilder();
		catalog.Append("using System;\r\n");
		foreach (var u in usingsForCatalog)
		{
			catalog.Append($"using {u};\r\n");
		}
		catalog.Append("\r\n");
		catalog.Append($"namespace {ns};\r\n\r\n");
		catalog.Append("/// <summary>\r\n/// Every chat command of the client, in lookup order (see <see cref=\"CommandRegistry\"/>). The handlers live in the\r\n/// module each command works on.\r\n/// </summary>\r\n");
		catalog.Append($"public static class {cls}\r\n{{\r\n\tpublic static void RegisterAll()\r\n\t{{\r\n");
		foreach (var l in catalogLines)
		{
			catalog.Append(l).Append("\r\n");
		}
		catalog.Append("\t}\r\n}\r\n");
		var path = Path.Combine(project.Root, ns, cls + ".cs");
		File.WriteAllText(path, catalog.ToString(), new UTF8Encoding(true));
		await project.Save(solution);
	}

	private static string ShortName(string fullName) => fullName[(fullName.LastIndexOf('.') + 1)..];

	/// <summary>(names, statements, tail statements, span to remove)</summary>
	private static List<(List<string>, List<StatementSyntax>, List<StatementSyntax>, TextSpan)> FromSwitch(MethodDeclarationSyntax method, SemanticModel model, List<string> problems)
	{
		var result = new List<(List<string>, List<StatementSyntax>, List<StatementSyntax>, TextSpan)>();
		var body = method.Body.Statements;
		int index = body.IndexOf(s => s is SwitchStatementSyntax);
		var switchStatement = (SwitchStatementSyntax)body[index];
		var tail = body.Skip(index + 1).ToList();
		foreach (var section in switchStatement.Sections)
		{
			if (section.Labels.Any(l => l is DefaultSwitchLabelSyntax))
			{
				continue;
			}
			var names = new List<string>();
			foreach (var label in section.Labels)
			{
				if (label is CaseSwitchLabelSyntax { Value: LiteralExpressionSyntax literal } && literal.IsKind(SyntaxKind.StringLiteralExpression))
				{
					names.Add(literal.Token.ValueText);
				}
				else
				{
					problems.Add($"non-literal label in {method.Identifier}: {label}");
					names = null;
					break;
				}
			}
			if (names == null)
			{
				continue;
			}
			result.Add((names, section.Statements.ToList(), tail, section.FullSpan));
		}
		return result;
	}

	private static List<(List<string>, List<StatementSyntax>, List<StatementSyntax>, TextSpan)> FromIfs(MethodDeclarationSyntax method, SemanticModel model, List<string> problems)
	{
		var result = new List<(List<string>, List<StatementSyntax>, List<StatementSyntax>, TextSpan)>();
		foreach (var statement in method.Body.Statements.OfType<IfStatementSyntax>())
		{
			var names = new List<string>();
			if (!CollectNames(statement.Condition, names) || statement.Else != null)
			{
				continue;
			}
			var statements = statement.Statement is BlockSyntax block ? block.Statements.ToList() : new List<StatementSyntax> { statement.Statement };
			result.Add((names, statements, new List<StatementSyntax>(), statement.FullSpan));
		}
		return result;
	}

	private static bool CollectNames(ExpressionSyntax condition, List<string> names)
	{
		switch (condition)
		{
		case BinaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalOrExpression } or:
			return CollectNames(or.Left, names) && CollectNames(or.Right, names);
		case BinaryExpressionSyntax { RawKind: (int)SyntaxKind.EqualsExpression } eq
			when eq.Left is IdentifierNameSyntax { Identifier.Text: "text" } && eq.Right is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression):
			names.Add(literal.Token.ValueText);
			return true;
		case ParenthesizedExpressionSyntax p:
			return CollectNames(p.Expression, names);
		default:
			return false;
		}
	}

	private static Command Build(List<string> names, List<StatementSyntax> statements, List<StatementSyntax> tail, MethodDeclarationSyntax method,
		SemanticModel model, Vocabulary vocabulary, List<string> problems, string source)
	{
		if (statements.Count == 1 && statements[0] is BlockSyntax onlyBlock)
		{
			statements = onlyBlock.Statements.ToList();
		}
		var tailWithoutReturn = tail.Where(s => !(s is ReturnStatementSyntax r && r.Expression is LiteralExpressionSyntax l && l.IsKind(SyntaxKind.TrueLiteralExpression))).ToList();
		var replacements = new Dictionary<SyntaxNode, string>();
		var parameterNames = method.ParameterList.Parameters.Select(p => p.Identifier.Text).ToHashSet();
		var locals = method.Body.Statements.TakeWhile(s => s is LocalDeclarationStatementSyntax).OfType<LocalDeclarationStatementSyntax>()
			.SelectMany(d => d.Declaration.Variables).ToDictionary(v => v.Identifier.Text, v => v.Initializer?.Value.ToString());
		var typeCounts = new Dictionary<string, int>();
		var usings = new HashSet<string>();
		bool failed = false;
		foreach (var statement in statements)
		{
			foreach (var node in statement.DescendantNodesAndSelf())
			{
				switch (node)
				{
				case IdentifierNameSyntax id:
				{
					var symbol = model.GetSymbolInfo(id).Symbol;
					if (symbol is IParameterSymbol parameter && parameterNames.Contains(parameter.Name) && SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, model.GetDeclaredSymbol(method)))
					{
						replacements[id] = parameter.Name switch
						{
							"text" => Param + ".Name",
							"chatText" => Param + ".Text",
							"array" => Param + ".Parts",
							_ => null,
						};
						if (replacements[id] == null)
						{
							problems.Add($"unknown parameter {parameter.Name} in {source}");
							failed = true;
						}
					}
					else if (symbol is ILocalSymbol local && locals.ContainsKey(local.Name) && local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is VariableDeclaratorSyntax decl && !statements.Any(s => s.Span.Contains(decl.Span)))
					{
						string mapped = locals[local.Name] == "Main.LocalPlayer" ? Param + ".Player" : local.Name switch
						{
							"array" => Param + ".Parts",
							"text" => Param + ".Name",
							_ => null,
						};
						if (mapped != null)
						{
							replacements[id] = mapped;
						}
						else
						{
							problems.Add($"prologue local {local.Name} used by .{names[0]} in {source}");
							failed = true;
						}
					}
					else if (symbol != null && symbol.IsStatic && symbol.ContainingType != null)
					{
						var owner = symbol.ContainingType.OriginalDefinition;
						while (owner.ContainingType != null)
						{
							owner = owner.ContainingType;
						}
						var full = owner.ToDisplayString();
						if (full.StartsWith("IsaClient.") && !ExcludedTargets.Contains(full) && owner.IsStatic)
						{
							typeCounts[full] = typeCounts.GetValueOrDefault(full) + 1;
						}
					}
					// Unqualified members of the dispatcher class keep working from the module the body moves to.
					if (symbol != null && symbol.IsStatic && symbol is not INamedTypeSymbol && SymbolEqualityComparer.Default.Equals(symbol.ContainingType, model.GetDeclaredSymbol(method).ContainingType)
						&& !(id.Parent is MemberAccessExpressionSyntax ma && ma.Name == id) && !replacements.ContainsKey(id))
					{
						replacements[id] = symbol.ContainingType.Name + "." + id.Identifier.Text;
					}
					var alias = model.GetAliasInfo(id);
					if (alias != null)
					{
						AddUsing(alias, usings);
					}
					else
					{
						AddUsing(symbol, usings);
					}
					break;
				}
				case GenericNameSyntax generic:
					AddUsing(model.GetSymbolInfo(generic).Symbol, usings);
					break;
				case InvocationExpressionSyntax invocation when model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { IsExtensionMethod: true } extension:
					usings.Add("using " + extension.ContainingNamespace.ToDisplayString() + ";");
					break;
				case BreakStatementSyntax breakStatement when TargetsOuterSwitch(breakStatement, statement):
					replacements[breakStatement] = string.Join(" ", tailWithoutReturn.Select(s => s.ToString())) + (tailWithoutReturn.Count > 0 ? " " : "") + "return;";
					break;
				case ReturnStatementSyntax ret when !InsideLambda(ret, statement):
					if (ret.Expression is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.TrueLiteralExpression))
					{
						replacements[ret] = "return;";
					}
					else if (ret.Expression is LiteralExpressionSyntax lit2 && lit2.IsKind(SyntaxKind.FalseLiteralExpression))
					{
						problems.Add($".{names[0]} in {source} can return false (declines): kept out");
						failed = true;
					}
					else if (ret.Expression != null)
					{
						problems.Add($".{names[0]} in {source} returns a value: kept out");
						failed = true;
					}
					break;
				}
			}
		}
		if (failed)
		{
			return null;
		}
		// Statement text with the replacements, then re-indented to method body level.
		var pieces = new StringBuilder();
		var all = statements.ToList();
		// A body ending in break/return gets the tail instead; drop a final plain return.
		for (int i = 0; i < all.Count; i++)
		{
			var statement = all[i];
			bool last = i == all.Count - 1;
			if (last && statement is BreakStatementSyntax)
			{
				foreach (var t in tailWithoutReturn)
				{
					pieces.Append(t.ToFullString());
				}
				continue;
			}
			if (last && statement is ReturnStatementSyntax { Expression: null or LiteralExpressionSyntax { RawKind: (int)SyntaxKind.TrueLiteralExpression } })
			{
				continue;
			}
			pieces.Append(Rewrite(statement, replacements));
		}
		var body = Reindent(pieces.ToString());
		string target = typeCounts.Count == 0 ? "IsaClient.Core.GeneralCommands" : typeCounts.OrderByDescending(kv => kv.Value).First().Key;
		string longest = names.OrderByDescending(n => n.Length).First();
		return new Command
		{
			Names = names,
			Source = source,
			TargetType = target,
			MethodName = names.Select(n => MethodNames.GetValueOrDefault(n)).FirstOrDefault(m => m != null) ?? vocabulary.Pascal(longest) + "Command",
			Body = body,
			Usings = usings,
		};
	}

	private static void AddUsing(ISymbol symbol, HashSet<string> usings)
	{
		if (symbol is INamedTypeSymbol type && type.ContainingType == null && type.ContainingNamespace is { IsGlobalNamespace: false } ns)
		{
			usings.Add("using " + ns.ToDisplayString() + ";");
		}
		else if (symbol is IAliasSymbol alias)
		{
			usings.Add($"using {alias.Name} = {alias.Target.ToDisplayString()};");
		}
	}

	/// <summary>True when the break leaves the command switch (not a loop or switch inside the command).</summary>
	private static bool TargetsOuterSwitch(BreakStatementSyntax node, SyntaxNode root)
	{
		if (node == root)
		{
			return true;
		}
		foreach (var ancestor in node.Ancestors())
		{
			if (ancestor is ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax or SwitchStatementSyntax)
			{
				return false;
			}
			if (ancestor == root)
			{
				return true;
			}
		}
		return true;
	}

	private static bool InsideLambda(SyntaxNode node, SyntaxNode root)
	{
		foreach (var ancestor in node.Ancestors())
		{
			if (ancestor == root)
			{
				return false;
			}
			if (ancestor is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax or LocalFunctionStatementSyntax)
			{
				return true;
			}
		}
		return false;
	}

	private static string Rewrite(StatementSyntax statement, Dictionary<SyntaxNode, string> replacements)
	{
		var changes = replacements.Where(kv => statement.FullSpan.Contains(kv.Key.Span))
			.Where(kv => !replacements.Keys.Any(other => other != kv.Key && other.Span.Contains(kv.Key.Span) && statement.FullSpan.Contains(other.Span)))
			.Select(kv => new TextChange(new TextSpan(kv.Key.Span.Start - statement.FullSpan.Start, kv.Key.Span.Length), kv.Value))
			.OrderBy(c => c.Span.Start);
		return SourceText.From(statement.ToFullString()).WithChanges(changes).ToString();
	}

	private static string Reindent(string text)
	{
		var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
		while (lines.Count > 0 && lines[^1].Trim().Length == 0)
		{
			lines.RemoveAt(lines.Count - 1);
		}
		while (lines.Count > 0 && lines[0].Trim().Length == 0)
		{
			lines.RemoveAt(0);
		}
		int min = lines.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart('\t').Length).DefaultIfEmpty(0).Min();
		var sb = new StringBuilder();
		foreach (var line in lines)
		{
			sb.Append(line.Trim().Length == 0 ? "" : "\t\t" + line.Substring(Math.Min(min, line.Length - line.TrimStart('\t').Length))).Append('\n');
		}
		return sb.ToString();
	}
}

/// <summary>Splits run-together command names into words using the words the code base itself uses.</summary>
public sealed class Vocabulary
{
	private readonly Dictionary<string, double> _cost = new();

	public static Vocabulary Build(string root)
	{
		var counts = new Dictionary<string, int>();
		var splitter = new Regex("[A-Z]?[a-z]+|[A-Z]+(?![a-z])");
		foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
		{
			if (file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) || file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
			{
				continue;
			}
			// identifiers only: strings and comments hold the run-together command names themselves
			var code = Regex.Replace(File.ReadAllText(file), "//[^\\n]*|/\\*.*?\\*/|@\"(?:\"\"|[^\"])*\"|\"(?:\\\\.|[^\"\\\\\\n])*\"", "", RegexOptions.Singleline);
			foreach (Match identifier in Regex.Matches(code, "[A-Za-z][A-Za-z0-9]+"))
			{
				if (identifier.Value.All(char.IsLower) && identifier.Value.Length > 7)
				{
					continue; // long all-lowercase identifiers are run-together words too
				}
				foreach (Match word in splitter.Matches(identifier.Value))
				{
					var w = word.Value.ToLowerInvariant();
					if (w.Length >= 2)
					{
						counts[w] = counts.GetValueOrDefault(w) + 1;
					}
				}
			}
		}
		var vocabulary = new Vocabulary();
		double total = counts.Values.Sum();
		foreach (var (word, count) in counts)
		{
			vocabulary._cost[word] = -Math.Log(count / total);
		}
		return vocabulary;
	}

	/// <summary>"crystalkillaura" -> "CrystalKillAura" (best split by word frequency; unknown letters cost a lot).</summary>
	public string Pascal(string name)
	{
		var clean = new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
		int n = clean.Length;
		var best = new double[n + 1];
		var from = new int[n + 1];
		for (int i = 1; i <= n; i++)
		{
			best[i] = double.MaxValue;
			for (int j = Math.Max(0, i - 20); j < i; j++)
			{
				var piece = clean[j..i];
				double cost = _cost.TryGetValue(piece, out var c) ? c : (piece.All(char.IsDigit) ? 5 : 25 + 8 * piece.Length);
				if (best[j] + cost < best[i])
				{
					best[i] = best[j] + cost;
					from[i] = j;
				}
			}
		}
		var parts = new List<string>();
		for (int i = n; i > 0; i = from[i])
		{
			parts.Insert(0, clean[from[i]..i]);
		}
		return string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
	}
}
