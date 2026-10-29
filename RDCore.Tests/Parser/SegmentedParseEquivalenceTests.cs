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

        // a module of one member, or of none, is one segment: it is parsed whole whichever way, and every other is stitched.
        var stitched = modules.Count(module => AssertTheSameAsAWholeParse(File.ReadAllText(module), stitches: null));
        Assert.IsGreaterThan(modules.Length / 2, stitched, "most of the corpus is stitched");
    }
}
