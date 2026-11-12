// IsaClient refactoring tool: semantic (Roslyn) moves and renames over the whole client source.
//
//   dotnet run -c Release -- check                 compile in memory, print the errors
//   dotnet run -c Release -- move <plan.txt>       move static members to other classes, rewriting every reference
//   dotnet run -c Release -- rename <plan.txt>     rename symbols (types, members), everywhere
//   dotnet run -c Release -- members <Type>        list a type's members with their reference counts per file
//
// move plan lines:    Source.Type.member -> Target.Namespace.TargetClass      (# comments allowed)
// rename plan lines:  Full.Type.Name[.member] => NewName
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;

namespace IsaRefactor;

public static class Program
{
	public static async Task<int> Main(string[] args)
	{
		if (args.Length == 0)
		{
			Console.WriteLine("usage: check | move <plan> | rename <plan> | members <Type>");
			return 1;
		}
		var src = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src"));
		if (!Directory.Exists(src))
		{
			src = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "src"));
		}
		var project = SourceProject.Load(src);
		switch (args[0])
		{
		case "check":
			return await Check(project) == 0 ? 0 : 2;
		case "move":
			await MoveMembers.Run(project, File.ReadAllLines(args[1]));
			return 0;
		case "rename":
			await RenameSymbols.Run(project, File.ReadAllLines(args[1]));
			return 0;
		case "members":
			await Members(project, args[1]);
			return 0;
		case "chat-calls":
			await ChatCalls.Run(project, args.Skip(1).ToArray());
			return 0;
		case "find-recursion":
			await Recursion.Run(project, args.Skip(1).ToArray());
			return 0;
		case "check-init-order":
			await InitOrder.Run(project, args.Skip(1).ToArray());
			return 0;
		case "tidy":
			await Tidy.Run(project, args.Skip(1).ToArray());
			return 0;
		case "extract-commands":
			await ExtractCommands.Run(project, File.ReadAllLines(args[1]), args.Contains("--dry"));
			return 0;
		case "wrap-text-sinks":
			await WrapTextSinks.Run(project, args.Skip(1).ToArray());
			return 0;
		case "extract-strings":
			await ExtractStrings.Run(project, args.Skip(1).ToArray());
			return 0;
		case "rename-batch":
			await RenameBatch.Run(project, File.ReadAllLines(args[1]));
			return 0;
		case "list-members":
			await RenameBatch.ListMembers(project, args[1]);
			return 0;
		case "retarget":
			await RetargetTypes.Run(project, File.ReadAllLines(args[1]));
			return 0;
		case "inline-cctor":
			await InlineStaticConstructor.Run(project, args[1]);
			return 0;
		default:
			Console.WriteLine("unknown command " + args[0]);
			return 1;
		}
	}

	public static async Task<int> Check(SourceProject project)
	{
		var compilation = await project.Workspace.CurrentSolution.Projects.Single().GetCompilationAsync();
		var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
		foreach (var error in errors.Take(80))
		{
			var span = error.Location.GetLineSpan();
			Console.WriteLine($"{Path.GetRelativePath(project.Root, span.Path)}({span.StartLinePosition.Line + 1}): {error.Id} {error.GetMessage()}");
		}
		Console.WriteLine($"{errors.Count} error(s)");
		return errors.Count;
	}

	private static async Task Members(SourceProject project, string typeName)
	{
		var solution = project.Workspace.CurrentSolution;
		var compilation = await solution.Projects.Single().GetCompilationAsync();
		var type = Symbols.FindType(compilation, typeName);
		foreach (var member in type.GetMembers().Where(m => !m.IsImplicitlyDeclared).OrderBy(m => m.Name))
		{
			if (member is IMethodSymbol { MethodKind: not MethodKind.Ordinary })
			{
				continue;
			}
			var refs = await SymbolFinder.FindReferencesAsync(member, solution);
			var files = refs.SelectMany(r => r.Locations).GroupBy(l => Path.GetFileName(l.Document.FilePath))
				.Select(g => g.Key + ":" + g.Count());
			var declFile = Path.GetFileName(member.Locations.FirstOrDefault()?.SourceTree?.FilePath ?? "?");
			Console.WriteLine($"{member.Kind}\t{(member.IsStatic ? "static" : "inst")}\t{member.Name}\t[{declFile}]\t{string.Join(" ", files)}");
		}
	}
}

/// <summary>The client project (src/Terraria.csproj) as an in-memory Roslyn workspace.</summary>
public sealed class SourceProject
{
	public string Root { get; private init; }

	public AdhocWorkspace Workspace { get; private init; }

	private readonly Dictionary<string, Encoding> _encodings = new(StringComparer.OrdinalIgnoreCase);

	public static SourceProject Load(string root)
	{
		var work = Path.Combine(Path.GetDirectoryName(root), "tools", "refactor", ".work");
		Directory.CreateDirectory(work);
		var msbuild = Path.Combine(work, "msb.json");
		var json = JsonDocument.Parse(File.ReadAllText(msbuild)).RootElement;
		var defines = json.GetProperty("Properties").GetProperty("DefineConstants").GetString().Split(';', StringSplitOptions.RemoveEmptyEntries);
		var references = json.GetProperty("Items").GetProperty("ReferencePathWithRefAssemblies").EnumerateArray()
			.Select(r => (MetadataReference)MetadataReference.CreateFromFile(r.GetProperty("Identity").GetString())).ToList();
		var parse = new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.Parse, preprocessorSymbols: defines);
		var options = new CSharpCompilationOptions(OutputKind.WindowsApplication, allowUnsafe: true, platform: Platform.X86)
			.WithAssemblyIdentityComparer(DesktopAssemblyIdentityComparer.Default);
		var project = new SourceProject { Root = root, Workspace = new AdhocWorkspace() };
		var projectId = ProjectId.CreateNewId();
		var documents = new List<DocumentInfo>();
		// The SDK compiles every .cs under src except bin/obj: new files from earlier moves count too.
		var paths = json.GetProperty("Items").GetProperty("Compile").EnumerateArray()
			.Select(item => Path.GetFullPath(Path.Combine(root, item.GetProperty("Identity").GetString())))
			.Concat(Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories).Where(p =>
			{
				var relative = Path.GetRelativePath(root, p).Replace('\\', '/');
				return !relative.StartsWith("bin/") && !relative.StartsWith("obj/") && !relative.StartsWith(".build-home/");
			}))
			.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase);
		foreach (var path in paths)
		{
			if (!File.Exists(path))
			{
				continue;
			}
			var bytes = File.ReadAllBytes(path);
			bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
			var encoding = new UTF8Encoding(bom);
			project._encodings[path] = encoding;
			var text = SourceText.From(encoding.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0)), encoding);
			documents.Add(DocumentInfo.Create(DocumentId.CreateNewId(projectId), Path.GetFileName(path), filePath: path,
				loader: TextLoader.From(TextAndVersion.Create(text, VersionStamp.Create(), path))));
		}
		var info = ProjectInfo.Create(projectId, VersionStamp.Create(), "Terraria", "Terraria", LanguageNames.CSharp,
			compilationOptions: options, parseOptions: parse, metadataReferences: references, documents: documents);
		project.Workspace.AddProject(info);
		Console.WriteLine($"loaded {documents.Count} files");
		return project;
	}

	/// <summary>Writes every document of <paramref name="solution"/> whose text differs from disk, keeping its encoding.</summary>
	public async Task Save(Solution solution)
	{
		int written = 0;
		foreach (var document in solution.Projects.Single().Documents)
		{
			var text = await document.GetTextAsync();
			var path = document.FilePath;
			var encoding = _encodings.TryGetValue(path, out var e) ? e : new UTF8Encoding(true);
			var current = File.Exists(path) ? File.ReadAllText(path) : null;
			var newText = text.ToString();
			if (current == newText)
			{
				continue;
			}
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			File.WriteAllText(path, newText, encoding);
			written++;
		}
		Console.WriteLine($"wrote {written} file(s)");
	}

	public static string NewLineOf(string text) => text.Contains("\r\n") ? "\r\n" : "\n";
}

public static class Symbols
{
	public static INamedTypeSymbol FindType(Compilation compilation, string name)
	{
		var byMetadata = compilation.GetTypeByMetadataName(name);
		if (byMetadata != null)
		{
			return byMetadata;
		}
		var matches = compilation.GetSymbolsWithName(name.Split('.').Last(), SymbolFilter.Type).OfType<INamedTypeSymbol>()
			.Where(t => t.ToDisplayString() == name || t.Name == name).ToList();
		if (matches.Count != 1)
		{
			throw new InvalidOperationException($"type '{name}': {matches.Count} matches");
		}
		return matches[0];
	}

	/// <summary>"Ns.Type.member" -> the type and the member name (types may be nested: Ns.Outer.Inner.member).</summary>
	public static (INamedTypeSymbol Type, string Member) SplitMember(Compilation compilation, string spec)
	{
		int dot = spec.LastIndexOf('.');
		return (FindType(compilation, spec[..dot]), spec[(dot + 1)..]);
	}
}

/// <summary>Moves static members (fields, properties, methods, events, nested types) to other static classes.</summary>
public static class MoveMembers
{
	private sealed record Target(string Namespace, string Class)
	{
		public string FullName => Namespace + "." + Class;
	}

	public static async Task Run(SourceProject project, string[] plan)
	{
		var solution = project.Workspace.CurrentSolution;
		var compilation = await solution.Projects.Single().GetCompilationAsync();
		var moves = new Dictionary<ISymbol, Target>(SymbolEqualityComparer.Default);
		foreach (var raw in plan)
		{
			var line = raw.Split('#')[0].Trim();
			if (line.Length == 0)
			{
				continue;
			}
			var parts = line.Split("->", StringSplitOptions.TrimEntries);
			var (type, memberName) = Symbols.SplitMember(compilation, parts[0]);
			int dot = parts[1].LastIndexOf('.');
			var target = new Target(parts[1][..dot], parts[1][(dot + 1)..]);
			var members = type.GetMembers(memberName).Where(m => !m.IsImplicitlyDeclared).ToList();
			if (members.Count == 0)
			{
				throw new InvalidOperationException("no member " + parts[0]);
			}
			foreach (var member in members)
			{
				if (!member.IsStatic && member is not INamedTypeSymbol && member is not IFieldSymbol { IsConst: true })
				{
					throw new InvalidOperationException($"{parts[0]} is not static");
				}
				moves[member] = target;
			}
		}
		Console.WriteLine($"moving {moves.Count} symbol(s)");

		// Declarations being moved, and where each one goes.
		var declarations = new Dictionary<SyntaxNode, Target>();
		foreach (var (symbol, target) in moves)
		{
			foreach (var reference in symbol.DeclaringSyntaxReferences)
			{
				var node = await reference.GetSyntaxAsync();
				if (node is VariableDeclaratorSyntax declarator)
				{
					var field = (FieldDeclarationSyntax)declarator.Parent.Parent;
					foreach (var other in field.Declaration.Variables)
					{
						var otherSymbol = compilation.GetSemanticModel(other.SyntaxTree).GetDeclaredSymbol(other);
						if (!moves.TryGetValue(otherSymbol, out var otherTarget) || otherTarget != target)
						{
							throw new InvalidOperationException($"field declaration '{field}' must move together");
						}
					}
					node = field;
				}
				else if (node is AccessorDeclarationSyntax)
				{
					continue;
				}
				declarations[node] = target;
			}
		}

		// Edits per document: (span, new text).
		var edits = new Dictionary<DocumentId, List<TextChange>>();
		var usingsNeeded = new Dictionary<DocumentId, HashSet<string>>();
		void Edit(DocumentId id, TextSpan span, string text)
		{
			if (!edits.TryGetValue(id, out var list))
			{
				edits[id] = list = new List<TextChange>();
			}
			if (list.Any(e => e.Span == span))
			{
				return;
			}
			list.Add(new TextChange(span, text));
		}

		Target NewContainerOf(SyntaxNode node, SemanticModel model)
		{
			foreach (var ancestor in node.AncestorsAndSelf())
			{
				if (declarations.TryGetValue(ancestor, out var target))
				{
					return target;
				}
			}
			var typeDecl = node.AncestorsAndSelf().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault();
			if (typeDecl == null)
			{
				return null;
			}
			var symbol = model.GetDeclaredSymbol(typeDecl);
			return symbol == null ? null : new Target(symbol.ContainingNamespace.ToDisplayString(), symbol.Name);
		}

		string NamespaceOf(SyntaxNode node)
		{
			var ns = node.AncestorsAndSelf().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
			return ns?.Name.ToString() ?? "";
		}

		// 1) Every reference to a moved symbol points at its new class.
		foreach (var (symbol, target) in moves)
		{
			var found = await SymbolFinder.FindReferencesAsync(symbol, solution);
			foreach (var location in found.SelectMany(r => r.Locations))
			{
				if (location.IsImplicit)
				{
					continue;
				}
				var document = location.Document;
				var root = await document.GetSyntaxRootAsync();
				var model = await document.GetSemanticModelAsync();
				var node = root.FindNode(location.Location.SourceSpan, findInsideTrivia: true, getInnermostNodeForTie: true);
				if (node is not SimpleNameSyntax name)
				{
					name = node.DescendantNodesAndSelf().OfType<SimpleNameSyntax>().FirstOrDefault(n => n.Span == location.Location.SourceSpan);
					if (name == null)
					{
						Console.WriteLine($"  ? {Path.GetFileName(document.FilePath)} {node.Kind()} '{node}'");
						continue;
					}
				}
				var container = NewContainerOf(name, model);
				bool sameClass = container != null && container == target;
				switch (name.Parent)
				{
				case MemberAccessExpressionSyntax access when access.Name == name:
					Edit(document.Id, access.Expression.Span, target.Class);
					break;
				case QualifiedNameSyntax qualified when qualified.Right == name:
					Edit(document.Id, qualified.Left.Span, target.Class);
					break;
				case QualifiedCrefSyntax qualifiedCref:
					Edit(document.Id, qualifiedCref.Container.Span, target.Class);
					break;
				case NameMemberCrefSyntax when !sameClass:
					Edit(document.Id, name.Span, target.Class + "." + name);
					break;
				default:
					if (!sameClass)
					{
						Edit(document.Id, name.Span, target.Class + "." + name);
					}
					break;
				}
				if (!sameClass && !NamespaceOf(name).StartsWith(target.Namespace, StringComparison.Ordinal) && !declarations.Keys.Any(d => d.SyntaxTree == name.SyntaxTree && d.Span.Contains(name.Span)))
				{
					if (!usingsNeeded.TryGetValue(document.Id, out var set))
					{
						usingsNeeded[document.Id] = set = new HashSet<string>();
					}
					set.Add(target.Namespace);
				}
			}
		}

		// 2) Inside moved declarations, unqualified uses of members that stay behind get their old class name.
		foreach (var (declaration, target) in declarations)
		{
			var document = solution.GetDocument(declaration.SyntaxTree);
			var model = await document.GetSemanticModelAsync();
			var source = model.GetDeclaredSymbol(declaration.Ancestors().OfType<BaseTypeDeclarationSyntax>().First());
			foreach (var name in declaration.DescendantNodes(descendIntoTrivia: true).OfType<SimpleNameSyntax>())
			{
				if (name.Parent is MemberAccessExpressionSyntax access && access.Name == name || name.Parent is QualifiedNameSyntax q && q.Right == name
					|| name.Parent is MemberBindingExpressionSyntax || name.Parent is QualifiedCrefSyntax)
				{
					continue;
				}
				var symbol = model.GetSymbolInfo(name).Symbol ?? model.GetSymbolInfo(name).CandidateSymbols.FirstOrDefault();
				if (symbol == null || moves.ContainsKey(symbol.OriginalDefinition) || moves.ContainsKey(symbol))
				{
					continue;
				}
				bool member = symbol is IFieldSymbol or IPropertySymbol or IMethodSymbol { MethodKind: MethodKind.Ordinary } or IEventSymbol or INamedTypeSymbol;
				if (member && SymbolEqualityComparer.Default.Equals(symbol.ContainingType?.OriginalDefinition, source)
					&& (symbol.IsStatic || symbol is INamedTypeSymbol || symbol is IFieldSymbol { IsConst: true }))
				{
					Edit(document.Id, name.Span, source.Name + "." + name);
				}
				else if (member && symbol.ContainingType != null && !(symbol.IsStatic || symbol is INamedTypeSymbol) && SymbolEqualityComparer.Default.Equals(symbol.ContainingType.OriginalDefinition, source))
				{
					throw new InvalidOperationException($"{declaration.GetLocation().GetLineSpan()} uses instance member {symbol.Name}");
				}
			}
		}

		// 2b) A private member still used from its old class (or elsewhere) becomes internal; static classes have no protected.
		foreach (var (declaration, target) in declarations)
		{
			if (declaration is not MemberDeclarationSyntax memberDeclaration)
			{
				continue;
			}
			var document = solution.GetDocument(declaration.SyntaxTree);
			var model = await document.GetSemanticModelAsync();
			ISymbol symbol = declaration is FieldDeclarationSyntax field
				? model.GetDeclaredSymbol(field.Declaration.Variables[0])
				: model.GetDeclaredSymbol(declaration);
			var modifiers = memberDeclaration.Modifiers;
			var protectedToken = modifiers.FirstOrDefault(m => m.IsKind(SyntaxKind.ProtectedKeyword));
			var privateToken = modifiers.FirstOrDefault(m => m.IsKind(SyntaxKind.PrivateKeyword));
			bool usedOutside = false;
			if (privateToken != default && symbol != null)
			{
				var found = await SymbolFinder.FindReferencesAsync(symbol, solution);
				usedOutside = found.SelectMany(r => r.Locations).Any(l =>
				{
					var root = l.Document.GetSyntaxRootAsync().Result;
					var node = root.FindNode(l.Location.SourceSpan, findInsideTrivia: true);
					var refModel = l.Document.GetSemanticModelAsync().Result;
					var container = NewContainerOf(node, refModel);
					return container == null || container != target;
				});
			}
			if (protectedToken != default)
			{
				Edit(document.Id, protectedToken.Span, "internal");
				if (privateToken != default)
				{
					Edit(document.Id, privateToken.Span, "");
				}
			}
			else if (usedOutside)
			{
				Edit(document.Id, privateToken.Span, "internal");
			}
		}

		// 3) Cut the declarations (with their own edits applied) and remove them from their files. #region / #endregion
		//    lines in a declaration's leading trivia stay in the old file, so both files keep balanced regions.
		var moved = new Dictionary<Target, List<(string Text, ImmutableArray<UsingDirectiveSyntax> Usings, string SourceNamespace)>>();
		foreach (var (declaration, target) in declarations.OrderBy(d => d.Key.SyntaxTree.FilePath).ThenBy(d => d.Key.SpanStart))
		{
			var document = solution.GetDocument(declaration.SyntaxTree);
			var text = await document.GetTextAsync();
			var span = declaration.FullSpan;
			var inner = edits.TryGetValue(document.Id, out var list) ? list.Where(e => span.Contains(e.Span)).ToList() : new List<TextChange>();
			var piece = text.GetSubText(span).WithChanges(inner.Select(e => new TextChange(new TextSpan(e.Span.Start - span.Start, e.Span.Length), e.NewText))).ToString();
			list?.RemoveAll(e => span.Contains(e.Span));
			var newLine = SourceProject.NewLineOf(text.ToString());
			var pieceLines = piece.Replace("\r\n", "\n").Split('\n');
			bool IsRegionLine(string l) => l.TrimStart().StartsWith("#region") || l.TrimStart().StartsWith("#endregion");
			var kept = pieceLines.Where(IsRegionLine).ToList();
			piece = string.Join(newLine, pieceLines.Where(l => !IsRegionLine(l)));
			Edit(document.Id, span, kept.Count == 0 ? "" : string.Concat(kept.Select(l => l + newLine)));
			var unit = (CompilationUnitSyntax)declaration.SyntaxTree.GetRoot();
			if (!moved.TryGetValue(target, out var pieces))
			{
				moved[target] = pieces = new();
			}
			pieces.Add((piece, unit.Usings.ToImmutableArray(), NamespaceOf(declaration)));
		}

		// 4) Using directives for files that now reference new classes.
		foreach (var (id, namespaces) in usingsNeeded)
		{
			var document = solution.GetDocument(id);
			var root = (CompilationUnitSyntax)await document.GetSyntaxRootAsync();
			var existing = root.Usings.Select(u => u.Name?.ToString()).ToHashSet();
			var missing = namespaces.Where(n => !existing.Contains(n)).OrderBy(n => n).ToList();
			if (missing.Count == 0)
			{
				continue;
			}
			var newLine = SourceProject.NewLineOf(root.ToFullString());
			var insert = string.Concat(missing.Select(n => $"using {n};{newLine}"));
			int position = root.Usings.Count > 0 ? root.Usings.Last().FullSpan.End : root.FullSpan.Start;
			Edit(id, new TextSpan(position, 0), insert);
		}

		foreach (var (id, list) in edits)
		{
			var ordered = list.OrderBy(e => e.Span.Start).ToList();
			for (int i = 1; i < ordered.Count; i++)
			{
				if (ordered[i].Span.Start < ordered[i - 1].Span.End)
				{
					throw new InvalidOperationException($"overlapping edits in {solution.GetDocument(id).FilePath}: {ordered[i - 1].Span} {ordered[i].Span}");
				}
			}
			var document = solution.GetDocument(id);
			var text = await document.GetTextAsync();
			solution = solution.WithDocumentText(id, text.WithChanges(ordered));
		}

		// 5) Append to the target classes (existing file or a new one).
		foreach (var (target, pieces) in moved)
		{
			solution = await AppendToTarget(project, solution, target.Namespace, target.Class, pieces);
		}
		await project.Save(solution);
	}

	private static async Task<Solution> AppendToTarget(SourceProject project, Solution solution, string ns, string className,
		List<(string Text, ImmutableArray<UsingDirectiveSyntax> Usings, string SourceNamespace)> pieces)
	{
		var compilation = await solution.Projects.Single().GetCompilationAsync();
		var existingType = compilation.GetTypeByMetadataName(ns + "." + className);
		if (existingType != null)
		{
			var reference = existingType.DeclaringSyntaxReferences.First();
			var declaration = (TypeDeclarationSyntax)await reference.GetSyntaxAsync();
			var document = solution.GetDocument(reference.SyntaxTree);
			var text = await document.GetTextAsync();
			var newLine = SourceProject.NewLineOf(text.ToString());
			var body = string.Join("", pieces.Select(p => newLine + Normalize(p.Text.Trim('\r', '\n'), newLine) + newLine));
			solution = solution.WithDocumentText(document.Id, text.WithChanges(new TextChange(new TextSpan(declaration.CloseBraceToken.SpanStart, 0), body.TrimStart('\r', '\n') + "")));
			// keep the closing brace on its own line
			var root = (CompilationUnitSyntax)await solution.GetDocument(document.Id).GetSyntaxRootAsync();
			var missing = pieces.SelectMany(p => p.Usings).Select(u => u.ToString()).Concat(UsingsFor(pieces))
				.Except(root.Usings.Select(u => u.ToString())).Distinct().OrderBy(u => u).ToList();
			if (missing.Count > 0)
			{
				var t = await solution.GetDocument(document.Id).GetTextAsync();
				int position = root.Usings.Count > 0 ? root.Usings.Last().FullSpan.End : 0;
				solution = solution.WithDocumentText(document.Id, t.WithChanges(new TextChange(new TextSpan(position, 0), string.Concat(missing.Select(u => u + newLine)))));
			}
			return solution;
		}
		var nl = "\r\n";
		var usings = pieces.SelectMany(p => p.Usings).Select(u => u.ToString()).Concat(UsingsFor(pieces)).Distinct()
			.OrderBy(u => u.Contains('=') ? 1 : 0).ThenBy(u => u.StartsWith("using System") ? 0 : 1).ThenBy(u => u).ToList();
		var sb = new StringBuilder();
		foreach (var u in usings)
		{
			sb.Append(u).Append(nl);
		}
		sb.Append(nl).Append("namespace ").Append(ns).Append(';').Append(nl).Append(nl);
		sb.Append("public static partial class ").Append(className).Append(nl).Append('{').Append(nl);
		sb.Append(string.Join(nl, pieces.Select(p => Normalize(p.Text.Trim('\r', '\n'), nl) + nl)));
		sb.Append('}').Append(nl);
		var folder = Path.Combine(project.Root, ns);
		var path = Path.Combine(folder, className + ".cs");
		if (File.Exists(path) || solution.Projects.Single().Documents.Any(d => string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase)))
		{
			throw new InvalidOperationException("file exists but type not found: " + path);
		}
		var id = DocumentId.CreateNewId(solution.Projects.Single().Id);
		return solution.AddDocument(DocumentInfo.Create(id, className + ".cs", filePath: path,
			loader: TextLoader.From(TextAndVersion.Create(SourceText.From(sb.ToString(), new UTF8Encoding(true)), VersionStamp.Create(), path))));
	}

	/// <summary>The moved code was written inside its source namespace: keep those names visible.</summary>
	private static IEnumerable<string> UsingsFor(List<(string Text, ImmutableArray<UsingDirectiveSyntax> Usings, string SourceNamespace)> pieces)
	{
		foreach (var ns in pieces.Select(p => p.SourceNamespace).Distinct())
		{
			var parts = ns.Split('.');
			for (int i = 1; i <= parts.Length; i++)
			{
				yield return "using " + string.Join(".", parts.Take(i)) + ";";
			}
		}
	}

	private static string Normalize(string text, string newLine) => text.Replace("\r\n", "\n").Replace("\n", newLine);
}

/// <summary>
/// The decompiler turns field initializers into one static constructor. This puts each <c>field = value;</c> of that
/// constructor back on the field (as its initializer), so a field keeps its value wherever it is moved.
/// </summary>
public static class InlineStaticConstructor
{
	public static async Task Run(SourceProject project, string typeName)
	{
		var solution = project.Workspace.CurrentSolution;
		var compilation = await solution.Projects.Single().GetCompilationAsync();
		var type = Symbols.FindType(compilation, typeName);
		var cctor = type.StaticConstructors.FirstOrDefault(c => !c.IsImplicitlyDeclared);
		if (cctor == null)
		{
			Console.WriteLine("no static constructor");
			return;
		}
		var ctorSyntax = (ConstructorDeclarationSyntax)await cctor.DeclaringSyntaxReferences[0].GetSyntaxAsync();
		var ctorDocument = solution.GetDocument(ctorSyntax.SyntaxTree);
		var ctorModel = await ctorDocument.GetSemanticModelAsync();
		var edits = new Dictionary<DocumentId, List<TextChange>>();
		void Edit(DocumentId id, TextSpan span, string text)
		{
			if (!edits.TryGetValue(id, out var list))
			{
				edits[id] = list = new List<TextChange>();
			}
			list.Add(new TextChange(span, text));
		}
		int moved = 0;
		var assigned = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
		var remaining = new List<StatementSyntax>();
		foreach (var statement in ctorSyntax.Body.Statements)
		{
			if (statement is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.SimpleAssignmentExpression } assignment }
				&& ctorModel.GetSymbolInfo(assignment.Left).Symbol is IFieldSymbol field
				&& SymbolEqualityComparer.Default.Equals(field.ContainingType, type) && assigned.Add(field))
			{
				var declarator = (VariableDeclaratorSyntax)await field.DeclaringSyntaxReferences[0].GetSyntaxAsync();
				var declaration = (VariableDeclarationSyntax)declarator.Parent;
				// Only plain single-variable fields without an initializer, and values that do not read other
				// fields of this type that are assigned later in the constructor (their order would change).
				bool readsLater = assignment.Right.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Any(n =>
					ctorModel.GetSymbolInfo(n).Symbol is IFieldSymbol f && SymbolEqualityComparer.Default.Equals(f.ContainingType, type) && !assigned.Contains(f));
				if (declarator.Initializer == null && declaration.Variables.Count == 1 && !readsLater)
				{
					var document = solution.GetDocument(declarator.SyntaxTree);
					var value = assignment.Right.ToFullString().Trim();
					Edit(document.Id, new TextSpan(declarator.Identifier.Span.End, 0), " = " + value);
					Edit(ctorDocument.Id, statement.FullSpan, "");
					moved++;
					continue;
				}
			}
			remaining.Add(statement);
		}
		if (remaining.Count == 0)
		{
			Edit(ctorDocument.Id, ctorSyntax.FullSpan, "");
		}
		foreach (var (id, list) in edits)
		{
			var text = await solution.GetDocument(id).GetTextAsync();
			var ordered = list.OrderBy(e => e.Span.Start).ToList();
			// Statement removals inside a removed constructor are dropped.
			ordered = ordered.Where(e => !ordered.Any(o => o.Span != e.Span && o.Span.Contains(e.Span))).ToList();
			solution = solution.WithDocumentText(id, text.WithChanges(ordered));
		}
		Console.WriteLine($"{moved} assignment(s) became field initializers, {remaining.Count} statement(s) left in the static constructor");
		foreach (var statement in remaining)
		{
			Console.WriteLine("  left: " + statement.ToString().Split('\n')[0].Trim());
		}
		await project.Save(solution);
	}
}

/// <summary>
/// Gives top-level types a new name and/or namespace: <c>Old.Ns.Type => New.Ns.NewName</c>. References are rewritten
/// (qualified ones get the new namespace, others a using directive), the moved files get usings for what they could see
/// from their old namespace, and relative namespace names inside them become fully qualified.
/// </summary>
public static class RetargetTypes
{
	public static async Task Run(SourceProject project, string[] plan)
	{
		var solution = project.Workspace.CurrentSolution;
		var targets = new List<(string Old, string NewNamespace, string NewName)>();
		foreach (var raw in plan)
		{
			var line = raw.Split('#')[0].Trim();
			if (line.Length == 0)
			{
				continue;
			}
			var parts = line.Split("=>", StringSplitOptions.TrimEntries);
			int dot = parts[1].LastIndexOf('.');
			targets.Add((parts[0], parts[1][..dot], parts[1][(dot + 1)..]));
		}

		// 1) Renames (Roslyn Renamer keeps every reference right).
		var renamed = new List<(string FullName, string NewNamespace)>();
		foreach (var (old, newNamespace, newName) in targets)
		{
			var compilation = await solution.Projects.Single().GetCompilationAsync();
			var type = compilation.GetTypeByMetadataName(old) ?? throw new InvalidOperationException("type not found: " + old);
			if (type.Name != newName)
			{
				solution = await Renamer.RenameSymbolAsync(solution, type, new SymbolRenameOptions(), newName);
			}
			renamed.Add((type.ContainingNamespace.ToDisplayString() + "." + newName, newNamespace));
		}

		// 2) Namespaces.
		var compilation2 = await solution.Projects.Single().GetCompilationAsync();
		var edits = new Dictionary<DocumentId, List<TextChange>>();
		var usings = new Dictionary<DocumentId, HashSet<string>>();
		void Edit(DocumentId id, TextSpan span, string text)
		{
			if (!edits.TryGetValue(id, out var list))
			{
				edits[id] = list = new List<TextChange>();
			}
			if (!list.Any(e => e.Span == span))
			{
				list.Add(new TextChange(span, text));
			}
		}
		void Using(DocumentId id, string ns)
		{
			if (!usings.TryGetValue(id, out var set))
			{
				usings[id] = set = new HashSet<string>();
			}
			set.Add(ns);
		}
		var movedFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // file -> new namespace
		var types = new List<(INamedTypeSymbol Type, string NewNamespace)>();
		foreach (var (fullName, newNamespace) in renamed)
		{
			var type = compilation2.GetTypeByMetadataName(fullName) ?? throw new InvalidOperationException("lost " + fullName);
			types.Add((type, newNamespace));
			foreach (var reference in type.DeclaringSyntaxReferences)
			{
				var path = reference.SyntaxTree.FilePath;
				if (movedFiles.TryGetValue(path, out var other) && other != newNamespace)
				{
					throw new InvalidOperationException($"{Path.GetFileName(path)} holds types for {other} and {newNamespace}: split it first");
				}
				movedFiles[path] = newNamespace;
			}
		}
		string FileNamespace(SyntaxNode node) => node.AncestorsAndSelf().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString() ?? "";

		foreach (var (type, newNamespace) in types)
		{
			var oldNamespace = type.ContainingNamespace.ToDisplayString();
			var found = await SymbolFinder.FindReferencesAsync(type, solution);
			foreach (var location in found.SelectMany(r => r.Locations))
			{
				if (location.IsImplicit)
				{
					continue;
				}
				var document = location.Document;
				var root = await document.GetSyntaxRootAsync();
				var node = root.FindNode(location.Location.SourceSpan, findInsideTrivia: true, getInnermostNodeForTie: true);
				var name = node as SimpleNameSyntax ?? node.DescendantNodesAndSelf().OfType<SimpleNameSyntax>().FirstOrDefault(n => n.Span == location.Location.SourceSpan);
				if (name == null)
				{
					continue;
				}
				// Qualified by its namespace: Old.Namespace.X / global::Old.Namespace.X
				if (name.Parent is QualifiedNameSyntax q && q.Right == name)
				{
					Edit(document.Id, q.Left.Span, newNamespace);
					continue;
				}
				if (name.Parent is MemberAccessExpressionSyntax m && m.Name == name)
				{
					var model = await document.GetSemanticModelAsync();
					if (model.GetSymbolInfo(m.Expression).Symbol is INamespaceSymbol)
					{
						Edit(document.Id, m.Expression.Span, newNamespace);
						continue;
					}
				}
				if (name.Parent is QualifiedCrefSyntax qc)
				{
					Edit(document.Id, qc.Container.Span, newNamespace);
					continue;
				}
				var fileNamespace = movedFiles.TryGetValue(document.FilePath, out var movedTo) ? movedTo : FileNamespace(name);
				if (fileNamespace != newNamespace && !fileNamespace.StartsWith(newNamespace + "."))
				{
					Using(document.Id, newNamespace);
				}
			}
		}

		// 3) The moved files: new namespace, usings for what the old namespace made visible, relative names qualified.
		foreach (var (path, newNamespace) in movedFiles)
		{
			var document = solution.Projects.Single().Documents.First(d => string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase));
			var root = (CompilationUnitSyntax)await document.GetSyntaxRootAsync();
			var model = await document.GetSemanticModelAsync();
			var nsDecl = root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().First();
			var oldNamespace = nsDecl.Name.ToString();
			Edit(document.Id, nsDecl.Name.Span, newNamespace);
			var parts = oldNamespace.Split('.');
			for (int i = 1; i <= parts.Length; i++)
			{
				Using(document.Id, string.Join(".", parts.Take(i)));
			}
			foreach (var nameNode in nsDecl.Members.SelectMany(member => member.DescendantNodes(descendIntoTrivia: false)).OfType<IdentifierNameSyntax>())
			{
				// leftmost identifier of a dotted name that is a namespace written relative to the old namespace
				bool leftmost = nameNode.Parent is QualifiedNameSyntax ql && ql.Left == nameNode || nameNode.Parent is MemberAccessExpressionSyntax ml && ml.Expression == nameNode;
				if (!leftmost)
				{
					continue;
				}
				if (model.GetSymbolInfo(nameNode).Symbol is INamespaceSymbol ns && ns.ToDisplayString() != nameNode.Identifier.Text && !ns.IsGlobalNamespace)
				{
					Edit(document.Id, nameNode.Span, ns.ToDisplayString());
				}
			}
		}

		foreach (var (id, set) in usings)
		{
			var document = solution.GetDocument(id);
			var root = (CompilationUnitSyntax)await document.GetSyntaxRootAsync();
			var existing = root.Usings.Where(u => u.Alias == null && u.StaticKeyword == default).Select(u => u.Name.ToString()).ToHashSet();
			var missing = set.Where(n => !existing.Contains(n)).OrderBy(n => n).ToList();
			if (missing.Count == 0)
			{
				continue;
			}
			var newLine = SourceProject.NewLineOf(root.ToFullString());
			int position = root.Usings.Count > 0 ? root.Usings.Last().FullSpan.End : 0;
			Edit(id, new TextSpan(position, 0), string.Concat(missing.Select(n => $"using {n};{newLine}")));
		}
		foreach (var (id, list) in edits)
		{
			var ordered = list.OrderBy(e => e.Span.Start).ToList();
			for (int i = 1; i < ordered.Count; i++)
			{
				if (ordered[i].Span.Start < ordered[i - 1].Span.End)
				{
					throw new InvalidOperationException($"overlapping edits in {solution.GetDocument(id).FilePath}");
				}
			}
			var text = await solution.GetDocument(id).GetTextAsync();
			solution = solution.WithDocumentText(id, text.WithChanges(ordered));
		}
		await project.Save(solution);

		// 4) Files follow their namespace folder and their (single) type name.
		foreach (var (path, newNamespace) in movedFiles)
		{
			var folder = Path.Combine(project.Root, newNamespace);
			var typesInFile = types.Where(t => t.Type.DeclaringSyntaxReferences.Any(r => string.Equals(r.SyntaxTree.FilePath, path, StringComparison.OrdinalIgnoreCase))).ToList();
			var fileName = Path.GetFileName(path);
			if (typesInFile.Count == 1 && typesInFile[0].Type.DeclaringSyntaxReferences.Length == 1)
			{
				fileName = typesInFile[0].Type.Name + ".cs";
			}
			else if (typesInFile.Count == 1)
			{
				// partial class over several files: OldName.Part.cs -> NewName.Part.cs
				var rest = fileName.Contains('.') && fileName.Count(c => c == '.') > 1 ? fileName[fileName.IndexOf('.')..] : ".cs";
				fileName = typesInFile[0].Type.Name + rest;
			}
			var destination = Path.Combine(folder, fileName);
			if (!string.Equals(destination, path, StringComparison.OrdinalIgnoreCase))
			{
				Directory.CreateDirectory(folder);
				if (File.Exists(destination))
				{
					throw new InvalidOperationException("exists: " + destination);
				}
				File.Move(path, destination);
				Console.WriteLine($"  {Path.GetRelativePath(project.Root, path)} -> {Path.GetRelativePath(project.Root, destination)}");
			}
		}
	}
}

/// <summary>
/// Many renames in one pass: <c>Ns.Type.member => NewName</c> (all overloads) or <c>Ns.Type => NewName</c>. Every
/// declaration and reference token gets the new name. Refuses names already used by another member of the same type.
/// </summary>
public static class RenameBatch
{
	public static async Task ListMembers(SourceProject project, string namespacePrefix)
	{
		var compilation = await project.Workspace.CurrentSolution.Projects.Single().GetCompilationAsync();
		foreach (var type in AllTypes(compilation.GlobalNamespace).Where(t => t.ContainingNamespace.ToDisplayString().StartsWith(namespacePrefix) && t.Locations.Any(l => l.IsInSource)).OrderBy(t => t.ToDisplayString()))
		{
			foreach (var member in type.GetMembers().Where(m => !m.IsImplicitlyDeclared && m.Locations.Any(l => l.IsInSource)))
			{
				if (member is IMethodSymbol { MethodKind: not MethodKind.Ordinary } || member is INamedTypeSymbol)
				{
					continue;
				}
				string kind = member switch
				{
					IFieldSymbol f => f.IsConst ? "const" : "field:" + f.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
					IPropertySymbol p => "property:" + p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
					IMethodSymbol => "method",
					IEventSymbol => "event",
					_ => member.Kind.ToString(),
				};
				bool inherited = member.IsOverride || member.IsVirtual || member.IsAbstract || member is IMethodSymbol { ExplicitInterfaceImplementations.Length: > 0 }
					|| type.AllInterfaces.SelectMany(i => i.GetMembers()).Any(i => SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(i), member));
				Console.WriteLine($"{type.ToDisplayString()}\t{member.Name}\t{kind}\t{member.DeclaredAccessibility}\t{(member.IsStatic ? "static" : "instance")}\t{(inherited ? "inherited" : "own")}");
			}
		}
	}

	private static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceSymbol ns)
	{
		foreach (var type in ns.GetTypeMembers())
		{
			yield return type;
			foreach (var nested in type.GetTypeMembers())
			{
				yield return nested;
			}
		}
		foreach (var child in ns.GetNamespaceMembers())
		{
			foreach (var type in AllTypes(child))
			{
				yield return type;
			}
		}
	}

	public static async Task Run(SourceProject project, string[] plan)
	{
		var solution = project.Workspace.CurrentSolution;
		var compilation = await solution.Projects.Single().GetCompilationAsync();
		var renames = new List<(ISymbol Symbol, string NewName)>();
		foreach (var raw in plan)
		{
			var line = raw.Split('#')[0].Trim();
			if (line.Length == 0)
			{
				continue;
			}
			var parts = line.Split("=>", StringSplitOptions.TrimEntries);
			var type = compilation.GetTypeByMetadataName(parts[0]);
			if (type != null)
			{
				renames.Add((type, parts[1]));
				continue;
			}
			var (owner, memberName) = Symbols.SplitMember(compilation, parts[0]);
			var members = owner.GetMembers(memberName).Where(m => !m.IsImplicitlyDeclared).ToList();
			if (members.Count == 0)
			{
				throw new InvalidOperationException("not found: " + parts[0]);
			}
			if (memberName != parts[1] && owner.GetMembers(parts[1]).Any(m => !members.Contains(m, SymbolEqualityComparer.Default))
				&& !renames.Any(r => r.Symbol.ContainingType != null && SymbolEqualityComparer.Default.Equals(r.Symbol.ContainingType, owner) && r.Symbol.Name == parts[1]))
			{
				throw new InvalidOperationException($"{owner.Name} already has a member named {parts[1]}");
			}
			renames.AddRange(members.Select(m => (m, parts[1])));
		}
		var edits = new Dictionary<DocumentId, Dictionary<TextSpan, string>>();
		void Edit(Document document, TextSpan span, string text)
		{
			if (!edits.TryGetValue(document.Id, out var map))
			{
				edits[document.Id] = map = new Dictionary<TextSpan, string>();
			}
			if (map.TryGetValue(span, out var existing) && existing != text)
			{
				throw new InvalidOperationException($"conflicting renames at {document.FilePath} {span}");
			}
			map[span] = text;
		}
		int count = 0;
		foreach (var (symbol, newName) in renames)
		{
			var declarations = new List<Location>(symbol.Locations);
			if (symbol is INamedTypeSymbol namedType)
			{
				declarations.AddRange(namedType.Constructors.Concat(namedType.StaticConstructors).Where(c => !c.IsImplicitlyDeclared).SelectMany(c => c.Locations));
			}
			foreach (var location in declarations.Where(l => l.IsInSource))
			{
				var document = solution.GetDocument(location.SourceTree);
				Edit(document, location.SourceSpan, newName);
			}
			var found = await SymbolFinder.FindReferencesAsync(symbol, solution);
			foreach (var location in found.SelectMany(r => r.Locations))
			{
				if (location.IsImplicit || location.IsCandidateLocation)
				{
					continue;
				}
				var text = (await location.Document.GetTextAsync()).ToString(location.Location.SourceSpan);
				if (text != symbol.Name)
				{
					continue; // alias or attribute shorthand: leave it
				}
				Edit(location.Document, location.Location.SourceSpan, newName);
				count++;
			}
		}
		foreach (var (id, map) in edits)
		{
			var text = await solution.GetDocument(id).GetTextAsync();
			solution = solution.WithDocumentText(id, text.WithChanges(map.Select(kv => new TextChange(kv.Key, kv.Value))));
		}
		Console.WriteLine($"{renames.Count} symbol(s), {count} reference(s) renamed");
		await project.Save(solution);
	}
}

/// <summary>Renames types and members everywhere (Roslyn Renamer).</summary>
public static class RenameSymbols
{
	public static async Task Run(SourceProject project, string[] plan)
	{
		var solution = project.Workspace.CurrentSolution;
		foreach (var raw in plan)
		{
			var line = raw.Split('#')[0].Trim();
			if (line.Length == 0)
			{
				continue;
			}
			var parts = line.Split("=>", StringSplitOptions.TrimEntries);
			var compilation = await solution.Projects.Single().GetCompilationAsync();
			ISymbol symbol = compilation.GetTypeByMetadataName(parts[0]);
			if (symbol == null)
			{
				var (type, member) = Symbols.SplitMember(compilation, parts[0]);
				var members = type.GetMembers(member).Where(m => !m.IsImplicitlyDeclared).ToList();
				if (members.Count == 0)
				{
					throw new InvalidOperationException("not found: " + parts[0]);
				}
				symbol = members[0];
			}
			solution = await Renamer.RenameSymbolAsync(solution, symbol, new SymbolRenameOptions(RenameOverloads: true), parts[1]);
			Console.WriteLine($"renamed {parts[0]} -> {parts[1]}");
		}
		await project.Save(solution);
	}
}
