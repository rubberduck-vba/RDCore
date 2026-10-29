using RDCore.Parsing;
using RDCore.SDK.Formatting;

namespace RDCore.Tests.Formatting;

/// <summary>
/// The statements of a program of the platform's BASIC are indented by the blocks they are in, as a module's are: the statements in no block are at the left,
/// and a line's number is not part of its indentation.
/// </summary>
[TestClass]
public sealed class ProgramIndenterTests
{
    private static readonly Uri Program = new("untitled:Program.rdc");

    private static IReadOnlyList<string> Indented(CodeFormattingOptions options, params string[] statements)
        => ProgramIndenter.Indent(statements, text => new ModuleParser().Parse(Program, text), options);

    private static IReadOnlyList<string> Indented(params string[] statements) => Indented(CodeFormattingOptions.Default, statements);

    [TestMethod]
    public void NestedBlocks_AreALevelEach_AndWhatIsInNoBlockIsAtTheLeft()
    {
        var indented = Indented(
            "If x Then",
            "For i = 1 To 3",
            "y = y + i",
            "Next",
            "End If",
            "z = y");

        CollectionAssert.AreEqual(new[]
        {
            "If x Then",
            "    For i = 1 To 3",
            "        y = y + i",
            "    Next",
            "End If",
            "z = y",
        }, indented.ToArray());
    }

    [TestMethod]
    public void TheProcedureTheProgramIsTheBodyOf_IsNoLevel_WhateverTheSettingSays()
    {
        var indented = Indented(new CodeFormattingOptions { IndentEntireProcedureBody = true, IndentSpaces = 2 }, "x = 1", "If x Then", "y = 2", "End If");

        CollectionAssert.AreEqual(new[] { "x = 1", "If x Then", "  y = 2", "End If" }, indented.ToArray());
    }

    [TestMethod]
    public void TheCaseLinesOfASelectCase_AreIndented_WhenTheSettingSays()
    {
        string[] program = ["Select Case x", "Case 1", "y = 1", "Case Else", "y = 2", "End Select"];

        CollectionAssert.AreEqual(new[] { "Select Case x", "Case 1", "    y = 1", "Case Else", "    y = 2", "End Select" },
            Indented(new CodeFormattingOptions { IndentCase = false }, program).ToArray());
        CollectionAssert.AreEqual(new[] { "Select Case x", "    Case 1", "        y = 1", "    Case Else", "        y = 2", "End Select" },
            Indented(new CodeFormattingOptions { IndentCase = true }, program).ToArray());
    }

    [TestMethod]
    public void TheIndentationAStatementWasTypedWith_IsNotKept()
        => CollectionAssert.AreEqual(new[] { "x = 1", "y = 2" }, Indented("      x = 1", "\ty = 2").ToArray());

    [TestMethod]
    public void ABlockThatIsNotClosedYet_IsIndentedToTheLastLine()
        => CollectionAssert.AreEqual(new[] { "Do While x < 10", "    x = x + 1" }, Indented("Do While x < 10", "x = x + 1").ToArray());

    [TestMethod]
    public void TheCommentAfterAStatement_KeepsItsGap_WhenTheSettingSaysSo()
        => CollectionAssert.AreEqual(new[] { "If x Then", "    y = 1   ' one", "End If" },
            Indented(new CodeFormattingOptions { EndOfLineCommentStyle = EndOfLineCommentStyle.SameGap }, "If x Then", "y = 1   ' one", "End If").ToArray());

    [TestMethod]
    public void AStatementWithALineEndInIt_LeavesTheProgramAsItWasGiven()
    {
        string[] program = ["If x Then", "y = 1\r\nz = 2", "End If"];

        CollectionAssert.AreEqual(program, Indented(program).ToArray());
    }

    [TestMethod]
    public void TheStatements_AreTheLinesOfTheModuleTheIndenterIndents()
    {
        // what the indenter would make of the module the program is, line for line: alignment and comments too.
        string[] program = ["Dim a As Long ' first", "Dim bee As String", "If a Then", "Select Case a", "Case 1", "b = 1   ' one", "End Select", "End If", "Rem done"];
        var options = CodeFormattingOptions.Default with { IndentCase = true, AlignDims = true };
        var text = RDCore.SDK.Workspace.BasicProgramText.ToModuleSource(string.Join("\r\n", program) + "\r\n");
        var module = ModuleIndenter.Apply(text, ModuleIndenter.Indent(new ModuleParser().Parse(Program, text), text,
            options with { IndentEntireProcedureBody = false, VerticallySpaceProcedures = false }));

        CollectionAssert.AreEqual(module.Split("\r\n")[1..(1 + program.Length)], Indented(options, program).ToArray());
    }

    [TestMethod]
    public void NoStatements_AreNoStatements()
        => Assert.IsEmpty(Indented());

    [TestMethod]
    public void IndentingAnIndentedProgram_ChangesNothing()
    {
        var once = Indented("While x", "If y Then", "z = 1", "Else", "z = 2", "End If", "Wend");

        CollectionAssert.AreEqual(once.ToArray(), Indented([.. once]).ToArray());
    }
}
