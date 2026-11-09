using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;

namespace RDCore.Runtime.LanguageCore.Static;

public static class GraphicsStatementStaticSemantics
{
    public static VBCompileErrorInfo? Evaluate(GraphicsMethodStatementNode graphics)
        => graphics is { Target: null, IsWithRelative: false }
            ? VBCompileErrorInfo.For(VBCompileErrorId.SubOrFunctionNotDefined, graphics.SourceLocation,
                $"'{graphics.Token}' is not defined: written with no object, it is the {graphics.Token} method of the form or report it is written in, and the platform has no surface to draw on.")
            : null;

    public static IEnumerable<ExpressionNode> Expressions(GraphicsMethodStatementNode graphics)
        => graphics.Children.SelectMany(child => child is GraphicsPointNode point ? [point.X, point.Y] : child is ExpressionNode expression ? [expression] : Array.Empty<ExpressionNode>());
}
