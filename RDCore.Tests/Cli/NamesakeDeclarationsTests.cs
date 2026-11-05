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

    private const string EnumF = "Public Enum F\r\n    Zero\r\n    One\r\nEnd Enum";

    private const string FunctionF = "Public Function F(ByVal n As Long) As Long\r\n    F = n * 2\r\nEnd Function";

    // Program, with `declarations` before its procedures, `procedures` after them, and Main.
    private static string StandardProgram(string declarations, string procedures, string main)
        => $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n{declarations}\r\n{procedures}\r\nPublic Sub Main()\r\n{main}\r\nEnd Sub\r\n";

    private static async Task<string> RunStandardAsync(string declarations, string procedures, string main)
        => string.Join(" ", await ModuleWorkspace.RunAsync([], StandardProgram(declarations, procedures, main)));

    [TestMethod]
    public async Task InAStandardModule_TheBareNameInAnotherProcedure_IsTheFunction()
        // E11
        => Assert.AreEqual("8", await RunStandardAsync(EnumF, FunctionF, "    Debug.Print F(4)"));

    [TestMethod]
    public async Task InAStandardModule_TheFunctionCallsItself_AndAssignsItsResult()
        // E12
        => Assert.AreEqual("10", await RunStandardAsync(EnumF,
            "Public Function F(ByVal n As Long) As Long\r\n    If n <= 0 Then F = 0 Else F = n + F(n - 1)\r\nEnd Function", "    Debug.Print Program.F(4)"));

    [TestMethod]
    public async Task InAStandardModule_TheNameAsAType_IsTheEnum_AndAsAValue_TheFunction()
        // E16
        => Assert.AreEqual("1 2", await RunStandardAsync(EnumF, FunctionF, "    Dim e As F\r\n    e = One\r\n    Debug.Print e\r\n    Debug.Print F(e)"));

    [TestMethod]
    [Ignore("A bare call of a function that takes an argument (Debug.Print F) compiles: MS-VBA's Argument not optional is not checked for it, and F.One is an invalid qualifier instead.")]
    public async Task InAStandardModule_TheNameQualifyingAMember_IsTheFunction_CalledWithoutItsArgument()
    {
        // E17: F.One is F called with no argument, which its parameter is not optional for.
        var errors = await ModuleWorkspace.LoadErrorsAsync([], StandardProgram(EnumF, FunctionF, "    Debug.Print F.One"));

        StringAssert.Contains(Assert.ContainsSingle(errors), RDCore.SDK.Exceptions.VBCompileError_ArgumentNotOptional);
    }

    [TestMethod]
    // MS-VBA stops at the first error, `V = 5` where the variable is assigned: each use of the name is reported here.
    [DataRow("Public V As Long", true, "    V = 5\r\n    Debug.Print V", DisplayName = "E14: a variable after the Enum")]
    [DataRow("Public V As Long", false, "    V = 5\r\n    Debug.Print V", DisplayName = "E20: a variable before the Enum")]
    [DataRow("Private V As Long", false, "    V = 5\r\n    Debug.Print V", DisplayName = "E23: a Private variable")]
    [DataRow("Public Const V As Long = 3", false, "    Debug.Print V", DisplayName = "E22: a constant")]
    public async Task InAStandardModule_AVariableOrConstantAndAnEnumOfOneName_TheNameIsTheEnum(string variable, bool enumFirst, string main)
    {
        const string enumV = "Public Enum V\r\n    Zero\r\n    One\r\nEnd Enum";
        var declarations = enumFirst ? $"{enumV}\r\n{variable}" : $"{variable}\r\n{enumV}";

        var errors = await ModuleWorkspace.LoadErrorsAsync([], StandardProgram(declarations, string.Empty, main));

        Assert.HasCount(main.Split("\r\n").Length, errors, string.Join(" | ", errors));
        Assert.IsTrue(errors.All(error => error.Contains(RDCore.SDK.Exceptions.VBCompileError_ExpectedVariableOrProcedureNotEnumType)), string.Join(" | ", errors));
    }

    [TestMethod]
    public async Task InAClass_AVariableAndAnEnumOfOneName_TheNameIsTheEnum()
    {
        // E21: `V = 5` and `Get5 = V` each, where MS-VBA stops at the first.
        string[] sink = ["Public Enum V", "    Zero", "    One", "End Enum", "Public V As Long", "Public Function Get5() As Long", "    V = 5", "    Get5 = V", "End Function"];

        var errors = await LoadErrorsAsync(sink);

        Assert.HasCount(2, errors);
        Assert.IsTrue(errors.All(error => error.Contains(RDCore.SDK.Exceptions.VBCompileError_ExpectedVariableOrProcedureNotEnumType)), string.Join(" | ", errors));
    }

    [TestMethod]
    public async Task InAStandardModule_TheNameQualifyingAMember_IsTheFunctionsValue_AnInvalidQualifier()
    {
        // E24: F takes no argument, and F.One is a member of the Long it returns.
        var errors = await ModuleWorkspace.LoadErrorsAsync([], StandardProgram(EnumF, "Public Function F() As Long\r\n    F = 7\r\nEnd Function", "    Debug.Print F.One"));

        StringAssert.Contains(Assert.ContainsSingle(errors), RDCore.SDK.Exceptions.VBCompileError_InvalidQualifier);
    }

    [TestMethod]
    public async Task FromAnotherModule_AClassesEnumQualifiesItsMember()
        // E18
        => Assert.AreEqual("1", await RunAsync(PlayerType[..4], "    Dim t As PlayerType\r\n    t = PlayerType.ComputerControlled\r\n    Debug.Print t"));

    [TestMethod]
    public async Task FromAnotherModule_AClassesEnumBesideAGetOfItsName_IsAType_AndQualifiesNoMember()
    {
        // E15, and E19: the member unqualified is the Enum's.
        Assert.AreEqual("1", await RunAsync(PlayerType, "    Dim t As PlayerType\r\n    t = ComputerControlled\r\n    Debug.Print t"));
        var errors = await ModuleWorkspace.LoadErrorsAsync([("Sink", ModuleWorkspace.ClassModule("Sink", ["Option Explicit", .. PlayerType]))],
            Program("    Dim t As PlayerType\r\n    t = PlayerType.ComputerControlled"));

        StringAssert.Contains(Assert.ContainsSingle(errors), RDCore.SDK.Exceptions.VBCompileError_InvalidQualifier);
    }

    [TestMethod]
    public async Task ThroughAnObject_TheGetsValueQualifyingAMember_IsAnInvalidQualifier()
    {
        // E25
        var errors = await ModuleWorkspace.LoadErrorsAsync([("Sink", ModuleWorkspace.ClassModule("Sink", ["Option Explicit", .. PlayerType]))],
            Program("    Debug.Print s.PlayerType.ComputerControlled"));

        StringAssert.Contains(Assert.ContainsSingle(errors), RDCore.SDK.Exceptions.VBCompileError_InvalidQualifier);
    }

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
    public async Task AnEventAndASubOfOneName_RaiseEvent_DoesNotCallTheSub()
        // E09
        => Assert.AreEqual("0", await RunAsync([.. Changed, "Public Sub Fire()", "    RaiseEvent Changed", "End Sub"], "    s.Fire\r\n    Debug.Print s.Count"));
}
