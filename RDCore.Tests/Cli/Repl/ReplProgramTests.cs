using RDCore.CLI.App.Repl;
using RDCore.Parsing;
using RDCore.SDK.Formatting;

namespace RDCore.Tests.Cli.Repl;

/// <summary>
/// The shell's program buffer: numbered lines in number order, and the real VBA module they render
/// to — line numbers are MS-VBAL line-number labels, not a shell invention.
/// </summary>
[TestClass]
public sealed class ReplProgramTests
{
    private static ReplProgram WithLines(params (int Number, string Statement)[] lines)
    {
        var program = new ReplProgram();
        foreach (var (number, statement) in lines)
        {
            program.Store(number, statement);
        }
        return program;
    }

    [TestMethod]
    public void ANewProgram_IsEmpty()
    {
        var sut = new ReplProgram();

        Assert.IsTrue(sut.IsEmpty);
        Assert.AreEqual(0, sut.Count);
        Assert.AreEqual(0, sut.Version);
    }

    [TestMethod]
    public void Lines_ComeBackInNumberOrder_WhateverOrderTheyWereTypedIn()
    {
        var sut = WithLines((30, "C"), (10, "A"), (20, "B"));

        CollectionAssert.AreEqual(new[] { 10, 20, 30 }, sut.Lines().Select(line => line.Number).ToArray());
    }

    [TestMethod]
    public void Store_AtANumberThatAlreadyHasALine_ReplacesIt()
    {
        var sut = WithLines((10, "A"), (10, "B"));

        Assert.AreEqual(1, sut.Count);
        Assert.AreEqual("B", sut.Lines().Single().Statement);
    }

    [TestMethod]
    public void Delete_RemovesThatLine()
    {
        var sut = WithLines((10, "A"), (20, "B"));

        Assert.IsTrue(sut.Delete(10));
        Assert.AreEqual(1, sut.Count);
        Assert.AreEqual(20, sut.Lines().Single().Number);
    }

    [TestMethod]
    public void Delete_ALineThatIsNotThere_ReportsSo_AndChangesNothing()
    {
        var sut = WithLines((10, "A"));
        var version = sut.Version;

        Assert.IsFalse(sut.Delete(999));
        Assert.AreEqual(version, sut.Version);
    }

    [TestMethod]
    public void EveryEdit_BumpsTheVersion_AndANoOpDoesNot()
    {
        var sut = new ReplProgram();

        sut.Store(10, "A");
        var afterStore = sut.Version;
        sut.Clear();
        var afterClear = sut.Version;
        sut.Clear();

        Assert.IsGreaterThan(0, afterStore);
        Assert.IsGreaterThan(afterStore, afterClear);
        Assert.AreEqual(afterClear, sut.Version);
    }

    [TestMethod]
    public void Lines_NarrowsToARange_Inclusively()
    {
        var sut = WithLines((10, "A"), (20, "B"), (30, "C"), (40, "D"));

        CollectionAssert.AreEqual(new[] { 20, 30 }, sut.Lines(20, 30).Select(line => line.Number).ToArray());
        CollectionAssert.AreEqual(new[] { 30, 40 }, sut.Lines(from: 30).Select(line => line.Number).ToArray());
        CollectionAssert.AreEqual(new[] { 10, 20 }, sut.Lines(to: 20).Select(line => line.Number).ToArray());
    }

    [TestMethod]
    public void ToModuleSource_IsAProcedureOfLabelledStatements()
    {
        var sut = WithLines((20, "Debug.Print X"), (10, "X = 5"));

        Assert.AreEqual(
            "Public Sub Main()\r\n10 X = 5\r\n20 Debug.Print X\r\nEnd Sub\r\n",
            sut.ToModuleSource());
    }

    [TestMethod]
    public void LineNumberAt_MapsAGeneratedSourceLineBackToTheProgramsOwn()
    {
        // what a run-time error's position means to whoever typed the program: the platform locates an error
        // in the source it was handed, which here is generated, so its line is one nobody typed.
        var sut = WithLines((20, "Debug.Print X"), (10, "X = 5"));

        Assert.IsNull(sut.LineNumberAt(0), "the procedure header is not one of the program's lines");
        Assert.AreEqual(10, sut.LineNumberAt(1));
        Assert.AreEqual(20, sut.LineNumberAt(2));
        Assert.IsNull(sut.LineNumberAt(3), "End Sub, and anything an immediate-mode statement appends after it");
        Assert.IsNull(sut.LineNumberAt(-1), "a frame that carries no position at all");
    }

    // a buffer that stores its statements indented, by as many spaces a level as the test says.
    private static ReplProgram Indenting(Func<int> spaces)
        => new(statements => ProgramIndenter.Indent(statements, text => new ModuleParser().Parse(ReplProgram.IndentedModuleUri, text),
            new CodeFormattingOptions { IndentSpaces = spaces() }));

    [TestMethod]
    public void AStoredLine_IsStoredIndented_AndTheLinesItOpensABlockForAreIndentedAgain()
    {
        var sut = Indenting(() => 4);
        sut.Store(10, "If X Then");
        sut.Store(30, "End If");
        sut.Store(20, "Y = 1");

        CollectionAssert.AreEqual(new[] { "If X Then", "    Y = 1", "End If" }, sut.Lines().Select(line => line.Statement).ToArray());
        Assert.AreEqual("10 If X Then\r\n20     Y = 1\r\n30 End If\r\n", sut.ToSourceText(), "what is saved, and what the language server holds");
        Assert.AreEqual(3, sut.Version, "a version for each line stored, however many lines were indented again");
    }

    [TestMethod]
    public void ADeletedLine_IndentsTheLinesAgain()
    {
        var sut = Indenting(() => 4);
        sut.Store(10, "For I = 1 To 3");
        sut.Store(20, "X = I");
        sut.Store(30, "Next");

        Assert.IsTrue(sut.Delete(10));

        Assert.AreEqual("X = I", sut.Lines().First().Statement, "the line is in no block any more");
    }

    [TestMethod]
    public void ALoadedProgram_IsIndented()
    {
        var sut = Indenting(() => 2);

        Assert.IsEmpty(sut.Load("10 WHILE X\r\n20 X = X - 1\r\n30 WEND\r\n"));

        Assert.AreEqual("  X = X - 1", sut.Lines().ElementAt(1).Statement);
    }

    [TestMethod]
    public void WhenTheIndentationSettingsChange_TheProgramIsIndentedAgain_Once()
    {
        var spaces = 4;
        var sut = Indenting(() => spaces);
        sut.Store(10, "If X Then");
        sut.Store(20, "Y = 1");
        sut.Store(30, "End If");
        var version = sut.Version;

        spaces = 2;
        Assert.IsFalse(sut.IndentIfChanged(), "nothing said the settings changed");
        sut.IndentationChanged();

        Assert.IsTrue(sut.IndentIfChanged());
        Assert.AreEqual("  Y = 1", sut.Lines().ElementAt(1).Statement);
        Assert.AreEqual(version + 1, sut.Version, "the language server is told of the program as it is now");
        Assert.IsFalse(sut.IndentIfChanged(), "and it is indented once");
    }

    [TestMethod]
    public void ABufferThatDoesNotIndent_StoresWhatIsTyped()
    {
        var sut = WithLines((10, "If X Then"), (20, "   Y = 1"));

        Assert.AreEqual("   Y = 1", sut.Lines().ElementAt(1).Statement);
    }

    [TestMethod]
    public void ToModuleSource_OfAnEmptyBuffer_IsStillAValidEmptyProcedure()
        => Assert.AreEqual("Public Sub Main()\r\nEnd Sub\r\n", new ReplProgram().ToModuleSource());

    [TestMethod]
    public void ToImmediateModuleSource_AppendsTheStatementAsASecondProcedure()
    {
        // the immediate line compiles in the same module as the program, so it sees the same scope.
        var sut = WithLines((10, "X = 5"));

        Assert.AreEqual(
            "Public Sub Main()\r\n10 X = 5\r\nEnd Sub\r\n\r\nPublic Sub Immediate()\r\nDebug.Print X\r\nEnd Sub\r\n",
            sut.ToImmediateModuleSource("Debug.Print X"));
    }
}
