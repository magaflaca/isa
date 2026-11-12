using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IsaRefactor;

/// <summary>
/// Collects every text IsaClient may show, for the language packs (IsaClient.Languages.L10n):
///
///   refactor extract-strings &lt;en.json&gt; &lt;context.json&gt;
///
/// Each string expression in src/IsaClient.* becomes a key: a literal as it is, text built from parts as a template
/// ("Playing: " + title -> "Playing: {0}"; literal braces doubled). Texts that are not shown are left out by where they
/// are: attribute arguments, case labels, comparisons, indexers, and arguments of parameters named like keys, ids,
/// paths or commands; logs, ImGui and file/regex/string-search calls. Anything else must read like words. en.json is
/// the English pack (every key mapped to itself); context.json says where each key is used, for translators.
/// </summary>
public static class ExtractStrings
{
	private static readonly Regex ExcludedParameter = new(@"^(id|ids|key|keys|path|paths|file|files|filename|filepath|folder|directory|url|uri|link|layer|command|commands|commandname|alias|aliases|names|pattern|regex|setting|settingkey|resource|extension|extensions|legacy|keywords|usage|syntax|separator|oldvalue|newvalue|oldchar|newchar|format|culture|header|field|property|member|method|typename|assemblyname|arguments|args|argument|section|code|tag|channel|query|search|value2|json|contenttype|mime|encoding|scheme|host|cookie|cookies|token|apikey|model|role|layername|texture|sound|asset|style|kind|category|group|prefix|suffix|substring|match|input|replacement|trimchars|separators|startswith|endswith|prefixes)$", RegexOptions.IgnoreCase);

	private static readonly HashSet<string> ExcludedMethods = new(StringComparer.Ordinal)
	{
		"Combine", "Exists", "ReadAllText", "ReadAllBytes", "ReadAllLines", "WriteAllText", "WriteAllBytes", "WriteAllLines", "AppendAllText", "Delete", "Move", "Copy",
		"GetFiles", "GetDirectories", "EnumerateFiles", "CreateDirectory", "GetFileName", "ChangeExtension", "Request", "Tex", "GetManifestResourceStream",
		"Equals", "StartsWith", "EndsWith", "Contains", "IndexOf", "LastIndexOf", "IndexOfAny", "Replace", "Split", "Trim", "TrimStart", "TrimEnd", "PadLeft", "PadRight",
		"IsMatch", "Match", "Matches", "Escape", "Parse", "TryParse", "ParseExact", "TryParseExact", "ToString", "GetType", "GetMethod", "GetField", "GetProperty",
		"Field", "Method", "PropertyGetter", "PropertySetter", "Get", "Put", "TryGetValue", "ContainsKey", "Remove", "GetValue", "SetValue", "GetString", "SetString",
		"Log", "LogException", "WriteLine", "Assert",
		"Register", "Start", "GetEnvironmentVariable", "SetEnvironmentVariable", "Load", "LoadFrom", "GetTypeByMetadataName", "Invoke", "Ensure",
		"FromBase64String", "GetBytes", "Hash", "Encode", "Decode", "Join", "Concat", "Compare", "CompareOrdinal", "SelectToken", "Value", "Property",
		"AddRange", "Insert", "AddFile", "Open", "OpenRead", "OpenWrite", "Create", "Navigate", "Execute", "Send", "Post", "Upload", "Download", "RunYtDlp"
	};

	/// <summary>Classes whose calls never show text (their strings are ids, logs, ImGui labels...).</summary>
	private static readonly HashSet<string> ExcludedTypes = new(StringComparer.Ordinal)
	{
		"ClientLog", "PluginLog", "ImGui", "ImGuiExt", "Regex", "File", "Directory", "Path", "Process", "ProcessStartInfo", "Encoding", "Convert", "JObject", "JToken", "JArray",
		"Harmony", "AccessTools", "Assembly", "Type", "Environment", "Registry", "RegistryKey", "WebRequest", "HttpWebRequest", "WebClient", "HttpClient", "Uri",
		"Trace", "Debug", "Console", "StringComparer", "CultureInfo", "Guid", "BitConverter", "Marshal"
	};

	private static readonly Regex Identifier = new(@"^[A-Za-z][A-Za-z0-9]*([_.\-/:][A-Za-z0-9]+)+$|^[a-z]+([A-Z][a-z0-9]*)+$|^([A-Z][a-z0-9]+){2,}$|^[A-Z0-9_]{2,}$");

	public static async Task Run(SourceProject project, string[] args)
	{
		var solution = project.Workspace.CurrentSolution;
		var compilation = await solution.Projects.Single().GetCompilationAsync();
		var keys = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
		foreach (var document in solution.Projects.Single().Documents)
		{
			var relative = Path.GetRelativePath(project.Root, document.FilePath).Replace('\\', '/');
			// The test harness only writes to its own log file: nothing of it is shown.
			if (!relative.StartsWith("IsaClient.", StringComparison.Ordinal) || relative.EndsWith("IsaClient.Languages/L10n.cs", StringComparison.Ordinal) || relative.EndsWith("IsaClient.Core/TestHarness.cs", StringComparison.Ordinal))
			{
				continue;
			}
			var tree = await document.GetSyntaxTreeAsync();
			var model = compilation.GetSemanticModel(tree);
			foreach (var node in (await tree.GetRootAsync()).DescendantNodes().OfType<ExpressionSyntax>())
			{
				if (!IsStringExpression(node, model) || IsInsideConcatenation(node, model))
				{
					continue;
				}
				string template = Template(node, model, out int holes, out bool hasLiteral);
				if (!hasLiteral || template == null)
				{
					continue;
				}
				// (on ? "En" : "Dis") + "abled": the hole is the whole word ("Enabled"), translated on its own.
				template = Regex.Replace(template, @"(\{\d+\})abled", "$1");
				string context = Context(node, model, out bool excluded, out bool display);
				if (excluded || !Wanted(template, holes, display))
				{
					continue;
				}
				// L10n.F("Same as the game ({0})", name): a format, looked up as written.
				if (context.StartsWith("L10n.F(", StringComparison.Ordinal))
				{
					template = template.Replace("{{", "{").Replace("}}", "}");
				}
				int line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
				if (!keys.TryGetValue(template, out var refs))
				{
					refs = new List<string>();
					keys[template] = refs;
				}
				if (refs.Count < 4)
				{
					refs.Add(relative + ":" + line + (context.Length > 0 ? " " + context : ""));
				}
			}
		}
		var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
		var pack = new Dictionary<string, object>
		{
			["Code"] = "en",
			["Name"] = "English",
			["EnglishName"] = "English",
			["Flag"] = "us",
			["Author"] = "IsaClient",
			["Strings"] = keys.Keys.Concat(new[] { "Enabled", "Disabled", "enabled", "disabled" }).Distinct().OrderBy(k => k, StringComparer.Ordinal).ToDictionary(k => k, k => k)
		};
		File.WriteAllText(args[0], JsonSerializer.Serialize(pack, options), new UTF8Encoding(false));
		File.WriteAllText(args[1], JsonSerializer.Serialize(keys, options), new UTF8Encoding(false));
		Console.WriteLine(keys.Count + " texts (" + keys.Keys.Count(k => k.Contains("{0}")) + " templates) -> " + args[0]);
	}

	private static bool IsStringExpression(ExpressionSyntax node, SemanticModel model)
	{
		switch (node)
		{
		case LiteralExpressionSyntax literal:
			return literal.IsKind(SyntaxKind.StringLiteralExpression);
		case InterpolatedStringExpressionSyntax:
			return true;
		case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression):
			return model.GetTypeInfo(binary).Type?.SpecialType == SpecialType.System_String && ContainsLiteral(binary);
		default:
			return false;
		}
	}

	private static bool ContainsLiteral(SyntaxNode node)
	{
		return node.DescendantNodesAndSelf().Any(n => n.IsKind(SyntaxKind.StringLiteralExpression) || n is InterpolatedStringExpressionSyntax);
	}

	/// <summary>Part of a bigger "a" + b + "c" (the whole one is the key).</summary>
	private static bool IsInsideConcatenation(ExpressionSyntax node, SemanticModel model)
	{
		SyntaxNode parent = node.Parent;
		while (parent is ParenthesizedExpressionSyntax)
		{
			parent = parent.Parent;
		}
		return parent is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.AddExpression) && model.GetTypeInfo(binary).Type?.SpecialType == SpecialType.System_String;
	}

	private static string Template(ExpressionSyntax node, SemanticModel model, out int holes, out bool hasLiteral)
	{
		var parts = new List<(bool hole, string text)>();
		Flatten(node, model, parts);
		var result = new StringBuilder();
		holes = 0;
		hasLiteral = false;
		bool lastWasHole = false;
		foreach (var (hole, text) in parts)
		{
			if (hole)
			{
				// Two parts in a row read as one.
				if (!lastWasHole)
				{
					result.Append('{').Append(holes++).Append('}');
				}
				lastWasHole = true;
				continue;
			}
			if (text.Length == 0)
			{
				continue;
			}
			hasLiteral = true;
			lastWasHole = false;
			result.Append(text.Replace("{", "{{").Replace("}", "}}"));
		}
		return result.ToString();
	}

	private static void Flatten(ExpressionSyntax node, SemanticModel model, List<(bool, string)> parts)
	{
		while (node is ParenthesizedExpressionSyntax parenthesized && IsStringAdd(parenthesized.Expression, model))
		{
			node = parenthesized.Expression;
		}
		switch (node)
		{
		case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression):
			parts.Add((false, literal.Token.ValueText));
			return;
		case LiteralExpressionSyntax character when character.IsKind(SyntaxKind.CharacterLiteralExpression):
			parts.Add((false, character.Token.ValueText));
			return;
		case InterpolatedStringExpressionSyntax interpolated:
			foreach (var content in interpolated.Contents)
			{
				if (content is InterpolatedStringTextSyntax text)
				{
					parts.Add((false, text.TextToken.ValueText.Replace("{{", "{").Replace("}}", "}")));
				}
				else
				{
					parts.Add((true, null));
				}
			}
			return;
		case BinaryExpressionSyntax binary when IsStringAdd(binary, model):
			Flatten(binary.Left, model, parts);
			Flatten(binary.Right, model, parts);
			return;
		default:
			parts.Add((true, null));
			return;
		}
	}

	private static bool IsStringAdd(ExpressionSyntax node, SemanticModel model)
	{
		return node is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.AddExpression) && model.GetTypeInfo(binary).Type?.SpecialType == SpecialType.System_String;
	}

	/// <summary>Where the text goes: excluded (an id, a key, a log...) or surely shown (a parameter called text, title...).</summary>
	private static string Context(ExpressionSyntax node, SemanticModel model, out bool excluded, out bool display)
	{
		excluded = false;
		display = false;
		SyntaxNode child = node;
		SyntaxNode parent = node.Parent;
		// Up through ?:, ??, parentheses, casts and ternary branches: the text goes where the whole goes.
		while (parent is ParenthesizedExpressionSyntax || parent is ConditionalExpressionSyntax || parent is CastExpressionSyntax || (parent is BinaryExpressionSyntax coalesce && coalesce.IsKind(SyntaxKind.CoalesceExpression)) || IsStringAdd(parent as ExpressionSyntax, model))
		{
			if (parent is ConditionalExpressionSyntax conditional && conditional.Condition == child)
			{
				break;
			}
			child = parent;
			parent = parent.Parent;
		}
		if (node.Ancestors().Any(a => a is AttributeArgumentSyntax))
		{
			excluded = true;
			return "attribute";
		}
		// { "Alex", "Sam" } assigned to a pool of names: names, never translated.
		if (parent is InitializerExpressionSyntax && parent.Parent is ExpressionSyntax created && created.Parent is EqualsValueClauseSyntax value && value.Parent is VariableDeclaratorSyntax pool && pool.Identifier.Text.EndsWith("Pool", StringComparison.OrdinalIgnoreCase))
		{
			excluded = true;
			return "= " + pool.Identifier.Text;
		}
		switch (parent)
		{
		case CaseSwitchLabelSyntax:
		case ConstantPatternSyntax:
		case CasePatternSwitchLabelSyntax:
			excluded = true;
			return "case";
		case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.EqualsExpression) || binary.IsKind(SyntaxKind.NotEqualsExpression):
			excluded = true;
			return "compare";
		case BracketedArgumentListSyntax:
			excluded = true;
			return "index";
		case ArgumentSyntax argument when argument.Parent is BracketedArgumentListSyntax:
			excluded = true;
			return "index";
		case SwitchExpressionArmSyntax arm when arm.Pattern.DescendantNodesAndSelf().Contains(child):
			excluded = true;
			return "case";
		}
		if (parent is ArgumentSyntax arg && arg.Parent is ArgumentListSyntax list)
		{
			var call = list.Parent;
			var symbol = (call != null) ? (model.GetSymbolInfo(call).Symbol ?? model.GetSymbolInfo(call).CandidateSymbols.FirstOrDefault()) as IMethodSymbol : null;
			if (symbol == null)
			{
				return "call";
			}
			string type = symbol.ContainingType?.Name ?? "";
			// Method names only count for the framework's own (string.Contains, File.Exists...): IsaClient's methods are judged by their parameters.
			string space = symbol.ContainingNamespace?.ToDisplayString() ?? "";
			bool framework = space.StartsWith("System", StringComparison.Ordinal) || space.StartsWith("Newtonsoft", StringComparison.Ordinal) || space.StartsWith("HarmonyLib", StringComparison.Ordinal) || space.StartsWith("Microsoft.Win32", StringComparison.Ordinal);
			if (ExcludedTypes.Contains(type) || (ExcludedMethods.Contains(symbol.Name) && symbol.MethodKind != MethodKind.Constructor && (framework || symbol.IsExtensionMethod)))
			{
				excluded = true;
				return type + "." + symbol.Name;
			}
			IParameterSymbol parameter = Parameter(symbol, list, arg);
			if (parameter != null)
			{
				if (ExcludedParameter.IsMatch(parameter.Name) || (parameter.Name == "name" && !IsShownName(symbol)))
				{
					excluded = true;
				}
				else if (Regex.IsMatch(parameter.Name, "^(text|label|title|subtitle|description|summary|tooltip|tip|hint|message|placeholder|status|caption|note|details|verb|prompt|question|confirm|line|body|heading|what|why|reason|error|info|notice|toast)$", RegexOptions.IgnoreCase))
				{
					display = true;
				}
				return type + "." + symbol.Name + "(" + parameter.Name + ")";
			}
			return type + "." + symbol.Name;
		}
		if (parent is EqualsValueClauseSyntax equals && equals.Parent is VariableDeclaratorSyntax declarator)
		{
			return Named(declarator.Identifier.Text, ref excluded, ref display);
		}
		if (parent is AssignmentExpressionSyntax assignment && assignment.Right == child)
		{
			// new Thread(...) { Name = "..." }: a name for debuggers, never shown.
			if (assignment.Parent is InitializerExpressionSyntax init && init.Parent is BaseObjectCreationExpressionSyntax newThread && model.GetTypeInfo(newThread).Type?.Name == "Thread")
			{
				excluded = true;
				return "= thread name";
			}
			string target = assignment.Left is MemberAccessExpressionSyntax member ? member.Name.Identifier.Text : (assignment.Left as IdentifierNameSyntax)?.Identifier.Text ?? "";
			return Named(target, ref excluded, ref display);
		}
		return parent?.Kind().ToString() ?? "";
	}

	private static string Named(string name, ref bool excluded, ref bool display)
	{
		// The AI's instructions go to the AI, not to the screen.
		if (Regex.IsMatch(name, "(Prompt|PromptEnglish|PromptSpanish|system|System|Instructions)$"))
		{
			excluded = true;
			return "= " + name;
		}
		// What a program tells a server about itself.
		if (Regex.IsMatch(name, "(UserAgent|Agent)$"))
		{
			excluded = true;
			return "= " + name;
		}
		// Pools of names to pick from (bots' random names): names, never translated.
		if (Regex.IsMatch(name, "(namePool|NamePool|Pool)$"))
		{
			excluded = true;
			return "= " + name;
		}
		if (Regex.IsMatch(name, "(Key|Id|Path|File|Folder|Url|Uri|Command|Pattern|Format|Layer|Extension|Name)$") && !Regex.IsMatch(name, "(Display|Shown|Friendly|English)Name$"))
		{
			excluded = !Regex.IsMatch(name, "^(Name|Title|Label|Text)$");
		}
		if (Regex.IsMatch(name, "(Title|Text|Label|Tooltip|Tip|Description|Summary|Message|Status|Hint|Placeholder|Caption|Verb|Note|What|Details)$", RegexOptions.IgnoreCase))
		{
			display = true;
		}
		return "= " + name;
	}

	/// <summary>A "name" that is shown (a button's), not an id.</summary>
	private static bool IsShownName(IMethodSymbol method)
	{
		return method.ContainingType?.Name is "MenuControls" or "HubKit" or "UiKit" or "MusicDrawing";
	}

	private static IParameterSymbol Parameter(IMethodSymbol method, ArgumentListSyntax list, ArgumentSyntax argument)
	{
		if (argument.NameColon != null)
		{
			return method.Parameters.FirstOrDefault(p => p.Name == argument.NameColon.Name.Identifier.Text);
		}
		int index = list.Arguments.IndexOf(argument);
		if (index < method.Parameters.Length)
		{
			return method.Parameters[index];
		}
		return method.Parameters.LastOrDefault(p => p.IsParams);
	}

	/// <summary>Reads like something a person reads (not an id, a path, a switch, a format).</summary>
	private static bool Wanted(string template, int holes, bool display)
	{
		// "tetris::Hold": the word as used in one place (IsaClient.Languages.L10n); judged by the word.
		template = Regex.Replace(template, @"^[a-z]{2,20}::(?=\S)", "");
		string literal = Regex.Replace(template, @"\{\d+\}", " ").Replace("{{", "{").Replace("}}", "}");
		string trimmed = literal.Trim();
		int letters = trimmed.Count(char.IsLetter);
		if (letters < 2 || !Regex.IsMatch(trimmed, @"\p{L}{2}"))
		{
			return false;
		}
		if (trimmed.Contains("<?") || trimmed.Contains(">?") || trimmed.StartsWith("-") || trimmed.StartsWith("http") || trimmed.StartsWith("##") || trimmed.Contains("://") && !trimmed.Contains(' ') || Regex.IsMatch(trimmed, @"^[\w.]+\.(cs|dll|exe|json|png|txt|xnb|ini|wld|plr|mp4|gif|jpg)$", RegexOptions.IgnoreCase))
		{
			return false;
		}
		// Ids of the panel's controls ("bs:card:{0}", "br:mode:{0}"): never shown.
		if (System.Text.RegularExpressions.Regex.IsMatch(template, @"^[a-z]{2,8}:[A-Za-z0-9_{}:\-]+$"))
		{
			return false;
		}
		// Terraria asset paths, JSON keys, file patterns.
		if (Regex.IsMatch(trimmed, @"^(Images|Sounds|Fonts|Music)[/\\]") || trimmed.StartsWith("*.") || trimmed.StartsWith("{\""))
		{
			return false;
		}
		bool words = trimmed.Contains(' ');
		if (display)
		{
			return words || !Identifier.IsMatch(trimmed);
		}
		if (words)
		{
			return true;
		}
		// One word: shown when it looks like a word of a sentence ("Search", "Saved:"), not an id ("ChatImages", "video_id").
		if (Identifier.IsMatch(trimmed))
		{
			return false;
		}
		return Regex.IsMatch(trimmed, @"^\p{Lu}\p{Ll}+[.:!?]?$") || Regex.IsMatch(trimmed, @"^[\p{L}]+[.:!?]$") || holes > 0;
	}
}
