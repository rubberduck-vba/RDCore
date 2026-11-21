using RDCore.Parsing;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Model.Source;

namespace RDCore.Tests.Parser;

/// <summary>
/// review D2: <see cref="ModuleParser"/> normalizes the source before parsing. ANTLR's input stream
/// and the <c>#</c>-directive regexes (<c>RegexOptions.Multiline</c>) only recognize <c>\n</c>, so a
/// CR-only or U+2028-separated file used to parse as a single line, and a leading BOM shifted the
/// first token off column 0. All of these must now parse the same as their <c>\r\n</c> equivalent.
/// </summary>
[TestClass]
public sealed class SourceNormalizationTests
{
    private static readonly Uri Uri = TestUri.TestModuleUri();

    private static ModuleParseResult Parse(string source)
        => new ModuleParser().Parse(Uri, source);

    [TestMethod]
    [DataRow("\r", DisplayName = "CR only")]
    [DataRow("\u2028", DisplayName = "U+2028 line separator")]
    [DataRow("\u2029", DisplayName = "U+2029 paragraph separator")]
    public void NonLfLineSeparators_SplitLinesLikeCrLf(string separator)
    {
        var source = string.Join(separator, "Option Explicit", "Public Sub Foo()", "End Sub");

        var result = Parse(source);

        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);
        Assert.ContainsSingle(result.SyntaxTree!.Children.OfType<ModuleOptionDirectiveNode>());
        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        Assert.AreEqual("Foo", member.Name);
    }

    // a U+2028 or a U+2029 ends a physical line (MS-VBAL §3.2.1), but LSP 3.17 ends a line only at CR, LF or CRLF: what follows one is on the same
    // line a position counts.
    [TestMethod]
    [DataRow("\u2028", DisplayName = "U+2028 line separator")]
    [DataRow("\u2029", DisplayName = "U+2029 paragraph separator")]
    public void AStatementAfterAUnicodeSeparator_IsOnTheSameLspLine(string separator)
    {
        var result = Parse($"Option Explicit{separator}Public Sub Foo()\r\nEnd Sub");

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        Assert.AreEqual(new SourcePosition(0, "Option Explicit".Length + 1), member.SourceLocation.Range.Start);
    }

    // a directive starts a physical line (MS-VBAL §3.4), which a U+2028 or a U+2029 starts too.
    [TestMethod]
    [DataRow("\u2028", DisplayName = "U+2028 line separator")]
    [DataRow("\u2029", DisplayName = "U+2029 paragraph separator")]
    public void ADirectiveAfterAUnicodeSeparator_IsADirective_OnTheSameLspLine(string separator)
    {
        var result = Parse($"Option Explicit{separator}#Const A = 1\r\nPublic Sub Foo()\r\nEnd Sub");

        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);
        var directives = result.PrecompilerTrivia.SelectMany(node => node.Descendants().Prepend(node));
        var directive = Assert.ContainsSingle(directives.OfType<PrecompilerConstantDeclarationNode>());
        Assert.AreEqual(new SourcePosition(0, "Option Explicit".Length + 1), directive.SourceLocation.Range.Start);
    }

    // a line continuation ends a physical line (MS-VBAL §3.2.2), which a U+2028 or a U+2029 ends too: the statement goes on, on the same LSP line.
    [TestMethod]
    [DataRow("\u2028", DisplayName = "U+2028 line separator")]
    [DataRow("\u2029", DisplayName = "U+2029 paragraph separator")]
    public void ALineContinuationAtAUnicodeSeparator_ContinuesTheStatement_OnTheSameLspLine(string separator)
    {
        var line = $"    Debug.Print 1, _{separator}2";

        var result = Parse($"Public Sub Foo()\r\n{line}\r\nEnd Sub");

        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);
        var continuation = Assert.ContainsSingle(result.Trivia.OfType<LineContinuationTriviaNode>());
        var at = line.IndexOf(" _", StringComparison.Ordinal);
        var end = at + " _".Length + separator.Length;
        Assert.AreEqual(new SourceRange(new SourcePosition(1, at), new SourcePosition(1, end)), continuation.SourceLocation.Range);
    }

    // a segment is whole lines, which a U+2028 or a U+2029 does not end: a module that has one is read whole.
    [TestMethod]
    public void AModuleWithAUnicodeSeparator_IsNotReadASegmentAtATime()
    {
        var parser = new ModuleParser { ForceSegments = true };

        var result = parser.Parse(Uri, "Public Sub A()\r\nEnd Sub\u2028Public Sub B()\r\nEnd Sub");

        Assert.AreEqual(1L, parser.Statistics[SegmentationStatistics.Path.UnicodeLineSeparator]);
        CollectionAssert.AreEqual(new[] { "A", "B" }, result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Select(member => member.Name).ToArray());
    }

    // MS-VBA joins at most 25 physical lines into a logical line: the 25th line continuation in a row is the error, on the LSP line it is on.
    [TestMethod]
    public void TheLineContinuationPastTheLimit_IsReportedOnTheLspLineItIsOn()
    {
        const int pastTheLimit = 25;
        var line = "    x = 1" + string.Concat(Enumerable.Repeat(" _\u2028+ 1", pastTheLimit));
        var underscore = -1;
        for (var continuation = 0; continuation < pastTheLimit; continuation++)
        {
            underscore = line.IndexOf('_', underscore + 1);
        }

        var result = Parse($"Public Sub Foo()\r\n{line}\r\nEnd Sub");

        var error = Assert.ContainsSingle(result.SyntaxErrors.Where(error => error.Verbose == Exceptions.VBCompileError_TooManyLineContinuations_Verbose));
        Assert.AreEqual(new SourcePosition(1, underscore), error.Location.Range.Start);
    }

    [TestMethod]
    public void CrOnlyFile_KeepsPrecompilerTrivia()
    {
        var source = string.Join('\r', "#If VBA7 Then", "Public X As Long", "#End If");

        Assert.IsNotEmpty(Parse(source).PrecompilerTrivia);
    }

    [TestMethod]
    public void CrOnlyFile_LocatesASyntaxErrorOnItsRealLine()
    {
        var source = string.Join('\r', "Public Sub Foo()", "Dim x As", "End Sub");

        var result = Parse(source);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsTrue(
            result.SyntaxErrors.Any(error => error.Location.Range.Start.Line > 0),
            "a CR-only file must still yield line numbers, not collapse to line 0");
    }

    [TestMethod]
    public void LeadingByteOrderMark_DoesNotCorruptTheFirstToken()
    {
        var result = Parse("\uFEFF" + string.Join("\r\n", "Option Explicit", "Public Sub Foo()", "End Sub"));

        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);
        Assert.ContainsSingle(result.SyntaxTree!.Children.OfType<ModuleOptionDirectiveNode>());
        Assert.AreEqual("Foo", result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single().Name);
    }

    [TestMethod]
    public void ByteOrderMarkOnlyContent_IsAnEmptyModule()
    {
        var result = Parse("\uFEFF");

        Assert.IsTrue(result.IsSuccess);
        Assert.IsEmpty(result.SyntaxTree!.Children);
    }
}
