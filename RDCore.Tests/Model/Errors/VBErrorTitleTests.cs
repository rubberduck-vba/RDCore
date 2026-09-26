using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Model.Source;

namespace RDCore.Tests.Model.Errors;

/// <summary>
/// An error's <em>title</em>: which of <strong>RD-VBAL §2.6</strong>'s four families raised it, as a
/// reader sees it above the message. The title says what kind of thing went wrong — "Run-time error" —
/// and the error's own description says what it was: "Division by zero".
/// </summary>
[TestClass]
public sealed class VBErrorTitleTests
{
    private static readonly SourceLocation Location = new(new Uri("file:///c:/ws/Mod1.bas"), SourceRange.Empty);

    [TestMethod]
    public void AnErrorTheRuntimeSemanticsReport_IsARunTimeError()
    {
        var error = VBRuntimeErrorInfo.For(VBRuntimeErrorId.DivisionByZero, Location, "verbose");

        Assert.AreEqual("Run-time error", error.ToDiagnosticTitle());
        Assert.AreEqual("Division by zero", error.Description, "the title is the category; the description is the error");
    }

    [TestMethod]
    public void AnErrorTheWorkspaceRaised_IsAnApplicationError()
    {
        var error = VBApplicationErrorInfo.Raised(513, Location, "verbose");

        Assert.AreEqual("Application error", error.ToDiagnosticTitle());
    }

    [TestMethod]
    public void AParserError_IsASyntaxError()
    {
        // VBC is one family of codes for two categories, so nothing but the id tells them apart:
        // VBCompileErrorId reserves [9300..] for the semantic ones, and everything below it is the parser's.
        var error = VBCompileErrorInfo.For(VBCompileErrorId.SyntaxError, Location, "verbose");

        Assert.AreEqual("Syntax error", error.ToDiagnosticTitle());
    }

    [TestMethod]
    public void ASemanticError_IsACompileError()
    {
        var error = VBCompileErrorInfo.For(VBCompileErrorId.LabelNotDefined, Location, "verbose");

        Assert.AreEqual("Compile error", error.ToDiagnosticTitle());
        Assert.IsGreaterThanOrEqualTo(9300, error.ErrorId, "the boundary this is decided by");
    }

    [TestMethod]
    public void ATitle_IsFoundThroughACarrierDeclaredMoreGenerally()
    {
        // the reason it switches on the runtime type rather than being one member per error type: an error
        // travels the interpreter in carriers declared as IVBRaisableError, and would otherwise answer for
        // whichever family the carrier's own static type named.
        IVBRaisableError raised = VBApplicationErrorInfo.Raised(513, Location, "verbose");

        Assert.AreEqual("Application error", raised.AsErrorInfo.ToDiagnosticTitle());
    }
}
