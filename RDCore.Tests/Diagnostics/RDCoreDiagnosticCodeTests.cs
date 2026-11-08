using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Tests.Diagnostics;

[TestClass]
public sealed class RDCoreDiagnosticCodeTests
{
    [TestMethod]
    [DataRow("XYZ", 42, "XYZ00042")]
    [DataRow("RDC", 0, "RDC00000")]
    [DataRow("ABC", 99999, "ABC99999")]
    public void ACode_IsWritten_AsItsPrefix_AndItsNumberInFiveDigits(string prefix, int number, string expected)
        => Assert.AreEqual(expected, new RDCoreDiagnosticCode(prefix, number).ToString());

    [TestMethod]
    [DataRow("XY")]
    [DataRow("WXYZ")]
    [DataRow("xyz")]
    [DataRow("X1Z")]
    [DataRow("ÀBC")]
    public void APrefixThatIsNotThreeLettersAToZ_IsRefused(string prefix)
        => Assert.ThrowsExactly<ArgumentException>(() => new RDCoreDiagnosticCode(prefix, 1));

    [TestMethod]
    [DataRow(-1)]
    [DataRow(100000)]
    public void ANumberOfMoreThanFiveDigits_IsRefused(int number)
        => Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RDCoreDiagnosticCode("XYZ", number));

    [TestMethod]
    public void ACodeWritten_IsReadBack()
    {
        Assert.IsTrue(RDCoreDiagnosticCode.TryParse("XYZ00042", out var code));
        Assert.AreEqual(new RDCoreDiagnosticCode("XYZ", 42), code);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("XYZ0042")]
    [DataRow("XYZ000042")]
    [DataRow("xyz00042")]
    [DataRow("XYZ0004A")]
    [DataRow("XYZ-0042")]
    public void TextThatIsNotACode_IsNotRead(string? text)
        => Assert.IsFalse(RDCoreDiagnosticCode.TryParse(text, out _));

    [TestMethod]
    public void ACoreDiagnostic_IsAnRdcCode_OfItsNumber()
    {
        var code = RDCoreDiagnosticId.EditNotApplied.ToCode();

        Assert.AreEqual(new RDCoreDiagnosticCode("RDC", 20001), code);
        Assert.AreEqual(RDCoreDiagnosticId.EditNotApplied.ToDiagnosticCode(), code.ToString());
        Assert.IsTrue(code.IsFirstParty);
    }

    [TestMethod]
    public void AFindingOfAnotherPublishersCode_HasNoCoreId_AndNoPageOfThePlatformsDocumentation()
    {
        var finding = new AnalyzerFinding(new RDCoreDiagnosticCode("XYZ", 42), default(SourceRange), DiagnosticSeverity.Warning, "message");

        var diagnostic = new DiagnosticFactory().FromAnalyzerFinding(finding);

        Assert.IsNull(finding.Id);
        Assert.AreEqual("XYZ00042", diagnostic.Code!.Value.String);
        Assert.IsNull(diagnostic.CodeDescription);
    }

    [TestMethod]
    public void AFindingOfACoreDiagnostic_IsItsId_AndLinksToItsPage()
    {
        var finding = new AnalyzerFinding(RDCoreDiagnosticId.EditNotApplied, default(SourceRange), DiagnosticSeverity.Warning, "message");

        var diagnostic = new DiagnosticFactory().FromAnalyzerFinding(finding);

        Assert.AreEqual(RDCoreDiagnosticId.EditNotApplied, finding.Id);
        Assert.AreEqual("RDC20001", diagnostic.Code!.Value.String);
        Assert.IsNotNull(diagnostic.CodeDescription);
    }
}
