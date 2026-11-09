using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Types;

namespace RDCore.LanguageServer.Services.Refactoring;

public readonly record struct ImplicitTypeHint(SourcePosition Position, string TypeName);

// an array parameter (Items()) gets no hint: there is nowhere to show the type where it would be written.
// MS-VBAL §5.2.3.1.5
public static class ImplicitTypeHints
{
    public static IReadOnlyList<ImplicitTypeHint> In(ModuleNode module, SourceRange range)
    {
        var directives = module.Children.OfType<TypeDefDirectiveNode>().ToList();
        var hints = new List<ImplicitTypeHint>();
        foreach (var node in module.Descendants())
        {
            var (name, typeHint, end) = node switch
            {
                VariableDeclarationNode variable => (variable.Name, variable.TypeHint, (SourcePosition?)variable.SourceLocation.Range.End),
                ParameterDeclarationNode { IsArray: false, NameRange: { } nameRange } parameter => (parameter.Name, parameter.TypeHint, nameRange.End),
                _ => (null, null, null),
            };

            if (name is null || end is not { } position || position < range.Start || position > range.End
                || !string.IsNullOrEmpty(typeHint) || node.Children.OfType<AsTypeExpressionNode>().Any())
            {
                continue;
            }

            if (TypeDefDirectiveNode.Covering(directives, name) is { } directive && directive.TypeName != VBTypeNames.VBVariant)
            {
                continue;
            }

            hints.Add(new ImplicitTypeHint(position, VBTypeNames.VBVariant));
        }

        return [.. hints.OrderBy(hint => hint.Position)];
    }
}
