using Microsoft.Extensions.Logging.Abstractions;
using RDCore.Diagnostics.Handlers;
using RDCore.Parsing;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// <see cref="DiagnoseDocumentHandler"/> is the inaugural diagnostics provider: it re-emits the
/// parser's located syntax errors as <see cref="PlatformDiagnostic"/>s over
/// <c>rdcore/diagnostics/document</c>.
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

    private static DiagnoseDocumentHandler NewHandler() => new(NullLogger<DiagnoseDocumentHandler>.Instance);

    private static DiagnoseDocumentRequest RequestFor(string source, int version = 1)
    {
        var uri = TestUri.TestModuleUri();
        var parseResult = new ModuleParser().Parse(uri, ModuleType.StdModule, source);
        return new DiagnoseDocumentRequest
        {
            Json = PlatformJson.Serialize(new DiagnoseDocumentPayload(uri, version, parseResult)),
        };
    }

    private static async Task<DiagnoseDocumentResult> HandleAsync(DiagnoseDocumentRequest request)
        => (await NewHandler().Handle(request, CancellationToken.None)).Unwrap<DiagnoseDocumentResult>();

    [TestMethod]
    public async Task SyntaxErrors_BecomePlatformDiagnostics()
    {
        var uri = TestUri.TestModuleUri();
        var parseResult = new ModuleParser().Parse(uri, ModuleType.StdModule, SplitConditionalModule);
        Assert.IsNotEmpty(parseResult.SyntaxErrors);

        var result = await HandleAsync(new DiagnoseDocumentRequest
        {
            Json = PlatformJson.Serialize(new DiagnoseDocumentPayload(uri, 7, parseResult)),
        });

        Assert.AreEqual(parseResult.SyntaxErrors.Length, result.Diagnostics.Length);
        Assert.AreEqual(7, result.SourceVersion, "the provider echoes the source version back");

        for (var i = 0; i < parseResult.SyntaxErrors.Length; i++)
        {
            var error = parseResult.SyntaxErrors[i];
            var diagnostic = result.Diagnostics[i];
            Assert.AreEqual(error.ErrorId, diagnostic.Code);
            Assert.AreEqual(error.Location, diagnostic.Location);
            Assert.AreEqual(error.Description, diagnostic.Message);
            Assert.AreEqual(error.Verbose, diagnostic.Verbose);
            Assert.AreEqual("RDCore.Diagnostics", diagnostic.Source);
            Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        }
    }

    [TestMethod]
    public async Task CleanParse_YieldsNoDiagnostics()
    {
        var result = await HandleAsync(RequestFor(CleanModule));

        Assert.IsEmpty(result.Diagnostics);
        Assert.AreEqual(1, result.SourceVersion);
    }
}
