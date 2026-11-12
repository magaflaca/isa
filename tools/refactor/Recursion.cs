using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IsaRefactor;

/// <summary>
/// Lists members that reference themselves: methods calling themselves and properties reading/writing themselves.
/// Real recursion exists, but after renames a member that now binds to itself is a stack overflow.
///
///   refactor find-recursion [folder prefix ...]
/// </summary>
public static class Recursion
{
	public static async Task Run(SourceProject project, string[] prefixes)
	{
		var solution = project.Workspace.CurrentSolution;
		var compilation = await solution.Projects.Single().GetCompilationAsync();
		foreach (var document in solution.Projects.Single().Documents)
		{
			var relative = Path.GetRelativePath(project.Root, document.FilePath).Replace('\\', '/');
			if (prefixes.Length > 0 && !prefixes.Any(p => relative.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
			{
				continue;
			}
			var tree = await document.GetSyntaxTreeAsync();
			var model = compilation.GetSemanticModel(tree);
			foreach (var member in (await tree.GetRootAsync()).DescendantNodes().Where(n => n is MethodDeclarationSyntax or PropertyDeclarationSyntax or AccessorDeclarationSyntax))
			{
				var symbol = model.GetDeclaredSymbol(member);
				if (symbol is IMethodSymbol { AssociatedSymbol: IPropertySymbol owner })
				{
					symbol = owner;
				}
				if (symbol == null)
				{
					continue;
				}
				SyntaxNode body = member switch
				{
					MethodDeclarationSyntax m => (SyntaxNode)m.Body ?? m.ExpressionBody,
					PropertyDeclarationSyntax p => p.ExpressionBody,
					AccessorDeclarationSyntax a => (SyntaxNode)a.Body ?? a.ExpressionBody,
					_ => null,
				};
				if (body == null)
				{
					continue;
				}
				foreach (var name in body.DescendantNodes().OfType<SimpleNameSyntax>())
				{
					var bound = model.GetSymbolInfo(name).Symbol;
					if (bound != null && SymbolEqualityComparer.Default.Equals(bound.OriginalDefinition, symbol.OriginalDefinition))
					{
						// a call through another instance (other.Foo()) is usually fine; report self/static uses
						bool viaOther = name.Parent is MemberAccessExpressionSyntax ma && ma.Name == name && ma.Expression is not ThisExpressionSyntax
							&& model.GetSymbolInfo(ma.Expression).Symbol is not INamedTypeSymbol;
						if (!viaOther)
						{
							var line = name.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
							Console.WriteLine($"{relative}:{line}  {symbol.ContainingType?.Name}.{symbol.Name}  ->  {name.Parent}");
							break;
						}
					}
				}
			}
		}
	}
}
