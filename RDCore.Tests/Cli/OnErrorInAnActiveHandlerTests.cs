using RDCore.SDK.Runtime.Shared;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.4.4</strong> An <c>On Error</c> statement run in an active error handler - an error an <c>On Error GoTo</c> caught, before a
/// <c>Resume</c>, an <c>Exit</c> or the end - sets the policy for after the handler, and nothing more: a second error in the handler goes to the caller,
/// and the error being handled can still be resumed. <c>On Error GoTo -1</c> ends the error being handled, and the label is the handler again.
/// </summary>
/// <remarks>
/// Each expected line is what MS-VBA printed for the same code, in Excel (64-bit, en-US, oracle\s53). <c>Inner</c>'s handler <c>H</c> runs each case's
/// statements after <c>Err.Raise 5</c>; its body goes on with <c>Err.Raise 7</c> if it is resumed. <c>Outer</c> calls it under a handler of its own.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.4.4 Error Handling Statements")]
public sealed class OnErrorInAnActiveHandlerTests
{
    private static string Program(string[] handler) => string.Join("\r\n",
        "Option Explicit",
        "Private acc As String",
        "Private hits As Long",
        "Private Sub P(ByVal x As Variant)", "    acc = acc & CStr(x) & \"|\"", "End Sub",
        "Private Sub Inner()",
        "    On Error GoTo H",
        "    Err.Raise 5",
        "    P \"resumed\"",
        "    Err.Raise 7",
        "    P \"body end\"",
        "    Exit Sub",
        "H:",
        "    hits = hits + 1",
        "    If hits > 1 Then P \"H again \" & Err.Number: Exit Sub",
        "    P \"H \" & Err.Number",
        "    " + string.Join("\r\n    ", handler),
        "    P \"H end\"",
        "    Exit Sub",
        "Other:",
        "    P \"Other \" & Err.Number",
        "End Sub",
        "Private Sub Outer()",
        "    On Error GoTo OH",
        "    Inner",
        "    P \"back\"",
        "    Exit Sub",
        "OH:",
        "    P \"outer \" & Err.Number",
        "End Sub",
        "Public Sub Main()",
        "    Outer",
        "    Debug.Print acc",
        "End Sub",
        string.Empty);

    private static async Task AssertPrinted(string expected, params string[] handler)
        => CollectionAssert.AreEqual(new[] { expected }, await ModuleWorkspace.RunAsync([], Program(handler)));

    // a second error in the handler goes to the caller, whatever On Error the handler ran.
    [TestMethod]
    [DataRow(new[] { "Err.Raise 6" }, "H 5|outer 6|", DisplayName = "A01 no On Error")]
    [DataRow(new[] { "Err.Raise 6", "P \"not here\"" }, "H 5|outer 6|", DisplayName = "D01 nothing after the second error runs")]
    [DataRow(new[] { "On Error GoTo Other", "Err.Raise 6" }, "H 5|outer 6|", DisplayName = "A02 On Error GoTo Other")]
    [DataRow(new[] { "On Error Resume Next", "Err.Raise 6", "P \"after \" & Err.Number" }, "H 5|outer 6|", DisplayName = "A03 On Error Resume Next")]
    [DataRow(new[] { "On Error GoTo 0", "Err.Raise 6" }, "H 5|outer 6|", DisplayName = "A04 On Error GoTo 0")]
    [DataRow(new[] { "Err.Clear", "On Error GoTo Other", "Err.Raise 6" }, "H 5|outer 6|", DisplayName = "A07 Err.Clear, then On Error GoTo Other")]
    [DataRow(new[] { "On Error GoTo Other", "Dim z As Long", "z = 1 \\ 0" }, "H 5|outer 11|", DisplayName = "A08 On Error GoTo Other, an error of the runtime")]
    [DataRow(new[] { "On Error Resume Next", "Dim z As Long", "z = 1 \\ 0", "P \"after \" & Err.Number" }, "H 5|outer 11|", DisplayName = "A09 On Error Resume Next, an error of the runtime")]
    [DataRow(new[] { "On Error GoTo Other", "Err.Raise 6", "P \"not here\"" }, "H 5|outer 6|", DisplayName = "D02 nothing after the second error runs")]
    public Task ASecondErrorInTheHandler_GoesToTheCaller(string[] handler, string expected)
        => AssertPrinted(expected, handler);

    // On Error GoTo -1 ends the error being handled: the label is the handler again, and another On Error takes effect at once.
    [TestMethod]
    [DataRow(new[] { "On Error GoTo -1", "Err.Raise 6" }, "H 5|H again 6|back|", DisplayName = "A05 the handler's label again")]
    [DataRow(new[] { "On Error GoTo -1", "On Error GoTo Other", "Err.Raise 6" }, "H 5|Other 6|back|", DisplayName = "A06 then On Error GoTo Other")]
    [DataRow(new[] { "On Error GoTo -1", "On Error Resume Next", "Err.Raise 6", "P \"after \" & Err.Number" }, "H 5|after 6|H end|back|", DisplayName = "A10 then On Error Resume Next")]
    [DataRow(new[] { "On Error GoTo -1", "Resume Next" }, "H 5|H again 20|back|", DisplayName = "C04 then Resume: nothing to resume (20)")]
    public Task OnErrorGoToMinusOne_EndsTheErrorBeingHandled(string[] handler, string expected)
        => AssertPrinted(expected, handler);

    // what an On Error in the handler says is the policy once a Resume ends the handler.
    [TestMethod]
    [DataRow(new[] { "On Error GoTo Other", "Resume Next" }, "H 5|resumed|Other 7|back|", DisplayName = "C01 On Error GoTo Other")]
    [DataRow(new[] { "On Error Resume Next", "Resume Next" }, "H 5|resumed|body end|back|", DisplayName = "C02 On Error Resume Next")]
    [DataRow(new[] { "On Error GoTo 0", "Resume Next" }, "H 5|resumed|outer 7|", DisplayName = "C03 On Error GoTo 0")]
    [DataRow(new[] { "Resume Next" }, "H 5|resumed|H again 7|back|", DisplayName = "C05 no On Error: the label")]
    public Task TheOnErrorOfTheHandler_IsThePolicyOnceItIsResumed(string[] handler, string expected)
        => AssertPrinted(expected, handler);

    // with no handler running there is no handling to end: a state that is not handling is its own state after it, whatever its AfterHandling defaults to.
    [TestMethod]
    public void HandlingOver_OfAStateThatIsNotHandling_IsThatState()
    {
        var resumeNext = new ErrorHandlerState(ErrorHandlingMode.ResumeNext, null, null, null);

        Assert.AreEqual(ErrorHandlerState.Disabled, ErrorHandlerState.Disabled.HandlingOver());
        Assert.AreEqual(resumeNext, resumeNext.HandlingOver());
    }

    // every On Error statement clears Err, in a handler too.
    [TestMethod]
    [DataRow("On Error GoTo Other", DisplayName = "B01 On Error GoTo Other")]
    [DataRow("On Error Resume Next", DisplayName = "B02 On Error Resume Next")]
    [DataRow("On Error GoTo 0", DisplayName = "B03 On Error GoTo 0")]
    [DataRow("On Error GoTo -1", DisplayName = "B04 On Error GoTo -1")]
    [DataRow("On Error GoTo H", DisplayName = "B05 On Error GoTo its own label")]
    public Task AnOnErrorInTheHandler_ClearsErr(string onError)
        => AssertPrinted("H 5|err 0|src |H end|back|", onError, "P \"err \" & Err.Number", "P \"src \" & Err.Source");
}
