using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Types;

namespace RDCore.SDK.Semantics.Refactoring;

/// <summary>
/// The type a variable or a parameter has because it is declared without one, shown after its name (<strong>LSP 3.17</strong> §Inlay Hint Request).
/// </summary>
/// <param name="Position">Where the name, with its array bounds, ends: the hint is shown there.</param>
/// <param name="TypeName">The name of the type: <c>Variant</c>.</param>
public readonly record struct ImplicitTypeHint(SourcePosition Position, string TypeName);

/// <summary>
/// The declarations of a module that state no type, and are a <c>Variant</c> (<strong>MS-VBAL §5.2.3.1.5</strong>): <c>Dim x</c>, <c>Sub Show(Prompt)</c>.
/// </summary>
/// <remarks>
/// A declaration states its type with an <c>As</c> clause or with a type-declaration character. A name that a <c>Def&lt;Type&gt;</c> directive of the module covers
/// is given that type, not a <c>Variant</c>, and has no hint. A constant has none: its type is the type of its value. A parameter that is an array
/// (<c>Items()</c>) has none, since the hint cannot be shown where its type would be written.
/// </remarks>
public static class ImplicitTypeHints
{
    /// <summary>
    /// The hints of the declarations written in <paramref name="range"/> of <paramref name="module"/>.
    /// </summary>
    /// <param name="module">The module.</param>
    /// <param name="range">The range a client shows.</param>
    /// <returns>The hints, in the order they are written.</returns>
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
