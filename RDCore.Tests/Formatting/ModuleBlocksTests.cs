using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing;
using RDCore.SDK.Formatting;

namespace RDCore.Tests.Formatting;

/// <summary>
/// The blocks of a module that a line is in and that the module does not close, and what a line break typed into a module brings: the new line indented, and
/// the closer of the block the line before it opens; and the same blocks for a program of the platform's BASIC, which a shell closes as it is typed.
/// </summary>
[TestClass]
public sealed class ModuleBlocksTests
{
    private static readonly Uri Module = new("file:///c:/ws/Module1.bas");
    private static readonly Uri Program = new("untitled:Program.rdc");

    // the text once the edits of a line break are made, as a client makes them: those at the same position in the order they are given.
    private static async Task<string> AfterLineBreakAsync(string text, int line)
    {
        var edits = await ModuleBlocks.OnLineBreakAsync(new ModuleParser().Parse(Module, text), text, line, CodeFormattingOptions.Default,
            (closed, _) => Task.FromResult(new ModuleParser().Parse(Module, closed)));
        var starts = LineStarts(text);
        foreach (var edit in edits.Reverse())
        {
            var start = starts[edit.Range.Start.Line] + edit.Range.Start.Character;
            var end = starts[edit.Range.End.Line] + edit.Range.End.Character;
            text = text[..start] + edit.NewText + text[end..];
        }

        return text;
    }

    private static int[] LineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n' || (text[index] == '\r' && (index + 1 == text.Length || text[index + 1] != '\n')))
            {
                starts.Add(index + 1);
            }
        }

        return [.. starts];
    }

    [TestMethod]
    [DataRow("Sub A()\r\n    If x Then\r\n\r\nEnd Sub", 2, "Sub A()\r\n    If x Then\r\n        \r\n    End If\r\nEnd Sub", DisplayName = "an If block gets its End If")]
    [DataRow("Sub A()\r\n    Select Case x\r\n\r\nEnd Sub", 2, "Sub A()\r\n    Select Case x\r\n    \r\n    End Select\r\nEnd Sub", DisplayName = "a Select Case gets its End Select, the new line where its Case goes")]
    [DataRow("Sub A()\r\n    For i = 1 To 3\r\n\r\nEnd Sub", 2, "Sub A()\r\n    For i = 1 To 3\r\n        \r\n    Next\r\nEnd Sub", DisplayName = "a For loop gets its Next")]
    [DataRow("Sub A()\r\n    Do While x\r\n\r\nEnd Sub", 2, "Sub A()\r\n    Do While x\r\n        \r\n    Loop\r\nEnd Sub", DisplayName = "a Do loop gets its Loop")]
    [DataRow("Sub A()\r\n    With x\r\n\r\nEnd Sub", 2, "Sub A()\r\n    With x\r\n        \r\n    End With\r\nEnd Sub", DisplayName = "a With block gets its End With")]
    [DataRow("Sub A()\n    If x Then\n\nEnd Sub", 2, "Sub A()\n    If x Then\n        \n    End If\nEnd Sub", DisplayName = "the closer ends as the lines of the module do")]
    public async Task ALineBreakAfterTheOpenerOfABlockWithNoCloser_IndentsTheNewLine_AndClosesTheBlock(string text, int line, string expected)
        => Assert.AreEqual(expected, await AfterLineBreakAsync(text, line));

    [TestMethod]
    [DataRow("Option Explicit\r\nPublic Sub A()\r\n", 2, "Option Explicit\r\nPublic Sub A()\r\n    \r\nEnd Sub", DisplayName = "a Sub")]
    [DataRow("Option Explicit\r\nPublic Function A() As Long\r\n", 2, "Option Explicit\r\nPublic Function A() As Long\r\n    \r\nEnd Function", DisplayName = "a Function")]
    [DataRow("Option Explicit\r\nPrivate Type TPoint\r\n", 2, "Option Explicit\r\nPrivate Type TPoint\r\n    \r\nEnd Type", DisplayName = "a Type")]
    public async Task ALineBreakAfterAProcedureWithNoCloser_ClosesIt_OnTheLastLineOfTheModule(string text, int line, string expected)
        => Assert.AreEqual(expected, await AfterLineBreakAsync(text, line));

    [TestMethod]
    public async Task ABlockInABlock_IsClosedAtItsOwnLevel()
        => Assert.AreEqual("Sub A()\r\n    If x Then\r\n        For i = 1 To 3\r\n            \r\n        Next\r\n    End If\r\nEnd Sub",
            await AfterLineBreakAsync("Sub A()\r\n    If x Then\r\n        For i = 1 To 3\r\n\r\n    End If\r\nEnd Sub", 3));

    [TestMethod]
    [DataRow("Sub A()\r\n    If x Then\r\n\r\n    End If\r\nEnd Sub", 2, "Sub A()\r\n    If x Then\r\n        \r\n    End If\r\nEnd Sub", DisplayName = "a block that has its closer")]
    [DataRow("Sub A()\r\n    If x Then y = 1\r\n\r\nEnd Sub", 2, "Sub A()\r\n    If x Then y = 1\r\n    \r\nEnd Sub", DisplayName = "a single-line If, which is no block")]
    [DataRow("Sub A()\r\n    If x Then\r\ny = 1\r\nEnd Sub", 2, "Sub A()\r\n    If x Then\r\n        y = 1\r\nEnd Sub", DisplayName = "a break before code, which the new line keeps")]
    [DataRow("Sub A()\r\n    If x Then\r\n        y = 1\r\n    Else\r\n\r\nEnd Sub", 4, "Sub A()\r\n    If x Then\r\n        y = 1\r\n    Else\r\n        \r\nEnd Sub", DisplayName = "a break after an Else, which opens no block")]
    [DataRow("Sub A()\r\n    y = 1\r\n\r\nEnd Sub", 2, "Sub A()\r\n    y = 1\r\n    \r\nEnd Sub", DisplayName = "a break after a statement")]
    public async Task ALineBreakThatOpensNoBlock_OnlyIndentsTheNewLine(string text, int line, string expected)
        => Assert.AreEqual(expected, await AfterLineBreakAsync(text, line));

    [TestMethod]
    public async Task TheLineTheBreakEnds_IsIndentedToo()
        => Assert.AreEqual("Sub A()\r\n    If x Then\r\n        y = 1\r\n    End If\r\n    \r\nEnd Sub",
            await AfterLineBreakAsync("Sub A()\r\n    If x Then\r\n        y = 1\r\n        End If\r\n\r\nEnd Sub", 4));

    [TestMethod]
    public async Task ALineThatIsNoLineOfTheModule_HasNoEdit()
    {
        const string text = "Sub A()\r\nEnd Sub";
        var parse = new ModuleParser().Parse(Module, text);

        Assert.IsEmpty(await ModuleBlocks.OnLineBreakAsync(parse, text, 0, CodeFormattingOptions.Default, (_, _) => Task.FromResult(parse)));
        Assert.IsEmpty(await ModuleBlocks.OnLineBreakAsync(parse, text, 5, CodeFormattingOptions.Default, (_, _) => Task.FromResult(parse)));
        Assert.IsEmpty(ModuleBlocks.OpenAt(parse, text, -1));
    }

    [TestMethod]
    public void TheBlocksOpenAtALine_AreTheInnermostFirst_WithTheirBranch()
    {
        const string text = "Sub A()\r\n    If x Then\r\n        y = 1\r\n    Else\r\n        For i = 1 To 3\r\n            z = i\r\nEnd Sub";

        var open = ModuleBlocks.OpenAt(new ModuleParser().Parse(Module, text), text, 5);

        CollectionAssert.AreEqual(new[] { new ModuleBlock(4, 4, "Next", true), new ModuleBlock(1, 3, "End If", true) }, open.ToArray());
    }

    [TestMethod]
    public void AProgram_HasTheBlocksOfItsLines_AndNotTheProcedureItIsTheBodyOf()
    {
        static IReadOnlyList<ProgramBlock> Open(int index, params string[] statements)
            => ProgramBlocks.OpenAt(statements, index, text => new ModuleParser().Parse(Program, text));

        CollectionAssert.AreEqual(new[] { new ProgramBlock(0, 0, "End If", true) }, Open(1, "If x > 0 Then", "y = 1").ToArray());
        Assert.IsEmpty(Open(1, "If x > 0 Then", "y = 1", "End If"));
        Assert.IsEmpty(Open(0, "y = 1"));
    }
}
