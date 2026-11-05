namespace RDCore.Tests.Cli;

/// <summary>
/// An Enum, or an Event, and a procedure of one name in one module (<c>Property Get PlayerType() As PlayerType</c>, <c>Event Changed</c> and
/// <c>Sub Changed</c>): in a value, the name is the procedure; as a type, it is the Enum; an Event is only ever raised. As MS-VBA has them (Excel-verified,
/// oracle\s64).
/// </summary>
[TestClass]
public sealed class NamesakeDeclarationsTests
{
    private static readonly string[] PlayerType =
    [
        "Public Enum PlayerType", "    HumanControlled", "    ComputerControlled", "End Enum",
        "Public Property Get PlayerType() As PlayerType", "    PlayerType = ComputerControlled", "End Property",
    ];

    private static readonly string[] Changed =
    [
        "Public Event Changed()",
        "Public Count As Long",
        "Public Sub Changed()", "    Count = Count + 1", "End Sub",
    ];

    private static string Program(string main)
        => $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n    Dim s As New Sink\r\n{main}\r\nEnd Sub\r\n";

    private static async Task<string> RunAsync(string[] sink, string main)
        => string.Join(" ", await ModuleWorkspace.RunAsync([("Sink", ModuleWorkspace.ClassModule("Sink", ["Option Explicit", .. sink]))], Program(main)));

    private static Task<string[]> LoadErrorsAsync(string[] sink)
        => ModuleWorkspace.LoadErrorsAsync([("Sink", ModuleWorkspace.ClassModule("Sink", ["Option Explicit", .. sink]))], Program(string.Empty));

    [TestMethod]
    public async Task InThePropertyGet_TheNameQualifyingAMember_IsItsResultVariable_AnInvalidQualifier()
    {
        // E02: in the Get, PlayerType is the Get's result, a value of the Enum, which has no members.
        string[] sink = [.. PlayerType[..4], "Public Property Get PlayerType() As PlayerType", "    PlayerType = PlayerType.ComputerControlled", "End Property"];

        StringAssert.Contains(Assert.ContainsSingle(await LoadErrorsAsync(sink)), RDCore.SDK.Exceptions.VBCompileError_InvalidQualifier);
    }

    [TestMethod]
    public async Task InAnotherProcedure_TheBareName_IsThePropertyGet()
        // E03
        => Assert.AreEqual("1", await RunAsync([.. PlayerType, "Public Function Other() As Long", "    Other = PlayerType", "End Function"], "    Debug.Print s.Other"));

    [TestMethod]
    public async Task InAnotherProcedure_TheNameQualifyingAMember_IsThePropertyGet_AnInvalidQualifier()
    {
        // E04: in a value, the name is the Get, and not the Enum, wherever it is written in the module.
        string[] sink = [.. PlayerType, "Public Function Other() As Long", "    Other = PlayerType.ComputerControlled", "End Function"];

        StringAssert.Contains(Assert.ContainsSingle(await LoadErrorsAsync(sink)), RDCore.SDK.Exceptions.VBCompileError_InvalidQualifier);
    }

    [TestMethod]
    public async Task InAnotherProcedure_TheNameAsAType_IsTheEnum()
        // E05
        => Assert.AreEqual("1", await RunAsync([.. PlayerType, "Public Function Other() As Long", "    Dim t As PlayerType", "    t = ComputerControlled", "    Other = t", "End Function"],
            "    Debug.Print s.Other"));

    [TestMethod]
    [DataRow("    Changed", DisplayName = "E06: the bare name")]
    [DataRow("    Call Changed", DisplayName = "E07: Call")]
    public async Task AnEventAndASubOfOneName_TheNameInAnotherProcedure_CallsTheSub(string call)
        => Assert.AreEqual("1", await RunAsync([.. Changed, "Public Sub Fire()", call, "End Sub"], "    s.Fire\r\n    Debug.Print s.Count"));

    [TestMethod]
    public async Task AnEventAndASubOfOneName_TheSubThroughAnObject_IsCalled()
        // E08
        => Assert.AreEqual("1", await RunAsync(Changed, "    s.Changed\r\n    Debug.Print s.Count"));

    [TestMethod]
    public async Task AnEventAndASubOfOneName_RaiseEvent_RaisesTheEvent_AndDoesNotCallTheSub()
        // E09
        => Assert.AreEqual("0", await RunAsync([.. Changed, "Public Sub Fire()", "    RaiseEvent Changed", "End Sub"], "    s.Fire\r\n    Debug.Print s.Count"));
}
