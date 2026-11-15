using static RDCore.Tests.Cli.ModuleWorkspace;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Tests.Cli;

/// <summary>
/// A record of a user-defined type is passed by reference, to a parameter of its type: MS-VBA refuses a parameter of one declared <c>ByVal</c>
/// (<see cref="RDCore.Runtime.LanguageCore.Errors.VBCompileErrorId.UserDefinedTypeMayNotBePassedByVal"/>), a record given to a <c>Variant</c>
/// (<see cref="RDCore.Runtime.LanguageCore.Errors.VBCompileErrorId.UserDefinedTypeNotCoercibleToVariant"/>), and one given in parentheses, which is a value and no variable to pass
/// (<see cref="RDCore.Runtime.LanguageCore.Errors.VBCompileErrorId.VariableRequired"/>). Each expectation is what MS-VBA reported compiling the same code, observed in Excel,
/// where RD-VBA crashed or ran. An event's record parameter is the one MS-VBA compiles <c>ByVal</c>.
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
    [DataRow("Private Property Get Pg(ByVal t As TPair) As Long\r\nEnd Property", "", DisplayName = "of a Property Get (D05)")]
    [DataRow("Private Property Let Pl(ByVal t As TPair)\r\nEnd Property", "", DisplayName = "the value of a Property Let (D06)")]
    [DataRow("Private Property Let Pl(ByVal t As TPair, ByVal v As Long)\r\nEnd Property", "", DisplayName = "of a Property Let (D07)")]
    [DataRow("Private Declare PtrSafe Sub Zero Lib \"kernel32\" Alias \"RtlZeroMemory\" (ByVal t As TPair, ByVal n As LongPtr)", "", DisplayName = "of a Declare Sub (E01)")]
    [DataRow("Private Declare PtrSafe Function Zero Lib \"kernel32\" Alias \"RtlZeroMemory\" (ByVal t As TPair, ByVal n As LongPtr) As Long", "", DisplayName = "of a Declare Function (E07)")]
    public Task ARecordParameter_DeclaredByVal_IsRefused(string procedure, string statement)
        => AssertCompileError(ByVal, procedure, statement);

    [TestMethod]
    [DataRow("Private Sub G(ByVal v As Variant)\r\nEnd Sub", "G t1", DisplayName = "ByVal (I11)")]
    [DataRow("Private Sub G(v As Variant)\r\nEnd Sub", "G t1", DisplayName = "ByRef (I12)")]
    [DataRow("Private Sub G(a)\r\nEnd Sub", "G t1", DisplayName = "untyped (C09)")]
    [DataRow("Private Sub G(ParamArray a() As Variant)\r\nEnd Sub", "G t1", DisplayName = "a ParamArray (C08)")]
    [DataRow("", "Dim c As New Collection: c.Add t1", DisplayName = "a library's (C14)")]
    [DataRow("", "Dim v As Variant: v = t1", DisplayName = "assigned (C07)")]
    [DataRow("Private Sub G(v As Variant)\r\nEnd Sub", "G v:=t1", DisplayName = "a named argument")]
    [DataRow("", "Debug.Print TypeName(t1)", DisplayName = "to TypeName (D14)")]
    [DataRow("", "Debug.Print VarType(t1)", DisplayName = "to VarType (E05)")]
    [DataRow("", "Debug.Print IsEmpty(t1)", DisplayName = "to IsEmpty (E06)")]
    [DataRow("Private Sub G(v As Variant)\r\nEnd Sub", "Dim ts(2) As TPair: G ts", DisplayName = "an array of records, to a parameter (D12)")]
    [DataRow("", "Dim ts(2) As TPair, v As Variant: v = ts", DisplayName = "an array of records, assigned (D13)")]
    public Task ARecordGivenToAVariant_IsRefused(string procedure, string statement)
        => AssertCompileError(ToVariant, procedure, statement);

    [TestMethod]
    public async Task ARecordParameter_DeclaredByVal_OfAnEvent_Compiles()
    {
        var errors = await LoadErrorsAsync([("Box", ClassModule("Box", "Option Explicit", "Public Event Changed(ByVal t As TPair)"))], Modules,
            "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\nEnd Sub\r\n");

        Assert.IsEmpty(errors, string.Join(" | ", errors));
    }

    [TestMethod]
    [DataRow("F (t1)", DisplayName = "F (t1) (I13)")]
    [DataRow("Call F((t1))", DisplayName = "Call F((t1)) (C10)")]
    public Task ARecordGivenInParentheses_IsAValue_AndNoVariableToPass(string statement)
        => AssertCompileError(VariableRequired, "Private Sub F(pt As TPair)\r\n    pt.X = pt.X + 1\r\nEnd Sub", statement);

    [TestMethod]
    public Task ARecordGivenInParentheses_BeforeAnotherArgument_IsAValue_AndNoVariableToPass()
        => AssertCompileError(VariableRequired, "Private Sub F(pt As TPair, n As Long)\r\nEnd Sub", "F (t1), 1");

    [TestMethod]
    [DataRow("Len", DisplayName = "Len")]
    [DataRow("LenB", DisplayName = "LenB (D17)")]
    public async Task TheLengthOfARecord_IsNoVariantOfIt(string function)
    {
        var errors = await LoadErrorsAsync([], Modules, $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n    Dim t1 As TPair\r\n    Debug.Print {function}(t1)\r\nEnd Sub\r\n");

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
