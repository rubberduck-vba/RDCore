using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;

namespace RDCore.Tests.Parser;

/// <summary>
/// A conditional-compilation directive is a logical line (<strong>MS-VBAL §3.2.2</strong>, §3.4): <see cref="ModuleParser"/> takes the physical lines a
/// line-continuation joins to a <c>#</c> line out of the module's code with it, and gives them to the directive's own parse, where they stood.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 3.4 Conditional Compilation")]
public sealed class ContinuedDirectiveTests
{
    private static readonly Uri Uri = TestUri.TestModuleUri();

    private static ModuleParseResult Parse(params string[] lines)
        => new ModuleParser().Parse(Uri, string.Join("\r\n", lines));

    [TestMethod]
    public void AContinuedConst_LeavesNothingInTheModulesCode()
    {
        var result = Parse("#Const LC = _", "    1", "Public Sub Main()", "End Sub");

        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);
        Assert.AreEqual("Main", result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single().Name);
    }

    [TestMethod]
    public void AContinuedConst_IsADirective()
    {
        var result = Parse("#Const LC = _", "    1", "Public Sub Main()", "End Sub");

        Assert.ContainsSingle(result.PrecompilerTrivia.SelectMany(node => node.Descendants().Prepend(node)).OfType<PrecompilerConstantDeclarationNode>());
    }

    [TestMethod]
    public void ASyntaxErrorAfterAContinuedDirective_IsOnItsOwnLine()
    {
        var result = Parse("#Const LC = _", "    1", "Public Sub Main()", "    Dim x As", "End Sub");

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(3, result.SyntaxErrors[0].Location.Range.Start.Line);
    }
}
