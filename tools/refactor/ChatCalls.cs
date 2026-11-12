using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace IsaRefactor;

/// <summary>
/// <c>Main.NewText(text, ClientChat.AccentColor)</c> becomes <c>ClientChat.Info(text)</c> (ErrorColor: Error, InfoColor:
/// Notice) when the text is a string. Files in the given folder prefixes only.
///
///   refactor chat-calls IsaClient. Terraria/WldDownloadCommands.cs
/// </summary>
public static class ChatCalls
{
	private static readonly Dictionary<string, string> Methods = new()
	{
		["AccentColor"] = "Info",
		["ErrorColor"] = "Error",
		["InfoColor"] = "Notice",
	};

	public static async Task Run(SourceProject project, string[] prefixes)
	{
		var solution = project.Workspace.CurrentSolution;
		var compilation = await solution.Projects.Single().GetCompilationAsync();
		var chatType = compilation.GetTypeByMetadataName("IsaClient.Core.ClientChat");
		int count = 0;
		var needsUsing = new HashSet<DocumentId>();
		foreach (var document in solution.Projects.Single().Documents.ToList())
		{
			var relative = Path.GetRelativePath(project.Root, document.FilePath).Replace('\\', '/');
			if (relative.EndsWith("IsaClient.Core/ClientChat.cs"))
			{
				continue; // Info/Notice/Error themselves are built on Main.NewText
			}
			if (!prefixes.Any(p => relative.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
			{
				continue;
			}
			var tree = await document.GetSyntaxTreeAsync();
			var model = compilation.GetSemanticModel(tree);
			var changes = new List<TextChange>();
			foreach (var invocation in (await tree.GetRootAsync()).DescendantNodes().OfType<InvocationExpressionSyntax>())
			{
				if (invocation.ArgumentList.Arguments.Count != 2 || invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.Text: "NewText" })
				{
					continue;
				}
				if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol { ContainingType.Name: "Main", ContainingType.ContainingNamespace.Name: "Terraria" })
				{
					continue;
				}
				var colorArgument = invocation.ArgumentList.Arguments[1].Expression;
				if (model.GetSymbolInfo(colorArgument).Symbol is not IFieldSymbol color || !SymbolEqualityComparer.Default.Equals(color.ContainingType, chatType) || !Methods.TryGetValue(color.Name, out var method))
				{
					continue;
				}
				var textArgument = invocation.ArgumentList.Arguments[0].Expression;
				if (model.GetTypeInfo(textArgument).Type?.SpecialType != SpecialType.System_String)
				{
					continue;
				}
				string qualifier = model.LookupNamespacesAndTypes(invocation.SpanStart, name: "ClientChat").Any(s => SymbolEqualityComparer.Default.Equals(s, chatType)) ? "ClientChat" : "IsaClient.Core.ClientChat";
				changes.Add(new TextChange(invocation.Span, $"{qualifier}.{method}({textArgument.ToFullString().Trim()})"));
				count++;
			}
			if (changes.Count > 0)
			{
				var text = await document.GetTextAsync();
				solution = solution.WithDocumentText(document.Id, text.WithChanges(RemoveNested(changes)));
			}
		}
		Console.WriteLine($"{count} chat call(s) rewritten");
		await project.Save(solution);
	}

	/// <summary>A NewText inside another NewText's text argument: keep the outer change only (its text already has the inner one).</summary>
	private static IEnumerable<TextChange> RemoveNested(List<TextChange> changes)
	{
		return changes.Where(c => !changes.Any(o => o.Span != c.Span && o.Span.Contains(c.Span))).OrderBy(c => c.Span.Start);
	}
}
