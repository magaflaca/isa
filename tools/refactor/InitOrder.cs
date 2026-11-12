using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IsaRefactor;

/// <summary>
/// Static field initializers run in declaration order. Lists initializers that read a static field of the same type
/// declared after them (or in another part of a partial type): that field still has its default value at that moment.
///
///   refactor check-init-order [folder prefix ...]
/// </summary>
public static class InitOrder
{
	public static async Task Run(SourceProject project, string[] prefixes)
	{
		var solution = project.Workspace.CurrentSolution;
		var compilation = await solution.Projects.Single().GetCompilationAsync();
		int found = 0;
		foreach (var document in solution.Projects.Single().Documents)
		{
			var relative = Path.GetRelativePath(project.Root, document.FilePath).Replace('\\', '/');
			if (prefixes.Length > 0 && !prefixes.Any(p => relative.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
			{
				continue;
			}
			var tree = await document.GetSyntaxTreeAsync();
			var model = compilation.GetSemanticModel(tree);
			foreach (var declarator in (await tree.GetRootAsync()).DescendantNodes().OfType<VariableDeclaratorSyntax>())
			{
				if (declarator.Initializer == null || model.GetDeclaredSymbol(declarator) is not IFieldSymbol { IsStatic: true, IsConst: false } field)
				{
					continue;
				}
				foreach (var name in declarator.Initializer.Value.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
				{
					if (InsideLambda(name, declarator))
					{
						continue;
					}
					if (model.GetSymbolInfo(name).Symbol is not IFieldSymbol { IsStatic: true, IsConst: false } read || !SymbolEqualityComparer.Default.Equals(read.ContainingType, field.ContainingType) || SymbolEqualityComparer.Default.Equals(read, field))
					{
						continue;
					}
					var readDeclaration = read.DeclaringSyntaxReferences.FirstOrDefault();
					if (readDeclaration == null)
					{
						continue;
					}
					bool later = readDeclaration.SyntaxTree != tree || readDeclaration.Span.Start > declarator.SpanStart;
					if (later)
					{
						found++;
						Console.WriteLine($"{relative}:{declarator.GetLocation().GetLineSpan().StartLinePosition.Line + 1}  {field.ContainingType.Name}.{field.Name} reads {read.Name} ({(readDeclaration.SyntaxTree == tree ? "declared later" : "another file")})");
					}
				}
			}
		}
		Console.WriteLine($"{found} problem(s)");
	}

	private static bool InsideLambda(SyntaxNode node, SyntaxNode root)
	{
		for (var current = node.Parent; current != null && current != root; current = current.Parent)
		{
			if (current is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
			{
				return true;
			}
		}
		return false;
	}
}
