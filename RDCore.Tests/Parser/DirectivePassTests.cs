using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Statements;

namespace RDCore.Tests.Parser;

/// <summary>
/// The directive pass of <see cref="ModuleParser"/> (<strong>MS-VBAL §3.4</strong>), parsed in two stages as the main pass is: SLL, which gives the tree
/// LL gives whenever it succeeds, and LL with error recovery when it does not.
/// </summary>
/// <remarks>
/// In LL alone, the 30 nested blocks of <see cref="NestedBlocks_AreEachTheirOwn_HoweverLongTheirBodies"/> took 12 s to parse: whether a line ends a block's
/// body is decided with the full context of every enclosing block.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 3.4 Conditional Compilation")]
public sealed class DirectivePassTests
{
    private static readonly Uri Uri = TestUri.TestModuleUri();

    private static ModuleParseResult Parse(IEnumerable<string> lines) => new ModuleParser().Parse(Uri, string.Join("\r\n", lines) + "\r\n");

    // a procedure of a body of many lines, in an #If block of its own, nested in another: what a module written for VBA7 and VBA6 alike has many of.
    private static IEnumerable<string> Procedure(int index)
        => [
            "#If VBA7 Then",
            "#If Win64 Then",
            $"Private Sub P{index}()",
            .. Enumerable.Range(0, 40).Select(line => $"    Debug.Print {line}"),
            "End Sub",
            "#End If",
            "#Else",
            $"Private Sub Q{index}()",
            "End Sub",
            "#End If",
        ];

    [TestMethod]
    public void NestedBlocks_AreEachTheirOwn_HoweverLongTheirBodies()
    {
        var result = Parse(Enumerable.Range(0, 30).SelectMany(Procedure));

        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);
        var blocks = result.PrecompilerTrivia.SelectMany(node => node.Descendants().Prepend(node)).OfType<PrecompilerIfBlockStatementNode>().ToList();
        Assert.HasCount(60, blocks, "an outer and an inner block for each procedure");
    }
}
