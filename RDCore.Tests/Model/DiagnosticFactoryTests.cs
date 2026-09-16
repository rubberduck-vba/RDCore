using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Server.ProtocolExtensions;

namespace RDCore.Tests.Model;

/// <summary>
/// <see cref="DiagnosticFactory"/> is the one place a <c>VB*ErrorInfo</c> becomes an LSP
/// <see cref="Diagnostic"/>: a coded diagnostic whose <c>codeDescription</c> points the LSP client at
/// the per-code page on the published docs site.
/// </summary>
[TestClass]
public sealed class DiagnosticFactoryTests
{
    private static readonly SourceLocation Where =
        new(new Uri("file:///c:/ws/Mod1.bas"), new SourceRange(3, 4, 3, 12));

    private readonly DiagnosticFactory _factory = new();

    [TestMethod]
    public void FromVBSyntaxError_IsACodedErrorDiagnostic_WithADocsSiteHelpUrl()
    {
        var error = VBSyntaxErrorInfo.For(VBCompileErrorId.SyntaxError, Where, "unexpected token 'GetPtr'");

        var diagnostic = _factory.FromVBSyntaxError(error);

        Assert.AreEqual("VBC00001", diagnostic.Code!.Value.String);
        Assert.AreEqual(
            new Uri("https://rubberduck-vba.github.io/RDCore/diagnostics/vbc00001.html"),
            diagnostic.CodeDescription!.Href,
            "the LSP client opens codeDescription.href — it must resolve to a real docs-site page");
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.AreEqual("RDCore", diagnostic.Source);
        Assert.AreEqual(Where.Range.ToLsp(), diagnostic.Range);
        Assert.IsNotNull(diagnostic.Data, "the error id and verbose detail ride Data");
        // regression: VBCompileErrors had no entry for SyntaxError, so every grammar error (the
        // overwhelmingly common syntax-error path) fell back to "Unspecified error".
        Assert.AreEqual("Syntax error", diagnostic.Message);
    }
}
