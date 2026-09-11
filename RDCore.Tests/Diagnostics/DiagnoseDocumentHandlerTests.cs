using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Handlers;
using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.ProtocolExtensions;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <see cref="DiagnoseDocumentHandler"/> is the inaugural diagnostics provider: it projects the
/// parser's located syntax errors through <see cref="ICoreDiagnosticsFactory"/> and returns them as
/// LSP <see cref="Diagnostic"/>s over <c>rdcore/diagnostics/document</c>.
/// </summary>
[TestClass]
public sealed class DiagnoseDocumentHandlerTests
{
    // a #If that splits the function header — unparseable, so the parser yields located syntax errors.
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

    private static DiagnoseDocumentHandler NewHandler()
        => new(new DiagnosticFactory(), NullLogger<DiagnoseDocumentHandler>.Instance);

    private static DiagnoseDocumentRequest RequestFor(string source, out ModuleParseResult parseResult, int version = 1)
    {
        var uri = TestUri.TestModuleUri();
        parseResult = new ModuleParser().Parse(uri, source);
        return new DiagnoseDocumentRequest
        {
            Json = PlatformJson.Serialize(new DiagnoseDocumentPayload(uri, version, parseResult)),
        };
    }

    private static Task<DiagnoseDocumentResponse> HandleAsync(DiagnoseDocumentRequest request)
        => NewHandler().Handle(request, CancellationToken.None);

    [TestMethod]
    public async Task SyntaxErrors_AreProjectedToCodedLspDiagnostics()
    {
        var request = RequestFor(SplitConditionalModule, out var parseResult, version: 7);
        Assert.IsNotEmpty(parseResult.SyntaxErrors);

        var result = await HandleAsync(request);
        var diagnostics = result.Diagnostics.ToArray();

        Assert.AreEqual(parseResult.SyntaxErrors.Length, diagnostics.Length);
        Assert.AreEqual(7, result.SourceVersion, "the provider echoes the source version back");

        for (var i = 0; i < parseResult.SyntaxErrors.Length; i++)
        {
            var error = parseResult.SyntaxErrors[i];
            var diagnostic = diagnostics[i];

            Assert.AreEqual(error.ToDiagnosticCode(), diagnostic.Code!.Value.String);
            StringAssert.StartsWith(diagnostic.Code!.Value.String, "VBC");
            var href = diagnostic.CodeDescription!.Href.ToString();
            Assert.AreEqual(
                $"https://rubberduck-vba.github.io/RDCore/diagnostics/{error.ToDiagnosticCode().ToLowerInvariant()}.html",
                href, "the LSP client opens this URL — it must resolve to a real docs-site page");
            Assert.AreEqual(error.Location.Range.ToLsp(), diagnostic.Range);
            Assert.AreEqual(error.Description, diagnostic.Message);
            Assert.AreEqual("RDCore", diagnostic.Source);
            Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.IsNotNull(diagnostic.Data, "the verbose token detail rides Data");
        }
    }

    [TestMethod]
    public async Task CleanParse_YieldsNoDiagnostics()
    {
        var result = await HandleAsync(RequestFor(CleanModule, out _));

        Assert.IsEmpty(result.Diagnostics);
        Assert.AreEqual(1, result.SourceVersion);
    }
}
