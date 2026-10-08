using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// Where a <c>Circle</c>, <c>Line</c>, <c>PSet</c> or <c>Scale</c> statement draws.
/// </summary>
/// <remarks>
/// They are the methods of whatever can be drawn on - a form, a picture box, a printer - and a statement written with no object is the method of the form or
/// report it is written in. The platform has no such surface, so a statement of that shape is as undefined as any other name the language does not declare.
/// A statement that does name an object (or that is the member of the object of a <c>With</c> block) is a call like any other, which the object either has or has not.
/// <para>
/// 🚧 TODO a document module that is a form or a report has these members, and an unqualified statement in one is the statement of its own surface.
/// </para>
/// </remarks>
public static class GraphicsStatementStaticSemantics
{
    /// <summary>
    /// Evaluates a graphics statement for the object it draws on.
    /// </summary>
    /// <param name="graphics">The statement.</param>
    /// <returns>The error, or <see langword="null"/> when the statement names an object to draw on.</returns>
    public static VBCompileErrorInfo? Evaluate(GraphicsMethodStatementNode graphics)
        => graphics is { Target: null, IsWithRelative: false }
            ? VBCompileErrorInfo.For(VBCompileErrorId.SubOrFunctionNotDefined, graphics.SourceLocation,
                $"'{graphics.Token}' is not defined: written with no object, it is the {graphics.Token} method of the form or report it is written in, and the platform has no surface to draw on.")
            : null;

    /// <summary>
    /// The expressions of a graphics statement, in source order: the object, the coordinates of its points and its arguments, apart from those it leaves out.
    /// </summary>
    /// <param name="graphics">The statement.</param>
    public static IEnumerable<ExpressionNode> Expressions(GraphicsMethodStatementNode graphics)
        => graphics.Children.SelectMany(child => child is GraphicsPointNode point ? [point.X, point.Y] : child is ExpressionNode expression ? [expression] : Array.Empty<ExpressionNode>());
}
