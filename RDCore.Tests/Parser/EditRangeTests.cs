using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;

namespace RDCore.Tests.Parser;

/// <summary>
/// What a fix places its edits by, in the parse: the line ending of the module's text, the keywords of a parameter, and the parameter list and the closing
/// statement of a procedure.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.3.1 Procedure Declarations")]
public sealed class EditRangeTests
{
    private static readonly Uri Module = new("file:///c:/ws/Mod1.bas");

    private static string? TextOf(string source, SourceRange? range)
    {
        if (range is not { } written)
        {
            return null;
        }

        var lines = source.Split("\r\n");
        return written.Start.Line == written.End.Line
            ? lines[written.Start.Line][written.Start.Character..written.End.Character]
            : string.Join("\r\n", [lines[written.Start.Line][written.Start.Character..], .. lines[(written.Start.Line + 1)..written.End.Line], lines[written.End.Line][..written.End.Character]]);
    }

    private static MemberDeclarationNode MemberOf(string source)
    {
        var parse = new ModuleParser().Parse(Module, source);
        Assert.IsEmpty(parse.SyntaxErrors);
        return Assert.ContainsSingle(parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>());
    }

    [TestMethod]
    [DataRow("Option Explicit\r\nPublic Sub Main()\r\nEnd Sub\r\n", "\r\n", DisplayName = "CR LF")]
    [DataRow("Option Explicit\nPublic Sub Main()\nEnd Sub\n", "\n", DisplayName = "LF")]
    [DataRow("Option Explicit\rPublic Sub Main()\rEnd Sub\r", "\r", DisplayName = "CR")]
    [DataRow("Option Explicit\nPublic Sub Main()\r\nEnd Sub\r\n", "\n", DisplayName = "mixed, the first line's")]
    [DataRow("Option Explicit", "\r\n", DisplayName = "one line")]
    public void TheLineEnding_IsTheOneTheFirstLineEndsWith(string source, string expected)
        => Assert.AreEqual(expected, new ModuleParser().Parse(Module, source).LineEnding);

    [TestMethod]
    [DataRow("Option Explicit\r\nPublic Sub Main()\r\nEnd Sub\r\n", "0:0-3:0", DisplayName = "a last line that ends")]
    [DataRow("Option Explicit\r\nPublic Sub Main()\r\nEnd Sub", "0:0-2:7", DisplayName = "a last line that does not end")]
    [DataRow("Option Explicit\r\n\r\n", "0:0-2:0", DisplayName = "blank lines after the code")]
    [DataRow("\r\n\r\n", "0:0-2:0", DisplayName = "blank lines alone")]
    [DataRow("", "0:0-0:0", DisplayName = "no text")]
    public void TheModule_SpansItsWholeText(string source, string expected)
    {
        var range = new ModuleParser().Parse(Module, source).SyntaxTree!.SourceLocation.Range;

        Assert.AreEqual(expected, $"{range.Start.Line}:{range.Start.Character}-{range.End.Line}:{range.End.Character}");
    }

    [TestMethod]
    public void TheKeywordsOfAParameter_AreEachAtTheirOwnRange()
    {
        const string source = "Public Sub Main(Optional  ByVal n As Long = 1, ByRef s As String, ParamArray rest() As Variant)\r\nEnd Sub\r\n";

        var parameters = MemberOf(source).Children.OfType<ParameterDeclarationNode>().ToArray();

        Assert.AreEqual("Optional", TextOf(source, parameters[0].OptionalRange));
        Assert.AreEqual("ByVal", TextOf(source, parameters[0].PassingRange));
        Assert.IsNull(parameters[0].ParamArrayRange);
        Assert.IsNull(parameters[1].OptionalRange);
        Assert.AreEqual("ByRef", TextOf(source, parameters[1].PassingRange));
        Assert.AreEqual("ParamArray", TextOf(source, parameters[2].ParamArrayRange));
        Assert.IsNull(parameters[2].PassingRange);
    }

    [TestMethod]
    [DataRow("Public Sub Main(ByVal n As Long)\r\nEnd Sub\r\n", "(ByVal n As Long)", "End Sub", DisplayName = "a Sub")]
    [DataRow("Public Function F$(a)\r\nEnd Function\r\n", "(a)", "End Function", DisplayName = "a Function with a type character")]
    [DataRow("Public Property Get P() As Long\r\nEnd Property\r\n", "()", "End Property", DisplayName = "a Property Get")]
    [DataRow("Public Property Let P(ByVal value As Long)\r\nEnd Property\r\n", "(ByVal value As Long)", "End Property", DisplayName = "a Property Let")]
    [DataRow("Public Property Set P(ByVal value As Object)\r\nEnd Property\r\n", "(ByVal value As Object)", "End Property", DisplayName = "a Property Set")]
    [DataRow("Sub Main\r\nEnd Sub\r\n", null, "End Sub", DisplayName = "a Sub with no parameter list")]
    [DataRow("Sub Main()\r\nEnd _\r\nSub\r\n", "()", "End _\r\nSub", DisplayName = "an End Sub across a line continuation")]
    public void AProcedure_HasTheRangesOfItsParameterListAndItsEnd(string source, string? parameterList, string end)
    {
        var member = MemberOf(source);

        Assert.AreEqual(parameterList, TextOf(source, member.ParameterListRange));
        Assert.AreEqual(end, TextOf(source, member.EndRange));
    }

    [TestMethod]
    [DataRow("Option Explicit\r\nPrivate n As Long\r\n\r\n' about Main\r\nPublic Sub Main()\r\nEnd Sub\r\n", 2, DisplayName = "a blank line and a comment above the procedure")]
    [DataRow("Option Explicit\r\nPrivate n As Long ' a field\r\nPublic Sub Main()\r\nEnd Sub\r\n", 2, DisplayName = "the procedure directly after a field")]
    [DataRow("Option Explicit\r\n\r\n\r\nPublic Sub Main()\r\n    Dim a As Long\r\nEnd Sub\r\n", 1, DisplayName = "blank lines and no field")]
    [DataRow("Option Explicit\r\nPrivate Type T\r\n    a As Long\r\n\r\n    b As Long\r\nEnd Type\r\n\r\nSub Main()\r\nEnd Sub\r\n", 6, DisplayName = "a Type with a blank line in it")]
    public void TheDeclarations_EndAfterTheLastLineWrittenAboveTheFirstProcedure(string source, int line)
        => Assert.AreEqual(new SourcePosition(line, 0), LineEdits.EndOfDeclarations(new ModuleParser().Parse(Module, source)));

    [TestMethod]
    public void TheDeclarations_InABranchOfAnIfDirective_HaveNoEnd()
    {
        const string source = "Option Explicit\r\n#If VBA7 Then\r\nPrivate n As LongPtr\r\nSub Main()\r\nEnd Sub\r\n#End If\r\n";

        Assert.IsNull(LineEdits.EndOfDeclarations(new ModuleParser().Parse(Module, source)));
    }

    [TestMethod]
    [DataRow("Sub Main()\r\n    Stop\r\n    Beep\r\nEnd Sub\r\n", "Stop", "1:0-2:0", DisplayName = "a statement alone on its line")]
    [DataRow("Sub Main()\r\n    Stop ' here\r\n    Beep\r\nEnd Sub\r\n", "Stop", null, DisplayName = "a statement before a comment")]
    [DataRow("Sub Main()\r\n    Beep: Stop\r\nEnd Sub\r\n", "Stop", null, DisplayName = "a statement after a colon")]
    [DataRow("Sub Main()\r\n    If True Then Stop\r\nEnd Sub\r\n", "Stop", null, DisplayName = "a statement of a single-line If")]
    public void TheWholeLinesOfAStatement_AreItsLinesWhenNothingElseIsOnThem(string source, string keyword, string? expected)
    {
        var parse = new ModuleParser().Parse(Module, source);
        var statement = parse.SyntaxTree!.Descendants().OfType<SDK.Model.AST.Statements.KeywordStatementNode>().Single(node => node.Token == keyword);

        var lines = LineEdits.WholeLinesOf(parse, statement);

        Assert.AreEqual(expected, lines is { } range ? $"{range.Start.Line}:{range.Start.Character}-{range.End.Line}:{range.End.Character}" : null);
    }

    [TestMethod]
    [DataRow("Sub Main()\r\nRetry:\r\n    Beep\r\nEnd Sub\r\n", "Retry", "Retry", DisplayName = "a label alone")]
    [DataRow("Sub Main()\r\nRetry:    Beep\r\nEnd Sub\r\n", "Retry", "Retry", DisplayName = "a label before a statement")]
    [DataRow("Sub Main()\r\n10 Retry: Beep\r\nEnd Sub\r\n", "Retry", "Retry", DisplayName = "a label after a line number")]
    [DataRow("Sub Main()\r\n[Try Again]:\r\n    Beep\r\nEnd Sub\r\n", "Try Again", "[Try Again]", DisplayName = "a bracketed label, with its brackets")]
    [DataRow("Sub Main()\r\n10 Beep\r\nEnd Sub\r\n", "10", null, DisplayName = "a line number has no name")]
    public void TheNameOfALabel_IsWithoutItsColon(string source, string label, string? expected)
    {
        var parse = new ModuleParser().Parse(Module, source);
        Assert.IsEmpty(parse.SyntaxErrors);
        var node = parse.SyntaxTree!.Descendants().OfType<SDK.Model.AST.Abstract.LineLabelNode>().Single(node => node.Name.Trim('[', ']') == label);

        Assert.AreEqual(expected, TextOf(source, node.NameRange));
    }

    [TestMethod]
    public void TheEndOfAProcedure_IsBeforeItsAttributes()
    {
        const string source = "Public Function F() As Long\r\nEnd Function\r\nAttribute F.VB_Description = \"what F does\"\r\n";

        var member = MemberOf(source);

        Assert.AreEqual(new SourceRange(1, 0, 1, 12), member.EndRange);
    }
}
