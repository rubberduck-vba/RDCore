using RDCore.CLI.App.Repl;

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
