using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace IsaRefactor;

/// <summary>
/// Cleans files under the given folder prefixes:
///  - <c>Type.Member</c> written inside Type itself becomes <c>Member</c> when the short name binds to the same symbol;
///  - using directives the compiler reports as unnecessary (CS8019) are removed.
///
///   refactor tidy IsaClient.     (relative folder prefixes, several allowed)
/// </summary>
public static class Tidy
{
	public static async Task Run(SourceProject project, string[] prefixes)
	{
		var solution = project.Workspace.CurrentSolution;
		var compilation = await solution.Projects.Single().GetCompilationAsync();
		var unnecessary = compilation.GetDiagnostics().Where(d => d.Id == "CS8019").GroupBy(d => d.Location.SourceTree).ToDictionary(g => g.Key, g => g.Select(d => d.Location.SourceSpan).ToList());
		int simplified = 0, usingsRemoved = 0;
		foreach (var document in solution.Projects.Single().Documents.ToList())
		{
			var relative = Path.GetRelativePath(project.Root, document.FilePath).Replace('\\', '/');
			if (!prefixes.Any(p => relative.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
			{
				continue;
			}
			var tree = await document.GetSyntaxTreeAsync();
			var root = await tree.GetRootAsync();
			var model = compilation.GetSemanticModel(tree);
			var changes = new List<TextChange>();
			foreach (var access in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
			{
				if (access.Expression is not IdentifierNameSyntax qualifier || access.Name is not SimpleNameSyntax name)
				{
					continue;
				}
				var member = model.GetSymbolInfo(access).Symbol;
				var typeSymbol = model.GetSymbolInfo(qualifier).Symbol as INamedTypeSymbol;
				if (member == null || typeSymbol == null || !member.IsStatic || !SymbolEqualityComparer.Default.Equals(member.ContainingType, typeSymbol))
				{
					continue;
				}
				var enclosing = model.GetEnclosingSymbol(access.SpanStart)?.ContainingType;
				bool inside = false;
				for (var t = enclosing; t != null; t = t.ContainingType)
				{
					if (SymbolEqualityComparer.Default.Equals(t, typeSymbol))
					{
						inside = true;
						break;
					}
				}
				if (!inside || access.Parent is InvocationExpressionSyntax && member is not IMethodSymbol)
				{
					continue;
				}
				// The short name must bind to the same member at this position.
				var speculative = model.GetSpeculativeSymbolInfo(access.SpanStart, SyntaxFactory.IdentifierName(name.Identifier.Text), SpeculativeBindingOption.BindAsExpression);
				var bound = speculative.Symbol ?? speculative.CandidateSymbols.FirstOrDefault();
				bool same = SymbolEqualityComparer.Default.Equals(bound?.OriginalDefinition, member.OriginalDefinition)
					|| (member is IMethodSymbol && speculative.CandidateReason == CandidateReason.MemberGroup && speculative.CandidateSymbols.Any(c => c.Name == member.Name && SymbolEqualityComparer.Default.Equals(c.ContainingType, typeSymbol)));
				if (!same)
				{
					continue;
				}
				changes.Add(new TextChange(TextSpan.FromBounds(access.Expression.SpanStart, access.Name.SpanStart), ""));
				simplified++;
			}
			if (unnecessary.TryGetValue(tree, out var spans))
			{
				var text = await document.GetTextAsync();
				foreach (var span in spans)
				{
					var line = text.Lines.GetLineFromPosition(span.Start);
					changes.Add(new TextChange(line.SpanIncludingLineBreak, ""));
					usingsRemoved++;
				}
			}
			if (changes.Count > 0)
			{
				var text = await document.GetTextAsync();
				solution = solution.WithDocumentText(document.Id, text.WithChanges(changes.OrderBy(c => c.Span.Start).GroupBy(c => c.Span).Select(g => g.First())));
			}
		}
		Console.WriteLine($"{simplified} self-qualified access(es) simplified, {usingsRemoved} unnecessary using(s) removed");
		await project.Save(solution);
	}
}
