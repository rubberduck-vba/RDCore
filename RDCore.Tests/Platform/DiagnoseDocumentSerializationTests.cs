using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Serialization;
using RDCore.Parsing;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Protocol;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.Platform;

/// <summary>
/// <see cref="DiagnoseDocumentPayload"/> carries a polymorphic <c>ModuleParseResult</c> to a provider
/// extension, so it rides a <see cref="PlatformJson"/> string; the
/// <see cref="DiagnoseDocumentResponse"/> comes back as plain LSP <see cref="Diagnostic"/>s the
/// transport's own (Newtonsoft) serializer round-trips.
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
    public void DiagnoseDocumentPayload_RoundTripsWithItsAstAndSyntaxErrors()
    {
        var uri = TestUri.TestModuleUri();
        var parseResult = new ModuleParser().Parse(uri, SplitConditionalModule);
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
        var parseResult = new ModuleParser().Parse(uri, CleanModule);
        Assert.IsTrue(parseResult.IsSuccess);

        var once = PlatformJson.Deserialize<DiagnoseDocumentPayload>(
            PlatformJson.Serialize(new DiagnoseDocumentPayload(uri, 1, parseResult)));

        Assert.IsTrue(once.ParseResult.IsSuccess);
        Assert.IsEmpty(once.ParseResult.SyntaxErrors);
        Assert.IsNotNull(once.ParseResult.SyntaxTree);
    }

    [TestMethod]
    public void DiagnoseDocumentResponse_RoundTripsThroughTheLspSerializer()
    {
        var original = new DiagnoseDocumentResponse
        {
            SourceVersion = 4,
            Diagnostics = new Container<Diagnostic>(
                new Diagnostic
                {
                    Code = new DiagnosticCode("VBC01027"),
                    CodeDescription = new CodeDescription { Href = new Uri("https://rubberduck-vba.github.io/RDCore/diagnostics/vbc01027.html") },
                    Severity = DiagnosticSeverity.Error,
                    Source = "RDCore",
                    Message = "Syntax error",
                    Range = new Range(new Position(3, 0), new Position(3, 5)),
                },
                new Diagnostic
                {
                    Code = new DiagnosticCode("VBC01027"),
                    Severity = DiagnosticSeverity.Error,
                    Source = "RDCore",
                    Message = "Syntax error",
                    Range = new Range(new Position(1, 0), new Position(1, 5)),
                }),
        };

        var json = LspSerializer.Instance.SerializeObject(original);
        var result = LspSerializer.Instance.DeserializeObject<DiagnoseDocumentResponse>(json);

        Assert.AreEqual(4, result.SourceVersion);
        Assert.AreEqual(2, result.Diagnostics.Count());

        var first = result.Diagnostics.First();
        Assert.AreEqual("VBC01027", first.Code!.Value.String);
        Assert.AreEqual("https://rubberduck-vba.github.io/RDCore/diagnostics/vbc01027.html", first.CodeDescription!.Href.ToString());
        Assert.AreEqual(DiagnosticSeverity.Error, first.Severity);
        Assert.AreEqual(new Range(new Position(3, 0), new Position(3, 5)), first.Range);
    }
}
