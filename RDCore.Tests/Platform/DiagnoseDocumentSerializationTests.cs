using RDCore.Parsing;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.Platform;

/// <summary>
/// <see cref="DiagnoseDocumentPayload"/> carries a polymorphic <c>ModuleParseResult</c> across the
/// language-server → provider-extension boundary, so it rides a <see cref="PlatformJson"/> string
/// rather than the JSON-RPC transport's own serializer.
/// </summary>
[TestClass]
public sealed class DiagnoseDocumentSerializationTests
{
    // legal VBA a #If splits mid-statement: unparseable, so the two-stage parser yields an AST plus
    // located syntax errors — exactly the payload shape a provider has to round-trip.
    private const string SplitConditionalModule = """
        #If VBA7 Then
        Private Function GetPtr() As LongPtr
        #Else
        Private Function GetPtr() As Long
        #End If
            GetPtr = 0
        End Function
        """;

    private const string CleanModule = "Option Explicit\r\n\r\nPublic Sub DoNothing()\r\nEnd Sub\r\n";

    [TestMethod]
    public void PlatformDiagnostic_RoundTripsThroughPlatformJson()
    {
        var original = new PlatformDiagnostic(
            Code: 1027,
            Source: "RDCore.Diagnostics",
            Severity: DiagnosticSeverity.Error,
            Location: new SourceLocation(TestUri.TestModuleUri(), new SourceRange(3, 4, 3, 18)),
            Message: "Syntax error",
            Verbose: "unexpected token 'GetPtr'");

        var result = PlatformJson.Deserialize<PlatformDiagnostic>(PlatformJson.Serialize(original));

        Assert.AreEqual(original, result);
    }

    [TestMethod]
    public void PlatformDiagnostic_KeepsANullVerbose()
    {
        var original = new PlatformDiagnostic(
            101, "RDCore.Diagnostics", DiagnosticSeverity.Warning,
            new SourceLocation(TestUri.TestModuleUri(), SourceRange.Empty), "Implicit declaration", Verbose: null);

        var result = PlatformJson.Deserialize<PlatformDiagnostic>(PlatformJson.Serialize(original));

        Assert.IsNull(result.Verbose);
        Assert.AreEqual(original, result);
    }

    [TestMethod]
    public void DiagnoseDocumentPayload_RoundTripsWithItsAstAndSyntaxErrors()
    {
        var uri = TestUri.TestModuleUri();
        var parseResult = new ModuleParser().Parse(uri, ModuleType.StdModule, SplitConditionalModule);
        Assert.IsFalse(parseResult.IsSuccess, "the fixture must produce syntax errors");
        Assert.IsNotEmpty(parseResult.SyntaxErrors);

        var original = new DiagnoseDocumentPayload(uri, SourceVersion: 4, parseResult);

        var once = PlatformJson.Deserialize<DiagnoseDocumentPayload>(PlatformJson.Serialize(original));
        var twice = PlatformJson.Deserialize<DiagnoseDocumentPayload>(PlatformJson.Serialize(once));

        Assert.AreEqual(
            PlatformJson.Serialize(once), PlatformJson.Serialize(twice),
            "serialization is not stable across a round trip");

        Assert.AreEqual(uri, once.DocumentUri);
        Assert.AreEqual(4, once.SourceVersion);
        Assert.IsNotNull(once.ParseResult.SyntaxTree, "the polymorphic AST must survive the round trip");
        Assert.AreEqual(parseResult.SyntaxErrors.Length, once.ParseResult.SyntaxErrors.Length);
        Assert.AreEqual(parseResult.SyntaxErrors[0].ErrorId, once.ParseResult.SyntaxErrors[0].ErrorId);
        Assert.AreEqual(parseResult.SyntaxErrors[0].Location, once.ParseResult.SyntaxErrors[0].Location);
    }

    [TestMethod]
    public void DiagnoseDocumentPayload_RoundTripsACleanParse()
    {
        var uri = TestUri.TestModuleUri();
        var parseResult = new ModuleParser().Parse(uri, ModuleType.StdModule, CleanModule);
        Assert.IsTrue(parseResult.IsSuccess);

        var once = PlatformJson.Deserialize<DiagnoseDocumentPayload>(
            PlatformJson.Serialize(new DiagnoseDocumentPayload(uri, 1, parseResult)));

        Assert.IsTrue(once.ParseResult.IsSuccess);
        Assert.IsEmpty(once.ParseResult.SyntaxErrors);
        Assert.IsNotNull(once.ParseResult.SyntaxTree);
    }

    [TestMethod]
    public void DiagnoseDocumentResult_RoundTripsThroughPlatformJson()
    {
        var original = new DiagnoseDocumentResult(
            [
                new PlatformDiagnostic(1027, "RDCore.Diagnostics", DiagnosticSeverity.Error,
                    new SourceLocation(TestUri.TestModuleUri(), new SourceRange(1, 0, 1, 5)), "Syntax error", "detail"),
                new PlatformDiagnostic(1027, "RDCore.Diagnostics", DiagnosticSeverity.Error,
                    new SourceLocation(TestUri.TestModuleUri(), new SourceRange(3, 0, 3, 5)), "Syntax error", null),
            ],
            SourceVersion: 4);

        var result = PlatformJson.Deserialize<DiagnoseDocumentResult>(PlatformJson.Serialize(original));

        Assert.AreEqual(4, result.SourceVersion);
        Assert.AreEqual(2, result.Diagnostics.Length);
        CollectionAssert.AreEqual(original.Diagnostics, result.Diagnostics);
    }
}
