using RDCore.LanguageServer.Services.Refactoring.ExtractMethod;

namespace RDCore.Tests.Semantics.Refactoring;

/// <summary>
/// An extraction is checked before it is made, on the module it would leave: it parses, every name binds, it has no compile error the module did not have, and the
/// call does what the selection did. An extraction that fails is refused, and the failure says why.
/// </summary>
[TestClass]
public sealed class ExtractionVerificationTests
{
    [TestMethod]
    [DataRow("Dim x As Long\nDo While x < 10\nx = x + 1\nLoop\nDebug.Print x", 2, 2, null, DisplayName = "a variable read and written")]
    [DataRow("Dim y As Long\ny = n * 2\nDebug.Print y", 1, 1, "y", DisplayName = "a return value")]
    [DataRow("Dim a As Long, b As Long\nb = n * 2\na = b + 1\nDebug.Print a", 1, 2, null, DisplayName = "a declaration that moves")]
    [DataRow("Dim i As Long\nFor i = 1 To 5\nIf i = n Then Exit Sub\nDebug.Print i\nNext", 2, 3, null, DisplayName = "an exit")]
    [DataRow("Dim i As Long\nFor i = 1 To 9\nIf i = n Then Exit For\nIf i = 4 Then Exit Sub\nDebug.Print i\nNext\nDebug.Print \"after\"", 2, 4, null, DisplayName = "several exits")]
    [DataRow("Dim i As Long\nFor i = 1 To 5\nIf i = n Then GoTo Done\nDebug.Print i\nNext\nDone:\nDebug.Print \"done\"", 2, 3, null, DisplayName = "a GoTo out of the selection")]
    [DataRow("Dim p As Point\nWith p\n.X = n\n.Y = .X + 1\nEnd With\nDebug.Print p.X", 2, 3, null, DisplayName = "a With target")]
    [DataRow("Static total As Long\ntotal = total + n\nDebug.Print total", 1, 1, null, DisplayName = "a Static local")]
    [DataRow("Dim x As Long\n#If True Then\nx = n\n#Else\nx = -n\n#End If\nDebug.Print x", 1, 5, null, DisplayName = "an #If block")]
    [DataRow("Dim x As Long\nOn Error Resume Next\nx = 1 / 0\nDebug.Print \"next\"", 2, 3, null, DisplayName = "On Error Resume Next")]
    public void AnExtractionAsItIsWritten_IsVerified(string body, int first, int last, string? returnValue)
    {
        var failure = ExtractionFixture.Verified(body, first, last, returnValue);

        Assert.IsNull(failure, $"{failure}");
    }

    [TestMethod]
    public void TheSourceFunctionsResult_IsVerified()
        => Assert.IsNull(ExtractionFixture.Verified("Run = n * 2\nRun = Run + 1", 0, 1, header: "Public Function Run(ByVal n As Long) As Long", end: "End Function"));

    [TestMethod]
    public void AModuleThatDoesNotParse_IsRefused()
    {
        var failure = ExtractionFixture.Verified("Dim x As Long\nx = n\nDebug.Print x", 1, 1,
            tamper: edit => ExtractionFixture.Replacing(edit, ExtractionTextEditKind.Procedure, "End Sub", "End Function"));

        Assert.AreEqual(ExtractionVerificationFailureKind.DoesNotParse, failure?.Kind);
    }

    // with no Option Explicit, a name the new procedure does not declare declares a variable of its own: it is not what the selection named.
    [TestMethod]
    public void ANameThatBindsToNothing_IsRefused_NamingIt()
    {
        var failure = ExtractionFixture.Verified("Dim x As Long\nx = n\nDebug.Print x", 1, 1, optionExplicit: false,
            tamper: edit => ExtractionFixture.Replacing(edit, ExtractionTextEditKind.Procedure, "x = n", "x = n + y"));

        Assert.AreEqual(new ExtractionVerificationFailure(ExtractionVerificationFailureKind.NameDoesNotBind, "y"), failure);
    }

    [TestMethod]
    public void ANewCompileError_IsRefused()
    {
        var failure = ExtractionFixture.Verified("Dim x As Long\nx = n\nDebug.Print x", 1, 1,
            tamper: edit => ExtractionFixture.Replacing(edit, ExtractionTextEditKind.Procedure, "x = n", "x = n + y"));

        Assert.AreEqual(ExtractionVerificationFailureKind.NewCompileError, failure?.Kind);
    }

    // x passed ByVal: what the new procedure writes to it is lost, and the loop never ends.
    [TestMethod]
    public void AVariableTheCallNoLongerGivesBack_IsRefused_NamingIt()
    {
        var failure = ExtractionFixture.Verified("Dim x As Long\nDo While x < 10\nx = x + 1\nLoop", 2, 2,
            tamper: edit => ExtractionFixture.Replacing(edit, ExtractionTextEditKind.Procedure, "ByRef x", "ByVal x"));

        Assert.AreEqual(new ExtractionVerificationFailure(ExtractionVerificationFailureKind.DataFlowDiffers, "x"), failure);
    }

    [TestMethod]
    public void AnExitTheCallDoesNotTake_IsRefused()
    {
        var failure = ExtractionFixture.Verified("Dim i As Long\nFor i = 1 To 5\nIf i = n Then Exit Sub\nDebug.Print i\nNext", 2, 3,
            tamper: edit => ExtractionFixture.Replacing(edit, ExtractionTextEditKind.Call, "Exit Sub", "Debug.Print i"));

        Assert.AreEqual(ExtractionVerificationFailureKind.ExitNotReached, failure?.Kind);
    }

    // x is read after the call, passed ByRef to a parameter the new procedure only reads: the call gives it no value, and nothing comes back of it.
    [TestMethod]
    public void AVariablePassedByReferenceToAParameterTheNewProcedureOnlyReads_IsNotGivenBackByTheCall()
        => Assert.IsNull(ExtractionFixture.Verified("Dim x As Long\nx = n\nDebug.Print x * 2\nDebug.Print x", 2, 2, parameters: [new("x", Passing: "ByRef")]));

    // the same parameter, written by the new procedure as the user changed it: what it writes comes back, which the selection did not give back.
    [TestMethod]
    public void AVariableTheNewProcedureWritesThroughAByRefParameter_IsGivenBackByTheCall_AndRefused()
    {
        var failure = ExtractionFixture.Verified("Dim x As Long\nx = n\nDebug.Print x * 2\nDebug.Print x", 2, 2, parameters: [new("x", Passing: "ByRef")],
            tamper: edit => ExtractionFixture.Replacing(edit, ExtractionTextEditKind.Procedure, "Debug.Print x * 2", "x = x * 2"));

        Assert.AreEqual(new ExtractionVerificationFailure(ExtractionVerificationFailureKind.DataFlowDiffers, "x"), failure);
    }
}
