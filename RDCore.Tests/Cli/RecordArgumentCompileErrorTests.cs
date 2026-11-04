using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A record of a user-defined type is passed by reference, to a parameter of its type: MS-VBA refuses a parameter of one declared <c>ByVal</c>
/// (<see cref="SDK.Model.Errors.VBCompileErrorId.UserDefinedTypeMayNotBePassedByVal"/>), a record given to a <c>Variant</c>
/// (<see cref="SDK.Model.Errors.VBCompileErrorId.UserDefinedTypeNotCoercibleToVariant"/>), and one given in parentheses, which is a value and no variable to pass
/// (<see cref="SDK.Model.Errors.VBCompileErrorId.VariableRequired"/>). Each expectation is what MS-VBA reported compiling the same code, recorded in Excel
/// (oracle\s56 I10-I13), where RD-VBA crashed or ran.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.3.1.11 Procedure Invocation Argument Processing")]
public sealed class RecordArgumentCompileErrorTests
{
    private const string ByVal = "User-defined type may not be passed ByVal";
    private const string ToVariant = "Only user-defined types defined in public object modules can be coerced to or from a variant or passed to late-bound functions";
    private const string VariableRequired = "Variable required";

    private static readonly IReadOnlyList<(string Name, string Source)> Modules =
    [
        ("Globals", StandardModule("Globals", "Option Explicit", "Public Type TPair", "    X As Long", "    Y As Long", "End Type")),
    ];

    private static async Task AssertCompileError(string expected, string procedure, params string[] body)
    {
        var errors = await LoadErrorsAsync([], Modules, string.Join("\r\n",
            [
                "Attribute VB_Name = \"Program\"",
                "Option Explicit",
                procedure,
                "Public Sub Main()",
                "    Dim t1 As TPair",
                "    t1.X = 3",
                .. body,
                "End Sub",
            ]) + "\r\n");

        Assert.HasCount(1, errors, string.Join(" | ", errors));
        Assert.StartsWith(expected, errors[0]);
    }

    [TestMethod]
    [DataRow("Private Sub F(ByVal pt As TPair)\r\nEnd Sub", "F t1", DisplayName = "of a Sub, called (I10)")]
    [DataRow("Private Sub F(ByVal pt As TPair)\r\nEnd Sub", "", DisplayName = "of a Sub, never called (C15)")]
    [DataRow("Private Function F(ByVal pt As TPair) As Long\r\nEnd Function", "", DisplayName = "of a Function (C06)")]
    public Task ARecordParameter_DeclaredByVal_IsRefused(string procedure, string statement)
        => AssertCompileError(ByVal, procedure, statement);

    [TestMethod]
    [DataRow("Private Sub G(ByVal v As Variant)\r\nEnd Sub", "G t1", DisplayName = "ByVal (I11)")]
    [DataRow("Private Sub G(v As Variant)\r\nEnd Sub", "G t1", DisplayName = "ByRef (I12)")]
    [DataRow("Private Sub G(a)\r\nEnd Sub", "G t1", DisplayName = "untyped (C09)")]
    [DataRow("Private Sub G(ParamArray a() As Variant)\r\nEnd Sub", "G t1", DisplayName = "a ParamArray (C08)")]
    [DataRow("", "Dim c As New Collection: c.Add t1", DisplayName = "a library's (C14)")]
    [DataRow("", "Dim v As Variant: v = t1", DisplayName = "assigned (C07)")]
    public Task ARecordGivenToAVariant_IsRefused(string procedure, string statement)
        => AssertCompileError(ToVariant, procedure, statement);

    [TestMethod]
    [DataRow("F (t1)", DisplayName = "F (t1) (I13)")]
    [DataRow("Call F((t1))", DisplayName = "Call F((t1)) (C10)")]
    public Task ARecordGivenInParentheses_IsAValue_AndNoVariableToPass(string statement)
        => AssertCompileError(VariableRequired, "Private Sub F(pt As TPair)\r\n    pt.X = pt.X + 1\r\nEnd Sub", statement);

    [TestMethod]
    public Task ARecordGivenInParentheses_BeforeAnotherArgument_IsAValue_AndNoVariableToPass()
        => AssertCompileError(VariableRequired, "Private Sub F(pt As TPair, n As Long)\r\nEnd Sub", "F (t1), 1");

    [TestMethod]
    public async Task TheLengthOfARecord_IsNoVariantOfIt()
    {
        var errors = await LoadErrorsAsync([], Modules, "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n    Dim t1 As TPair\r\n    Debug.Print Len(t1)\r\nEnd Sub\r\n");

        Assert.IsEmpty(errors, string.Join(" | ", errors));
    }

    [TestMethod]
    public async Task ARecordGivenToAParameterOfItsType_ByRef_IsPassed()
        => CollectionAssert.AreEqual(new[] { "4" },
            await RunAsync([], Modules, string.Join("\r\n",
                [
                    "Attribute VB_Name = \"Program\"",
                    "Option Explicit",
                    "Private Sub F(pt As TPair)",
                    "    pt.X = pt.X + 1",
                    "End Sub",
                    "Public Sub Main()",
                    "    Dim t1 As TPair",
                    "    t1.X = 3",
                    "    F t1",
                    "    Debug.Print t1.X",
                    "End Sub",
                ]) + "\r\n"));
}
