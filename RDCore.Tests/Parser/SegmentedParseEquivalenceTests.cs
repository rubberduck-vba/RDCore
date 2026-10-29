using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;

namespace RDCore.Tests.Parser;

/// <summary>
/// A module parsed a segment at a time - its declarations section, then each member on its own - is the module a parse of the whole of it gives: its tree,
/// with every node's identity and location, its trivia, its directive trivia and its errors. The segments only change how long it takes.
/// </summary>
[TestClass]
public sealed class SegmentedParseEquivalenceTests
{
    private static readonly Uri Module = new("file:///c:/ws/Module1.bas");

    private static string Lines(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    // the result as text, with everything a node says of where it is: two results are the same when their texts are.
    private static string[] Written(ModuleParseResult result) =>
    [
        System.Text.Json.JsonSerializer.Serialize<SyntaxNode?>(result.SyntaxTree, SyntaxNodeJson.Options),
        System.Text.Json.JsonSerializer.Serialize(result.Trivia, SyntaxNodeJson.Options),
        System.Text.Json.JsonSerializer.Serialize(result.PrecompilerTrivia, SyntaxNodeJson.Options),
        System.Text.Json.JsonSerializer.Serialize(result.SyntaxErrors),
    ];

    // the module parsed whole; as the parser parses it; and a segment at a time without its fast parse as a whole tried first, which takes the segmented
    // path wherever it can - and which, said by stitches, it does or doesn't. All three are the same.
    private static bool AssertTheSameAsAWholeParse(string source, SourcePosition anchor = default, bool? stitches = true)
    {
        var expected = Written(new ModuleParser { Segmented = false }.Parse(Module, source, anchor));
        var forced = new ModuleParser { ForceSegments = true };
        string[] parts = ["the tree", "the trivia", "the directive trivia", "the errors"];
        foreach (var (how, written) in new[] { ("as the parser parses it", Written(new ModuleParser().Parse(Module, source, anchor))),
            ("a segment at a time", Written(forced.Parse(Module, source, anchor))) })
        {
            for (var part = 0; part < parts.Length; part++)
            {
                Assert.AreEqual(expected[part], written[part], $"{parts[part]} of a parse {how} differs from a whole parse's");
            }
        }

        var stitched = forced.Statistics[SegmentationStatistics.Path.Stitched] == 1;
        if (stitches is { } expectedToStitch)
        {
            Assert.AreEqual(expectedToStitch, stitched, "the module parsed a segment at a time");
        }

        return stitched;
    }

    private static readonly string Declared = Lines(
        "Attribute VB_Name = \"Module1\"",
        "Option Explicit",
        "' a comment of the declarations",
        "Private Declare PtrSafe Function GetTickCount Lib \"kernel32\" () As Long",
        "Private Const N As Long = 3",
        "Public Type T",
        "    X As Long",
        "End Type",
        "Public Enum E",
        "    E1 = 1",
        "End Enum",
        "Private mValue As Long",
        "",
        "'@Description(\"the first\")",
        "Public Sub First(ByVal a As Long, _",
        "                 Optional ByVal b As Long = 2)",
        "    Dim i As Long",
        "    For i = 1 To N",
        "        mValue = mValue + a * b ' a trailing comment",
        "    Next",
        "    If a > 0 Then Debug.Print \"positive\" Else Debug.Print \"not\"",
        "End Sub",
        "",
        "' between the members",
        "Private Function Second() As String",
        "    Second = \"End Sub\" & \"Sub x\"",
        "End Function",
        "Public Property Get Value() As Long",
        "    Value = mValue",
        "End Property",
        "Public Property Let Value(ByVal v As Long)",
        "    mValue = v",
        "End Property",
        "' after the last member");

    [TestMethod]
    public void AModuleOfDeclarationsAndMembers_IsSegmented_AndParsesAsAWhole()
    {
        Assert.IsNotNull(ModuleSegmentScanner.Scan(Declared.Replace("\r\n", "\n")), "the module is segmented");
        AssertTheSameAsAWholeParse(Declared);
    }

    [TestMethod]
    public void AModuleAnchoredInALargerDocument_IsSegmented_AndParsesAsAWhole()
        => AssertTheSameAsAWholeParse(Declared, new SourcePosition(12, 0));

    [TestMethod]
    public void AModuleWithDirectives_IsSegmented_AndParsesAsAWhole()
    {
        var source = Lines(
            "Option Explicit",
            "#Const DEBUGGING = 1",
            "#If VBA7 Then",
            "Private Declare PtrSafe Function GetTickCount Lib \"kernel32\" () As Long",
            "#Else",
            "Private Declare Function GetTickCount Lib \"kernel32\" () As Long",
            "#End If",
            "Public Sub Main()",
            "#If DEBUGGING Then",
            "    Debug.Print \"debugging\"",
            "#End If",
            "    Debug.Print GetTickCount",
            "End Sub",
            "Public Sub Other()",
            "    Main",
            "End Sub");

        // the scanner reads the module as the main parse does, its directives' lines blank.
        Assert.IsNotNull(ModuleSegmentScanner.Scan(string.Join("\n", source.Replace("\r\n", "\n").Split('\n').Select(line => line.StartsWith('#') ? "" : line))));
        AssertTheSameAsAWholeParse(source);
    }

    [TestMethod]
    public void AClassModuleWithItsHeader_IsSegmented_AndParsesAsAWhole()
        => AssertTheSameAsAWholeParse(Lines(
            "VERSION 1.0 CLASS",
            "BEGIN",
            "  MultiUse = -1  'True",
            "END",
            "Attribute VB_Name = \"Class1\"",
            "Attribute VB_PredeclaredId = False",
            "Option Explicit",
            "Public Event Changed()",
            "Private Sub Class_Initialize()",
            "    RaiseEvent Changed",
            "End Sub",
            "Public Function Item(ByVal i As Long) As Variant",
            "Attribute Item.VB_UserMemId = 0",
            "    Item = i",
            "End Function"));

    // a header written once in each branch of a #If block: the main pass reads one, and the others are its alternatives, read on their own.
    [TestMethod]
    public void AModuleWithASplitHeader_IsSegmented_AndParsesAsAWhole()
        => AssertTheSameAsAWholeParse(Lines(
            "Option Explicit",
            "Private Const N As Long = 3",
            "#If VBA7 Then",
            "Private Function Unsigned(ByVal a As LongPtr, ByVal b As LongPtr) As LongPtr",
            "#Else",
            "Private Function Unsigned(ByVal a As Long, ByVal b As Long) As Long",
            "#End If",
            "    Unsigned = a + b",
            "End Function",
            "Public Sub Main()",
            "    Debug.Print Unsigned(1, N)",
            "End Sub"));

    // what the scanner can't segment is parsed whole, as before.
    [TestMethod]
    [DataRow("Public Sub A()|    x = 1|Public Sub B()|End Sub", DisplayName = "a header in a member")]
    [DataRow("Public Sub A()|End Sub|End Sub", DisplayName = "an End outside a member")]
    [DataRow("Public Sub A()|End Sub|x = 1", DisplayName = "code after the last End")]
    [DataRow("Public Sub A()|    x = 1", DisplayName = "a member with no End")]
    [DataRow("Option Explicit|Private x As Long", DisplayName = "no member")]
    [DataRow("Public Sub A(): End Sub", DisplayName = "an End that is not the first statement of its line")]
    public void AModuleTheScannerCantSegment_IsParsedWhole(string lines)
    {
        var source = Lines(lines.Split('|'));

        Assert.IsNull(ModuleSegmentScanner.Scan(source.Replace("\r\n", "\n")));
        _ = AssertTheSameAsAWholeParse(source, stitches: false);
    }

    // a segment with a syntax error is a whole module's to report: the module is parsed whole.
    [TestMethod]
    [DataRow("Public Sub A()|    If x Then|End Sub|Public Sub B()|End Sub", DisplayName = "a block not closed")]
    [DataRow("Public Sub A()|    x = = 1|End Sub|Public Sub B()|End Sub", DisplayName = "a statement the grammar refuses")]
    [DataRow("Private x As|Public Sub A()|End Sub", DisplayName = "a declaration the grammar refuses")]
    public void AModuleWithASyntaxError_ParsesAsAWhole(string lines)
        => _ = AssertTheSameAsAWholeParse(Lines(lines.Split('|')), stitches: false);

    // the modules of the repository's benchmark corpus.
    [TestMethod]
    public void EveryModuleOfTheCorpus_ParsesAsAWhole()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "RDCore.slnx")))
        {
            root = root.Parent;
        }

        Assert.IsNotNull(root, "the repository's root");
        var modules = Directory.GetFiles(Path.Combine(root.FullName, "RDCore.CLI", "Bench", "Corpus"));
        Assert.IsNotEmpty(modules);

        // a module of one member, or of none, is one segment, and a module with a syntax error is parsed whole: whichever way it is read, it is a whole
        // parse's. Most of the corpus is stitched.
        var stitched = modules.Count(module => AssertTheSameAsAWholeParse(File.ReadAllText(module), stitches: null));
        Assert.IsGreaterThan(modules.Length / 2, stitched, "most of the corpus is stitched");
    }

    // a parenthesized argument of a call statement with no space before it - `Foo(5)` - is a grouping, whose location is read off its token, not off a rule
    // the tree is anchored at: in a member that is not the first, its segment starts further down the module than the module does.
    [TestMethod]
    [DataRow(0, 0, DisplayName = "the module at the document's start")]
    [DataRow(4, 2, DisplayName = "the module anchored in a larger document")]
    public void AGroupedArgumentInALaterMember_IsWhereAWholeParseHasIt(int line, int character)
        => AssertTheSameAsAWholeParse(Lines(
            "Option Explicit",
            "Public Sub A()",
            "End Sub",
            "Public Sub B()",
            "    Debug.Print 1",
            "    Foo(5)",
            "End Sub",
            "Public Sub Foo(ByVal x As Long)",
            "End Sub"), new SourcePosition(line, character));

    // the arguments of a call statement in parentheses, with no Call - `Foo(1, 2)` - are a syntax error the listener reports from the parenthesis' token: in a
    // module anchored in a larger document, it is where the document has it.
    [TestMethod]
    public void ArgumentsInParenthesesWithoutCall_AreReportedWhereTheDocumentHasThem()
    {
        var source = Lines(
            "Option Explicit",
            "Public Sub A()",
            "End Sub",
            "Public Sub B()",
            "    Debug.Print 1",
            "    Foo(1, 2)",
            "End Sub",
            "Public Sub Foo(ByVal x As Long, ByVal y As Long)",
            "End Sub");
        var anchor = new SourcePosition(4, 2);

        var atTheStart = Assert.ContainsSingle(new ModuleParser().Parse(Module, source).SyntaxErrors).Location.Range;
        var anchored = Assert.ContainsSingle(new ModuleParser().Parse(Module, source, anchor).SyntaxErrors).Location.Range;

        Assert.AreEqual(new SourceRange(atTheStart.Start.AnchoredAt(anchor), atTheStart.End.AnchoredAt(anchor)), anchored);
        _ = AssertTheSameAsAWholeParse(source, anchor, stitches: false);
    }

    // a block If around a one-line If whose Then a colon follows: the fast prediction can't read it, and the member that holds it is read slowly on its own.
    private static readonly string[] SlowMember =
    [
        "Public Sub Slow(ByVal a As Boolean, ByVal b As Boolean)",
        "    Dim c As Long",
        "    If a Then",
        "        If b Then: c = -c",
        "    End If",
        "End Sub",
    ];

    // a module the fast prediction can't read as a whole, with a header written once in each branch of a #If block: as the parser parses it - not forced -
    // it is stitched, and each of its modules is counted once.
    [TestMethod]
    public void AModuleTheFastPredictionCantRead_WithASplitHeader_IsStitched()
    {
        var source = Lines([
            "Option Explicit",
            "#If VBA7 Then",
            "Private Function Unsigned(ByVal a As LongPtr) As LongPtr",
            "#Else",
            "Private Function Unsigned(ByVal a As Long) As Long",
            "#End If",
            "    Unsigned = a",
            "End Function",
            .. SlowMember,
            "Public Sub Main()",
            "    Debug.Print Unsigned(1)",
            "End Sub"]);
        var parser = new ModuleParser();

        _ = parser.Parse(Module, source);

        Assert.AreEqual(1L, parser.Statistics[SegmentationStatistics.Path.Stitched], "stitched as the parser parses it");
        Assert.AreEqual(1L, Enum.GetValues<SegmentationStatistics.Path>().Sum(path => parser.Statistics[path]), "the module counted once, its header's branch not");
        _ = AssertTheSameAsAWholeParse(source);
    }

    // an error the listener reports - not the grammar's, which would have the module parsed whole - in a member the fast prediction can't read, which is read
    // slowly on its own: the stitched module reports it once, where a whole parse does.
    [TestMethod]
    public void AnErrorTheListenerReports_InAMemberReadSlowly_IsReportedOnce()
    {
        var source = Lines([
            "Public Sub A()",
            "End Sub",
            "Public Sub Overflowing(ByVal a As Boolean, ByVal b As Boolean)",
            "    Const K = &H1FFFFFFFF",
            "    Dim c As Variant",
            "    If a Then",
            "        If b Then: c = -c",
            "    End If",
            "End Sub",
            .. SlowMember]);
        var parser = new ModuleParser();

        var errors = parser.Parse(Module, source).SyntaxErrors;

        Assert.AreEqual(1L, parser.Statistics[SegmentationStatistics.Path.Stitched], "stitched as the parser parses it");
        _ = Assert.ContainsSingle(errors, string.Join("; ", errors.Select(error => error.Verbose)));
        _ = AssertTheSameAsAWholeParse(source);
    }

    // a module read without segments is counted, as one that is read whole.
    [TestMethod]
    public void AModuleReadWithoutSegments_IsCounted()
    {
        var parser = new ModuleParser { Segmented = false };

        _ = parser.Parse(Module, Lines(SlowMember));

        Assert.AreEqual(1L, parser.Statistics[SegmentationStatistics.Path.NotSegmented]);
    }
}
