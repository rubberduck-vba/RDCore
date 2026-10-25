using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.6.10, §5.6.12</strong> A project, a procedural module and an enumeration qualify the names of what they declare, and by themselves are
/// no value: MS-VBA reports what each is, wherever it is written as an expression, but where the place it is written in has an error of its own.
/// </summary>
/// <remarks>
/// Each expected error is the one MS-VBA reported for the same code, in Excel (64-bit, en-US, oracle\s29 N, C, O, W and Y cases).
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.6.10 Simple Name Expressions")]
public sealed class NameIsNoValueTests
{
    private static readonly IReadOnlyList<(string Name, string Source)> Modules =
        [("Globals", StandardModule("Globals", "Public Count As Long", "Public Enum Color", "    Red = 1", "End Enum",
            "Public Function LongFn() As Long", "    LongFn = 7", "End Function"))];

    private static string Program(params string[] body) => string.Join("\r\n",
        "Attribute VB_Name = \"Program\"",
        "Option Explicit",
        "Private Type Pair", "    X As Long", "End Type",
        "Public Sub Main()",
        "    " + string.Join("\r\n    ", body),
        "End Sub",
        string.Empty);

    private static async Task AssertError(string expected, params string[] body)
    {
        var errors = await LoadErrorsAsync([], Modules, Program(body));

        Assert.HasCount(1, errors, string.Join("; ", errors));
        StringAssert.StartsWith(errors[0], expected);
    }

    [TestMethod]
    [DataRow("Debug.Print VBA")]
    [DataRow("Dim x As Variant: x = VBA")]
    [DataRow("Debug.Print Not VBA")]
    [DataRow("Debug.Print -VBA")]
    [DataRow("VBA")]
    [DataRow("VBA = 1")]
    [DataRow("Debug.Print IsObject(VBA)")]
    public Task AProject_IsNoValue(string statement)
        => AssertError("Expected variable or procedure, not project", statement);

    [TestMethod]
    [DataRow("Debug.Print Strings")]
    [DataRow("Debug.Print VBA.Strings")]
    [DataRow("Debug.Print Globals")]
    [DataRow("Debug.Print Program")]
    [DataRow("Debug.Print (Strings)")]
    [DataRow("Debug.Print Not Globals")]
    [DataRow("If Strings Then Debug.Print 1")]
    [DataRow("Strings")]
    [DataRow("Call Strings")]
    [DataRow("Strings = 1")]
    [DataRow("Set Globals = Nothing")]
    [DataRow("Debug.Print TypeName(Strings)")]
    public Task AModule_IsNoValue(string statement)
        => AssertError("Expected variable or procedure, not module", statement);

    [TestMethod]
    [DataRow("Dim x As Variant: x = Color")]
    [DataRow("Debug.Print Globals.Color")]
    [DataRow("Debug.Print Not Color")]
    [DataRow("Color = 1")]
    [DataRow("Debug.Print TypeName(Color)")]
    public Task AnEnumeration_IsNoValue(string statement)
        => AssertError("Expected variable or procedure, not enum type", statement);

    [TestMethod]
    [DataRow("Debug.Print Color.Red")]
    [DataRow("Debug.Print Globals.Color.Red")]
    [DataRow("Dim c As Color: c = Color.Red: Debug.Print c")]
    public async Task AnEnumeration_QualifiesItsMembers(string statement)
        => CollectionAssert.AreEqual(new[] { "1" }, await RunAsync([], Modules, Program(statement)));

    [TestMethod]
    [DataRow("Debug.Print VBA + 1")]
    [DataRow("Debug.Print 1 + Strings")]
    [DataRow("Debug.Print Strings & \"x\"")]
    [DataRow("Debug.Print Strings = 1")]
    [DataRow("Debug.Print Strings Is Nothing")]
    [DataRow("Debug.Print Strings Like \"x\"")]
    [DataRow("Debug.Print Strings Mod 2")]
    [DataRow("Debug.Print Color + 1")]
    [DataRow("Debug.Print Globals + 1")]
    [DataRow("Dim o As Object: Set o = VBA")]
    [DataRow("Dim o As Object: Set o = Strings")]
    [DataRow("Dim o As Object: Set o = Color")]
    public Task AsAnOperandOfABinaryOperator_OrAsTheValueOfASet_ItIsATypeMismatch(string statement)
        => AssertError("Type mismatch", statement);

    [TestMethod]
    [DataRow("Debug.Print Len(Globals)")]
    [DataRow("Debug.Print Len(VBA)")]
    [DataRow("Debug.Print LenB(Strings)")]
    [DataRow("Debug.Print Len(Color)")]
    public Task AsWhatLenMeasures_ItIsNoVariable(string statement)
        => AssertError("Variable required", statement);

    [TestMethod]
    [DataRow("With Strings: End With")]
    [DataRow("With VBA: End With")]
    [DataRow("With Color: End With")]
    [DataRow("Dim n As Long: With n: End With")]
    [DataRow("Dim s As String: With s: End With")]
    [DataRow("Dim d As Date: With d: End With")]
    [DataRow("With 5: End With")]
    [DataRow("With \"abc\": End With")]
    [DataRow("With 5 + 1: End With")]
    public Task AWithBlock_IsAboutAnObjectARecordOrAVariant(string statement)
        => AssertError("With object must be user-defined type, Object, or Variant", statement);

    [TestMethod]
    public async Task AWithBlock_OfARecordOrOfNothing_Compiles()
        => CollectionAssert.AreEqual(new[] { "0", "0" }, await RunAsync([], Modules, Program(
            "Dim q As Pair", "With q", "    Debug.Print .X", "End With", "On Error Resume Next", "With Nothing", "End With", "Debug.Print Err.Number")));

    [TestMethod]
    [DataRow("Dim v As Variant: For Each v In Strings: Next")]
    [DataRow("Dim v As Variant: For Each v In VBA: Next")]
    [DataRow("Dim v As Variant: For Each v In Color: Next")]
    [DataRow("Dim n As Long, v As Variant: For Each v In n: Next")]
    [DataRow("Dim s As String, v As Variant: For Each v In s: Next")]
    [DataRow("Dim q As Pair, v As Variant: For Each v In q: Next")]
    [DataRow("Dim v As Variant: For Each v In LongFn(): Next")]
    public Task AForEachLoop_IteratesAnArrayOrAnObject(string statement)
        => AssertError("For Each may only iterate over a collection object or an array", statement);

    [TestMethod]
    [DataRow("Dim v As Variant: For Each v In 5: Next")]
    [DataRow("Dim v As Variant: For Each v In \"abc\": Next")]
    [DataRow("Dim v As Variant: For Each v In True: Next")]
    [DataRow("Dim v As Variant: For Each v In Nothing: Next")]
    [DataRow("Dim v As Variant: For Each v In Empty: Next")]
    [DataRow("Dim v As Variant: For Each v In (5): Next")]
    [DataRow("Dim v As Variant: For Each v In 5 + 1: Next")]
    public Task AForEachLoop_OfWhatIsNoNameMemberOrCall_IsASyntaxError(string statement)
        => AssertError("Syntax error", statement);

    [TestMethod]
    public async Task AForEachLoop_OfAVariantThatHoldsNoArray_IsError92()
        => CollectionAssert.AreEqual(new[] { "92" }, await RunAsync([], Modules, Program(
            "Dim x As Variant, v As Variant", "x = 5", "On Error Resume Next", "For Each v In x", "Next", "Debug.Print Err.Number")));

    [TestMethod]
    [DataRow("Dim x As Variant: x = Pair", "Variable not defined")]
    [DataRow("Dim x As Variant: x = Program.Pair", "Method or data member not found")]
    [DataRow("Dim x As Variant: x = Collection", "Variable not defined")]
    [DataRow("Dim x As Variant: x = VBA.Collection", "Method or data member not found")]
    public Task AUserDefinedTypeOrAClass_IsNoValueToBeginWith(string statement, string expected)
        => AssertError(expected, statement);
}
