using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Variant</c> parameter passed a <c>String</c>, an error value or no argument by value is held in a Variant's cell, as one passed a scalar, <c>Empty</c>
/// or <c>Null</c> is: compiled code reads and writes it where its frame keeps it. The parameter holds what it was given (MS-VBAL §5.3.1.11), whatever the
/// caller's variable is assigned meanwhile, and an omitted argument is <c>Missing</c> in it and in a procedure it is passed on to.
/// </summary>
[TestClass]
public sealed class CompiledVariantParameterCellTests
{
    // TypeOf of a Variant that holds no object is False (MS-VBAL §5.6.7): compiled code tests the parameter in its cell, with no hand-over to the
    // interpreter, for a String, an error and Missing.
    [TestMethod]
    public async Task AVariantParameterPassedAStringAnErrorOrNothing_IsTestedWithTypeOfInCompiledCode()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Uses(\"x\"); Uses(CVErr(7)); Uses()",
            "End Sub",
            "Private Function Uses(Optional ByVal V As Variant) As Long",
            "    Dim I As Long",
            "    For I = 1 To 3",
            "        If TypeOf V Is Collection Then Uses = Uses + 100 Else Uses = Uses + 1",
            "    Next",
            "End Function"), "Uses", recoveries: 0, walkBacks: 0, expected: "| 3  3  3 |");

    // a Let of a Long to the parameter is written into its cell by compiled code, over the String, the error or Missing it was passed.
    [TestMethod]
    public async Task AVariantParameterPassedAStringAnErrorOrNothing_IsAssignedInItsCell()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Uses(\"x\"); Uses(CVErr(7)); Uses()",
            "End Sub",
            "Private Function Uses(Optional ByVal V As Variant) As Long",
            "    Dim I As Long",
            "    For I = 1 To 3",
            "        V = I",
            "    Next",
            "    Uses = V",
            "End Function"), "Uses", recoveries: 0, expected: "| 3  3  3 |");

    // the parameter holds the String it was given: the caller's variable, assigned through a ByRef parameter during the call, or changed by a Mid statement,
    // leaves it as it was, and what is assigned to the parameter leaves the caller's variable as it was. A String and a Variant that holds one, either way.
    [TestMethod]
    public async Task AVariantParameterPassedAString_HoldsItWhateverTheCallersVariableIsAssigned()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim S As String, W As Variant",
            "    S = \"a\"",
            "    Assigned S, S",
            "    Debug.Print S",
            "    W = \"abc\"",
            "    Changed W, W",
            "    Debug.Print W",
            "End Sub",
            "Private Sub Assigned(ByRef T As String, ByVal V As Variant)",
            "    T = \"b\"",
            "    Debug.Print V; T",
            "    V = V & \"x\"",
            "    Debug.Print V; T",
            "End Sub",
            "Private Sub Changed(ByRef T As Variant, ByVal V As Variant)",
            "    Mid(T, 1, 1) = \"z\"",
            "    Debug.Print V; T",
            "    Mid(V, 2, 1) = \"y\"",
            "    Debug.Print V; T",
            "End Sub"), expected: "| ab ; axb ; b ; abczbc ; ayczbc ; zbc |");

    // MS-VBAL §6.1.2.7.1.5, §6.1.2.7.1.6 and §6.1.2.7.1.13: an error value and Missing are both of the Error subtype, and only Missing is missing.
    // Missing passed on to an Optional parameter, by reference or by value, is missing there too.
    [TestMethod]
    public async Task AVariantParameterPassedAnErrorOrNothing_IsAnErrorAndMissingPassedOn()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Outer CVErr(5)",
            "    Outer",
            "End Sub",
            "Private Sub Outer(Optional ByVal V As Variant)",
            "    Debug.Print VarType(V); TypeName(V); IsError(V); IsMissing(V); IsNull(V); IsEmpty(V)",
            "    Inner V",
            "    InnerByVal V",
            "End Sub",
            "Private Sub Inner(Optional ByRef W As Variant)",
            "    Debug.Print \"ref\"; IsMissing(W); VarType(W)",
            "End Sub",
            "Private Sub InnerByVal(Optional ByVal W As Variant)",
            "    Debug.Print \"val\"; IsMissing(W); VarType(W)",
            "End Sub"),
            expected: "| 10 ErrorTrueFalseFalseFalse ; refFalse 10 ; valFalse 10 ; 10 ErrorTrueTrueFalseFalse ; refTrue 10 ; valTrue 10 |");
}
