using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace IsaRefactor;

/// <summary>
/// Puts IsaClient's direct calls to the game's text drawing through the player's language:
///
///   refactor wrap-text-sinks [--dry]
///
/// In src/IsaClient.*, the text argument of Main.MouseText/NewText, Utils.DrawBorderString*, ChatManager's string
/// drawing and measuring, DynamicSpriteFont.MeasureString/DrawString, TextSnippet and UIText becomes
/// IsaClient.Languages.L10n.T(text). The UI helpers that already translate where text comes in (HubKit, MusicDrawing,
/// UiKit, ClientChat) and IsaClient.Languages are left alone. Running it again changes nothing.
/// </summary>
public static class WrapTextSinks
{
	private static readonly HashSet<string> SkippedFiles = new(StringComparer.OrdinalIgnoreCase)
	{
		"IsaClient.Hub/HubKit.cs", "IsaClient.Music/MusicDrawing.cs", "IsaClient.Menu/UiKit.cs", "IsaClient.Core/ClientChat.cs", "IsaClient.Plugins/ImGuiExt.cs"
	};

	private static readonly HashSet<string> TextParameters = new(StringComparer.Ordinal) { "text", "cursorText", "newText", "txt" };

	private static readonly HashSet<string> SinkTypes = new(StringComparer.Ordinal)
	{
		"Main", "Utils", "ChatManager", "DynamicSpriteFont", "DynamicSpriteFontExtensionMethods", "TextSnippet", "UIText", "UITextPanel"
	};

	public static async Task Run(SourceProject project, string[] args)
	{
		bool dry = args.Contains("--dry");
		var solution = project.Workspace.CurrentSolution;
		var compilation = await solution.Projects.Single().GetCompilationAsync();
		int total = 0;
		foreach (var document in solution.Projects.Single().Documents.ToList())
		{
			var relative = Path.GetRelativePath(project.Root, document.FilePath).Replace('\\', '/');
			if (!relative.StartsWith("IsaClient.", StringComparison.Ordinal) || relative.StartsWith("IsaClient.Languages/", StringComparison.Ordinal) || SkippedFiles.Contains(relative))
			{
				continue;
			}
			var tree = await document.GetSyntaxTreeAsync();
			var model = compilation.GetSemanticModel(tree);
			var changes = new List<TextChange>();
			foreach (var call in (await tree.GetRootAsync()).DescendantNodes().Where(n => n is InvocationExpressionSyntax || n is ObjectCreationExpressionSyntax || n is ImplicitObjectCreationExpressionSyntax))
			{
				ArgumentListSyntax list = call switch
				{
					InvocationExpressionSyntax invocation => invocation.ArgumentList,
					ObjectCreationExpressionSyntax creation => creation.ArgumentList,
					ImplicitObjectCreationExpressionSyntax implicitCreation => implicitCreation.ArgumentList,
					_ => null
				};
				if (list == null || model.GetSymbolInfo(call).Symbol is not IMethodSymbol method || !SinkTypes.Contains(method.ContainingType?.Name ?? ""))
				{
					continue;
				}
				for (int i = 0; i < list.Arguments.Count; i++)
				{
					var argument = list.Arguments[i];
					IParameterSymbol parameter = (argument.NameColon != null) ? method.Parameters.FirstOrDefault(p => p.Name == argument.NameColon.Name.Identifier.Text) : (i < method.Parameters.Length ? method.Parameters[i] : null);
					if (parameter == null || parameter.Type.SpecialType != SpecialType.System_String || !TextParameters.Contains(parameter.Name))
					{
						continue;
					}
					var expression = argument.Expression;
					if (expression.ToString().Contains("L10n.T(") || (expression is LiteralExpressionSyntax literal && !literal.Token.ValueText.Any(char.IsLetter)))
					{
						continue;
					}
					changes.Add(new TextChange(expression.Span, "IsaClient.Languages.L10n.T(" + expression + ")"));
					Console.WriteLine(relative + ":" + (expression.GetLocation().GetLineSpan().StartLinePosition.Line + 1) + "  " + method.ContainingType.Name + "." + method.Name + "  " + Short(expression.ToString()));
				}
			}
			if (changes.Count == 0)
			{
				continue;
			}
			total += changes.Count;
			if (!dry)
			{
				var text = await document.GetTextAsync();
				// Nested sinks (a MouseText inside a DrawBorderString argument) are not changed twice: outer ones only.
				var outer = changes.Where(c => !changes.Any(o => o.Span != c.Span && o.Span.Contains(c.Span))).OrderByDescending(c => c.Span.Start).ToList();
				var updated = text.WithChanges(outer);
				var raw = File.ReadAllBytes(document.FilePath);
				bool bom = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;
				File.WriteAllText(document.FilePath, updated.ToString(), new System.Text.UTF8Encoding(bom));
			}
		}
		Console.WriteLine(total + " text arguments " + (dry ? "would be" : "") + " wrapped");
	}

	private static string Short(string text)
	{
		text = text.Replace("\r", "").Replace("\n", " ");
		return text.Length > 90 ? text.Substring(0, 87) + "..." : text;
	}
}
