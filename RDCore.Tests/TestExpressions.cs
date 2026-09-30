using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.Tests;

/// <summary>
/// Expression nodes for a test that has to build one by hand, rather than parse for it.
/// </summary>
internal static class TestExpressions
{
    /// <summary>
    /// A literal carrying <paramref name="value"/> — the shape a <c>Const</c> declaration or an
    /// <c>Optional</c> parameter's <c>default-value</c> clause reduces to in the simplest case.
    /// </summary>
    internal static LiteralExpressionNode Literal(VBTypedValue value)
        => new(new SyntaxNodeId(TestUri.TestModuleUri().AbsolutePath, []),
            new SourceLocation(TestUri.TestModuleUri(), SourceRange.Empty), value);
}
