using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Statements;

namespace RDCore.Tests.Parser;

/// <summary>
/// The directive pass of <see cref="ModuleParser"/> (<strong>MS-VBAL §3.4</strong>), parsed in SLL, which gives the tree LL gives whenever it succeeds,
/// and in SLL with error recovery when it does not: a module whose directives do not fit the grammar is recovered from as fast.
/// </summary>
/// <remarks>
/// In LL alone, the 30 nested blocks of <see cref="NestedBlocks_AreEachTheirOwn_HoweverLongTheirBodies"/> took 12 s to parse: whether a line ends a block's
/// body is decided with the full context of every enclosing block. A module of 100 blocks after a stray <c>#End If</c> took 46 s in LL, and one of 300
/// overflowed the stack.
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

    // a Declare written for VBA7 and for VBA6 alike, in a block of its own: what a module of Declares has one of for each.
    private static IEnumerable<string> Declare(int index)
        => [
            "#If VBA7 Then",
            $"Private Declare PtrSafe Function F{index} Lib \"kernel32\" Alias \"GetTickCount\" () As Long",
            "#Else",
            $"Private Declare Function F{index} Lib \"kernel32\" Alias \"GetTickCount\" () As Long",
            "#End If",
            "",
        ];

    private static List<PrecompilerIfBlockStatementNode> Blocks(ModuleParseResult result)
        => [.. result.PrecompilerTrivia.SelectMany(node => node.Descendants().Prepend(node)).OfType<PrecompilerIfBlockStatementNode>()];

    [TestMethod]
    [DataRow("#End If", 100, DisplayName = "a stray #End If before them")]
    [DataRow("#Else", 100, DisplayName = "a stray #Else before them")]
    [DataRow("#If Mac Then", 101, DisplayName = "an #If that is never ended before them, which holds them all")]
    public void DirectivesThatDoNotFit_AreParsedAsFast_AndTheBlocksAfterThemAreEachTheirOwn(string first, int blocks)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = Parse(Enumerable.Range(0, 100).SelectMany(Declare).Prepend(first));
        watch.Stop();

        Assert.HasCount(blocks, Blocks(result));
        Assert.IsLessThan(10.0, watch.Elapsed.TotalSeconds, "a parse in well under a second, that took 46 s in LL");
    }

    // the blocks, branches and bodies of a module's trivia, with where each is: what says which lines each branch holds.
    private static string Shape(ModuleParseResult result) => string.Join(" ", result.PrecompilerTrivia.Select(Shape).Where(shape => shape.Length > 0));

    private static string Shape(SyntaxNode node)
    {
        var kind = node.GetType().Name.Replace("Precompiler", "").Replace("StatementNode", "").Replace("Node", "");
        if (kind.Contains("Expression") || kind.StartsWith("VB"))
        {
            return "";
        }

        var range = node.SourceLocation.Range;
        var children = string.Join(" ", node.Children.Select(Shape).Where(shape => shape.Length > 0));
        return $"{kind}[{range.Start.Line}:{range.Start.Character}-{range.End.Line}:{range.End.Character}]" + (children.Length > 0 ? $"({children})" : "");
    }

    // a well-formed module, and a directive that is wrong in one, which MS-VBA refuses to compile: what a line is no directive of is a line of the body it is
    // in, and the blocks around it and after it hold what they would. An #If that is never ended holds what follows it.
    [TestMethod]
    [DataRow(new[] { "#If VBA7 Then", "#If Win64 Then", "a = 1", "#Else", "b = 1", "#End If", "#ElseIf Mac Then", "c = 1", "#Else", "d = 1", "#End If", "e = 1" },
        "Trivia[0:0-12:0](IfBlock[0:0-11:0](InlineIf[0:0-1:0] Trivia[1:0-6:0](IfBlock[1:0-6:0](InlineIf[1:0-2:0] Trivia[2:0-3:0] ElseBlock[3:0-5:0](Trivia[4:0-5:0]))) ElseIfBlock[6:0-8:0](Trivia[7:0-8:0]) ElseBlock[8:0-10:0](Trivia[9:0-10:0])))", DisplayName = "nested blocks, an #ElseIf and an #Else")]
    [DataRow(new[] { "#If(VBA7) Then", "a = 1", "#Else", "b = 1", "#EndIf", "#If (Win64)Then", "c = 1", "#End If ' x" },
        "Trivia[0:0-8:0](IfBlock[0:0-5:0](InlineIf[0:0-1:0] Trivia[1:0-2:0] ElseBlock[2:0-4:0](Trivia[3:0-4:0])) IfBlock[5:0-8:0](InlineIf[5:0-6:0] Trivia[6:0-7:0]))", DisplayName = "conditions in parentheses, #EndIf, a comment after #End If")]
    [DataRow(new[] { "# _", "  If VBA7 Then", "a = 1", "#Else", "b = 1", "# End _", "  If", "c = 1" },
        "Trivia[0:0-8:0](IfBlock[0:0-7:0](InlineIf[0:0-2:0] Trivia[2:0-3:0] ElseBlock[3:0-5:0](Trivia[4:0-5:0])))", DisplayName = "directives continued over lines")]
    [DataRow(new[] { "Sub P()", "    #If VBA7 Then", "    a = 1", "    #ElseIf Win64 Then", "    b = 1", "    #ElseIf Mac Then", "    c = 1", "    #Else", "    d = 1", "    #End If", "End Sub" },
        "Trivia[0:0-11:0](IfBlock[1:0-10:0](InlineIf[1:0-2:0] Trivia[2:0-3:0] ElseIfBlock[3:0-5:0](Trivia[4:0-5:0]) ElseIfBlock[5:0-7:0](Trivia[6:0-7:0]) ElseBlock[7:0-9:0](Trivia[8:0-9:0])))", DisplayName = "a block in a procedure")]
    [DataRow(new[] { "#If A Then", "a = 1", "#ElseIf B Then junk", "b = 1", "#End If", "x = 1", "#If C Then", "c = 1", "#End If" },
        "Trivia[0:0-9:0](IfBlock[0:0-5:0](InlineIf[0:0-1:0] Trivia[1:0-4:0]) IfBlock[6:0-9:0](InlineIf[6:0-7:0] Trivia[7:0-8:0]))", DisplayName = "an #ElseIf with text after its Then")]
    [DataRow(new[] { "#If A Then", "a = 1", "#ElseIf B", "b = 1", "#End If", "x = 1", "#If C Then", "c = 1", "#End If" },
        "Trivia[0:0-9:0](IfBlock[0:0-5:0](InlineIf[0:0-1:0] Trivia[1:0-4:0]) IfBlock[6:0-9:0](InlineIf[6:0-7:0] Trivia[7:0-8:0]))", DisplayName = "an #ElseIf with no Then")]
    [DataRow(new[] { "#If A Then", "a = 1", "#Else", "b = 1", "#ElseIf C Then", "c = 1", "#End If", "x = 1", "#If D Then", "d = 1", "#Else", "e = 1", "#End If", "y = 1" },
        "Trivia[0:0-14:0](IfBlock[0:0-7:0](InlineIf[0:0-1:0] Trivia[1:0-2:0] ElseBlock[2:0-6:0](Trivia[3:0-6:0])) IfBlock[8:0-13:0](InlineIf[8:0-9:0] Trivia[9:0-10:0] ElseBlock[10:0-12:0](Trivia[11:0-12:0])))", DisplayName = "an #ElseIf after the #Else")]
    [DataRow(new[] { "#If A Then", "a = 1", "#Else", "b = 1", "#Else", "c = 1", "#End If", "x = 1", "#If D Then", "d = 1", "#Else", "e = 1", "#End If", "y = 1" },
        "Trivia[0:0-14:0](IfBlock[0:0-7:0](InlineIf[0:0-1:0] Trivia[1:0-2:0] ElseBlock[2:0-6:0](Trivia[3:0-6:0])) IfBlock[8:0-13:0](InlineIf[8:0-9:0] Trivia[9:0-10:0] ElseBlock[10:0-12:0](Trivia[11:0-12:0])))", DisplayName = "a second #Else")]
    [DataRow(new[] { "#If Mac Then", "a = 1", "#If D Then", "d = 1", "#Else", "e = 1", "#End If", "y = 1" },
        "Trivia[0:0-8:0](IfBlock[0:0-8:0](InlineIf[0:0-1:0] Trivia[1:0-8:0](IfBlock[2:0-7:0](InlineIf[2:0-3:0] Trivia[3:0-4:0] ElseBlock[4:0-6:0](Trivia[5:0-6:0])))))", DisplayName = "an #If never ended, at the top")]
    [DataRow(new[] { "#If D Then", "d = 1", "#Else", "e = 1", "#End If", "#If Mac Then", "a = 1", "#If W Then", "w = 1", "#Else", "v = 1", "#End If", "y = 1" },
        "Trivia[0:0-13:0](IfBlock[0:0-5:0](InlineIf[0:0-1:0] Trivia[1:0-2:0] ElseBlock[2:0-4:0](Trivia[3:0-4:0])) IfBlock[5:0-13:0](InlineIf[5:0-6:0] Trivia[6:0-13:0](IfBlock[7:0-12:0](InlineIf[7:0-8:0] Trivia[8:0-9:0] ElseBlock[9:0-11:0](Trivia[10:0-11:0])))))", DisplayName = "an #If never ended, after a block")]
    [DataRow(new[] { "#If VBA7", "a = 1", "#Else", "b = 1", "#End If", "x = 1", "#If D Then", "d = 1", "#Else", "e = 1", "#End If" },
        "Trivia[0:0-2:0] Trivia[3:0-4:0] Trivia[5:0-11:0](IfBlock[6:0-11:0](InlineIf[6:0-7:0] Trivia[7:0-8:0] ElseBlock[8:0-10:0](Trivia[9:0-10:0])))", DisplayName = "an #If with no Then")]
    [DataRow(new[] { "#If A Then", "a = 1", "#Else junk", "b = 1", "#End If", "x = 1" },
        "Trivia[0:0-6:0](IfBlock[0:0-5:0](InlineIf[0:0-1:0] Trivia[1:0-4:0]))", DisplayName = "an #Else with text after it")]
    [DataRow(new[] { "#If A Then", "a = 1", "#End If", "#End If", "x = 1", "#If D Then", "d = 1", "#Else", "e = 1", "#End If" },
        "Trivia[0:0-3:0](IfBlock[0:0-3:0](InlineIf[0:0-1:0] Trivia[1:0-2:0])) Trivia[4:0-10:0](IfBlock[5:0-10:0](InlineIf[5:0-6:0] Trivia[6:0-7:0] ElseBlock[7:0-9:0](Trivia[8:0-9:0])))", DisplayName = "a stray #End If between blocks")]
    [DataRow(new[] { "#If A Then", "#If B Then", "b = 1", "#Else", "c = 1", "#Else", "d = 1", "#End If", "e = 1", "#Else", "f = 1", "#End If", "g = 1" },
        "Trivia[0:0-13:0](IfBlock[0:0-12:0](InlineIf[0:0-1:0] Trivia[1:0-9:0](IfBlock[1:0-8:0](InlineIf[1:0-2:0] Trivia[2:0-3:0] ElseBlock[3:0-7:0](Trivia[4:0-7:0]))) ElseBlock[9:0-11:0](Trivia[10:0-11:0])))", DisplayName = "a second #Else in an inner block")]
    [DataRow(new[] { "#If A Then", "#If B Then", "b = 1", "#Else", "c = 1", "#ElseIf X Then", "d = 1", "#End If", "e = 1", "#Else", "f = 1", "#End If", "g = 1" },
        "Trivia[0:0-13:0](IfBlock[0:0-12:0](InlineIf[0:0-1:0] Trivia[1:0-9:0](IfBlock[1:0-8:0](InlineIf[1:0-2:0] Trivia[2:0-3:0] ElseBlock[3:0-7:0](Trivia[4:0-7:0]))) ElseBlock[9:0-11:0](Trivia[10:0-11:0])))", DisplayName = "an #ElseIf after the #Else of an inner block")]
    [DataRow(new[] { "#ElseIf junk", "x = 1", "#If D Then", "d = 1", "#Else", "e = 1", "#End If" },
        "Trivia[0:0-7:0](IfBlock[2:0-7:0](InlineIf[2:0-3:0] Trivia[3:0-4:0] ElseBlock[4:0-6:0](Trivia[5:0-6:0])))", DisplayName = "a stray #ElseIf at the top")]
    [DataRow(new[] { "#If A Then", "#If B Then", "b = 1", "#ElseIf B Then junk", "c = 1", "#End If", "e = 1", "#Else", "f = 1", "#End If", "g = 1" },
        "Trivia[0:0-11:0](IfBlock[0:0-10:0](InlineIf[0:0-1:0] Trivia[1:0-7:0](IfBlock[1:0-6:0](InlineIf[1:0-2:0] Trivia[2:0-5:0])) ElseBlock[7:0-9:0](Trivia[8:0-9:0])))", DisplayName = "an #ElseIf with text after its Then in an inner block")]
    [DataRow(new[] { "#Const X = 1", "#If X = 1 Then", "a = 1", "#End If" },
        "Trivia[0:0-4:0](ConstantDeclaration[0:0-1:0] IfBlock[1:0-4:0](InlineIf[1:0-2:0] Trivia[2:0-3:0]))", DisplayName = "a #Const and an #If of it")]
    [DataRow(new[] { "#If A Then", "a = 1", "#End If", "#Else", "x = 1", "#If D Then", "#If E Then", "d = 1", "#End If", "#Else", "e = 1", "#End If" },
        "Trivia[0:0-3:0](IfBlock[0:0-3:0](InlineIf[0:0-1:0] Trivia[1:0-2:0])) Trivia[4:0-12:0](IfBlock[5:0-12:0](InlineIf[5:0-6:0] Trivia[6:0-9:0](IfBlock[6:0-9:0](InlineIf[6:0-7:0] Trivia[7:0-8:0])) ElseBlock[9:0-11:0](Trivia[10:0-11:0])))", DisplayName = "a stray #Else between blocks")]
    [DataRow(new[] { "#If A Then", "#If B Then", "b = 1", "#End If", "#End If", "#End If", "x = 1", "#If D Then", "d = 1", "#Else", "e = 1", "#End If" },
        "Trivia[0:0-5:0](IfBlock[0:0-5:0](InlineIf[0:0-1:0] Trivia[1:0-4:0](IfBlock[1:0-4:0](InlineIf[1:0-2:0] Trivia[2:0-3:0])))) Trivia[6:0-12:0](IfBlock[7:0-12:0](InlineIf[7:0-8:0] Trivia[8:0-9:0] ElseBlock[9:0-11:0](Trivia[10:0-11:0])))", DisplayName = "an #End If too many after nested blocks")]
    [DataRow(new[] { "#If A Then", "#If B Then", "b = 1", "#Else", "c = 1", "#End If", "x = 1", "#If D Then", "d = 1", "#Else", "e = 1", "#End If" },
        "Trivia[0:0-12:0](IfBlock[0:0-12:0](InlineIf[0:0-1:0] Trivia[1:0-12:0](IfBlock[1:0-6:0](InlineIf[1:0-2:0] Trivia[2:0-3:0] ElseBlock[3:0-5:0](Trivia[4:0-5:0])) IfBlock[7:0-12:0](InlineIf[7:0-8:0] Trivia[8:0-9:0] ElseBlock[9:0-11:0](Trivia[10:0-11:0])))))", DisplayName = "an inner #If never ended")]
    public void TheBlocksOfAModule_HoldTheLinesTheyWouldHold_WhateverIsWrongWithADirective(string[] lines, string shape)
        => Assert.AreEqual(shape, Shape(Parse(lines)));

    [TestMethod]
    public void AnEndIfWithTextAfterIt_IsNoDirective_AndTheBlockGoesOnToTheNext()
    {
        // MS-VBA reads `#End If junk` as no directive: the block it is in is ended by the #End If after it.
        var result = Parse(["#If VBA7 Then", "Private Const A = 1", "#End If junk", "Private Const B = 1", "#End If"]);

        var block = Assert.ContainsSingle(Blocks(result));
        // the block's range ends with the line of its #End If: the start of the line after it.
        Assert.AreEqual((5, 0), (block.SourceLocation.Range.End.Line, block.SourceLocation.Range.End.Character), "ended by the #End If of line 4, not by line 2");
    }
}
