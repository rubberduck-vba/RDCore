using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Runtime.LanguageCore.Static;

// a statement with no object is the method of the form or report it is written in: a Visual Basic 6.0 form's code draws on the form (drawsOnMe), and anywhere
// else it is as undefined as any other undeclared name, in a document module that is a UserForm or an Access form or report too.
public static class GraphicsStatementStaticSemantics
{
    public static VBCompileErrorInfo? Evaluate(GraphicsMethodStatementNode graphics, bool drawsOnMe = false)
        => graphics is { Target: null, IsWithRelative: false } && !drawsOnMe
            ? VBCompileErrorInfo.For(VBCompileErrorId.SubOrFunctionNotDefined, graphics.SourceLocation,
                $"'{graphics.Token}' is not defined: written with no object, it is the {graphics.Token} method of the form or report it is written in, which draws only in the code of a Visual Basic 6.0 form.")
            : null;

    public static IEnumerable<ExpressionNode> Expressions(GraphicsMethodStatementNode graphics)
        => graphics.Children.SelectMany(child => child is GraphicsPointNode point ? [point.X, point.Y] : child is ExpressionNode expression ? [expression] : Array.Empty<ExpressionNode>());
}
