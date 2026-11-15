using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// MS-VBAL §5.4.2.21: a <c>With</c> block holds its object from its <c>With</c> statement to its <c>End With</c> - wherever the program jumps meanwhile, out
/// of the block and back in, to an error handler and back with <c>Resume Next</c> - and has none before its <c>With</c> statement set one, nor after its
/// <c>End With</c>: a member with no object before the dot is then error 91. It holds the object a variable held, not the variable. Every expected line was
/// observed in Excel.
/// </summary>
[TestClass]
public sealed class WithBlockReentryTests
{
    private static async Task<string> Run(string body)
    {
        var program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPrivate acc As String\r\n"
            + "Private Sub P(ByVal x As Variant)\r\n    acc = acc & CStr(x) & \"|\"\r\nEnd Sub\r\n"
            + "Public Sub Main()\r\n    On Error Resume Next\r\n    " + string.Join("\r\n    ", body.Split(" ;; ")) + "\r\n    Debug.Print acc\r\nEnd Sub\r\n";
        return (await RunAsync([], program)).Last();
    }

    [TestMethod]
    [DataRow("Dim c As New Collection ;; On Error GoTo H ;; With c ;; .Add 1 ;; Err.Raise 5 ;; .Add 2 ;; End With ;; P c.Count ;; GoTo Done ;; H: ;; P \"h\" & Err.Number ;; Resume Next ;; Done:",
        "h5|2|", DisplayName = "W01 resumed into the block from a handler")]
    [DataRow("On Error GoTo H ;; With New Collection ;; .Add 1 ;; Err.Raise 5 ;; .Add 2 ;; P .Count ;; End With ;; GoTo Done ;; H: ;; P \"h\" & Err.Number ;; Resume Next ;; Done:",
        "h5|2|", DisplayName = "W08 resumed into a block of an object nothing else holds")]
    [DataRow("Dim c As New Collection ;; With c ;; .Add 1 ;; GoTo Away ;; Back: ;; .Add 2 ;; End With ;; P c.Count & \"|\" & Err.Number ;; GoTo Done ;; Away: ;; GoTo Back ;; Done:",
        "2|0|", DisplayName = "W05 out of the block and back in")]
    public async Task AWithBlockJumpedOutOfAndBackInto_StillHoldsItsObject(string body, string printed)
        => Assert.AreEqual(printed, await Run(body));

    [TestMethod]
    [DataRow("Dim c As New Collection ;; GoTo Inside ;; With c ;; .Add 1 ;; Inside: ;; .Add 2 ;; P \"in\" & Err.Number ;; End With ;; P \"out\" & Err.Number ;; P c.Count",
        "in91|out91|0|", DisplayName = "W04 jumped into, past its With statement")]
    [DataRow("Dim o As Object ;; With o.Foo ;; P \"in\" ;; .Add 1 ;; P \"after\" & Err.Number ;; End With ;; P \"out\" & Err.Number",
        "in|after91|out91|", DisplayName = "W02 its With statement in error, resumed next")]
    [DataRow("Dim c As New Collection, i As Long ;; With c ;; Inside: ;; .Add i ;; End With ;; i = i + 1 ;; If i < 3 Then GoTo Inside ;; P c.Count & \"|\" & Err.Number",
        "1|91|", DisplayName = "W06 jumped back into, past its End With")]
    [DataRow("Dim i As Long ;; With New Collection ;; Inside: ;; .Add i ;; P .Count & \"|\" & Err.Number ;; End With ;; i = i + 1 ;; If i < 3 Then GoTo Inside ;; P \"end\" & Err.Number",
        "1|0|end91|", DisplayName = "W07 an object nothing else holds, past its End With")]
    public async Task AWithBlockWithNoObject_RaisesNinetyOneForAMember(string body, string printed)
        => Assert.AreEqual(printed, await Run(body));

    [TestMethod]
    [DataRow("Dim c As New Collection ;; On Error GoTo H ;; With c ;; .Add 1 ;; Err.Raise 5 ;; .Add 2 ;; End With ;; P c.Count ;; GoTo Done ;; H: ;; P \"h\" & Err.Number ;; Set c = New Collection ;; Resume Next ;; Done:",
        "h5|0|", DisplayName = "W09 the variable Set in a handler")]
    [DataRow("Dim c As New Collection, d As New Collection ;; With c ;; .Add 1 ;; Set c = d ;; .Add 2 ;; End With ;; P c.Count & \"|\" & d.Count",
        "0|0|", DisplayName = "W10 the variable Set in the block")]
    public async Task AWithBlockOnAVariable_HoldsTheObjectTheVariableHeld(string body, string printed)
        => Assert.AreEqual(printed, await Run(body));

    // W03: the error in the With statement goes to the handler, Resume Next goes into the block, whose member is 91 - which goes to the handler again.
    [TestMethod]
    public async Task AnErrorInTheBlockAfterAResume_GoesToTheHandlerAgain()
        => Assert.AreEqual("h91|in|h91|after0|out|", await Run("Dim o As Object ;; On Error GoTo H ;; With o.Foo ;; P \"in\" ;; .Add 1 ;; P \"after\" & Err.Number ;; End With"
            + " ;; P \"out\" ;; GoTo Done ;; H: ;; P \"h\" & Err.Number ;; Resume Next ;; Done:"));
}
