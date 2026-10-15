using static RDCore.Tests.Runtime.StdLib.Information.InformationWorkspace;

namespace RDCore.Tests.Runtime.StdLib.Information;

/// <summary>
/// <strong>MS-VBAL §6.1.2.7.1.6 IsMissing</strong>, and the Missing value an omitted optional <c>Variant</c> parameter holds.
/// </summary>
/// <remarks>
/// Each expected value is what MS-VBA printed for the same source, in Excel. Missing is an error value (448, "Named argument not found")
/// that only <c>IsMissing</c> tells apart.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.7 Information")]
public sealed class IsMissingTests
{
    private const string Optionals =
        "Private Sub Opt(Optional v As Variant, Optional x As Long, Optional w As Variant = 5, Optional o As Object, Optional u)\r\n" +
        "    Debug.Print IsMissing(v) & \" \" & TypeName(v) & \" \" & VarType(v) & \" \" & IsError(v) & \" \" & IsEmpty(v)\r\n" +
        "    Debug.Print IsMissing(x) & \" \" & TypeName(x)\r\n" +
        "    Debug.Print IsMissing(w) & \" \" & TypeName(w)\r\n" +
        "    Debug.Print IsMissing(o) & \" \" & TypeName(o)\r\n" +
        "    Debug.Print IsMissing(u)\r\n" +
        "    Forward v\r\n" +
        "End Sub\r\n" +
        "Private Sub Forward(Optional f As Variant)\r\n" +
        "    Debug.Print IsMissing(f) & \" \" & TypeName(f)\r\n" +
        "End Sub";

    [TestMethod]
    public async Task AnOmittedOptionalVariant_IsMissing_AndAnErrorValue()
        => CollectionAssert.AreEqual(new[]
        {
            "True Error 10 True False",
            // a typed optional parameter takes its type's default, and one with a declared default takes that: neither is missing.
            "False Long",
            "False Integer",
            "False Nothing",
            // a parameter declared with no type is a Variant.
            "True",
            // passed on to another optional parameter, it is still missing there.
            "True Error",
        }, await Run(Optionals, "Opt"));

    [TestMethod]
    public async Task ASuppliedArgument_IsNotMissing()
        => CollectionAssert.AreEqual(new[] { "False Integer 2 False False", "False Long", "False Integer", "False Collection", "False", "False Integer" },
            await Run(Optionals, "Opt 1, 2, 3, New Collection, 4"));

    [TestMethod]
    [DataRow("Empty", "False Empty 0 False True")]
    [DataRow("Null", "False Null 1 False False")]
    // an error value is not missing, even one of code 448.
    [DataRow("CVErr(448)", "False Error 10 True False")]
    public async Task AnArgumentThatIsAnotherSpecialValue_IsNotMissing(string argument, string expected)
        => Assert.AreEqual(expected, (await Run(Optionals, $"Opt {argument}"))[0]);

    private const string UseMissing = "Private Sub Use(Optional v As Variant)\r\n    On Error Resume Next\r\n{0}\r\nEnd Sub";

    [TestMethod]
    [DataRow("Debug.Print CStr(v)", "Error 448")]
    [DataRow("Debug.Print CLng(v)", "448")]
    [DataRow("Debug.Print v + 1: Debug.Print \"err \" & Err.Number", "err 13")]
    [DataRow("Debug.Print \"x\" & v: Debug.Print \"err \" & Err.Number", "err 13")]
    [DataRow("Debug.Print (v = 1): Debug.Print \"err \" & Err.Number", "err 13")]
    // a copy of it is missing too.
    [DataRow("Dim copy: copy = v: Debug.Print IsMissing(copy) & \" \" & VarType(copy)", "True 10")]
    [DataRow("Debug.Print IsMissing(IIf(True, v, 1))", "True")]
    // assigned a value, it is a value.
    [DataRow("v = 5: Debug.Print IsMissing(v) & \" \" & v", "False 5")]
    public async Task TheMissingValue_IsErrorValue448(string statement, string expected)
        => CollectionAssert.AreEqual(new[] { expected }, await Run(string.Format(UseMissing, statement), "Use"));

    [TestMethod]
    public async Task TheMissingValue_PassedToATypedParameter_IsATypeMismatch()
        => CollectionAssert.AreEqual(new[] { "err 13" }, await Run(
            string.Format(UseMissing, "Debug.Print Twice(v): Debug.Print \"err \" & Err.Number") +
            "\r\nPrivate Function Twice(ByVal n As Long) As Long\r\n    Twice = n * 2\r\nEnd Function",
            "Use"));

    [TestMethod]
    public async Task TheMissingValue_PassedToAnOptionalByValueParameter_IsMissingThere()
        => CollectionAssert.AreEqual(new[] { "True 10" }, await Run(
            "Private Sub Use(Optional v As Variant)\r\n    Inner v\r\nEnd Sub\r\n" +
            "Private Sub Inner(Optional ByVal w As Variant)\r\n    Debug.Print IsMissing(w) & \" \" & VarType(w)\r\nEnd Sub",
            "Use"));

    #region ParamArray and empty arrays

    private const string Params = "Private Sub Params(ParamArray p() As Variant)\r\n" +
        "    Debug.Print IsMissing(p) & \" \" & TypeName(p) & \" \" & VarType(p) & \" \" & IsArray(p) & \" \" & UBound(p)\r\nEnd Sub";

    [TestMethod]
    public async Task AParamArrayThatCollectedNothing_IsMissing()
        // MS-VBAL says IsMissing "always returns False" for a ParamArray; MS-VBA returns True for one that collected nothing.
        => CollectionAssert.AreEqual(new[] { "True Variant() 8204 True -1" }, await Run(Params, "Params"));

    [TestMethod]
    public async Task AParamArrayThatCollectedArguments_IsNotMissing()
        => CollectionAssert.AreEqual(new[] { "False Variant() 8204 True 1" }, await Run(Params, "Params 1, \"a\""));

    [TestMethod]
    [DataRow("", "IsMissing(Array())", "True")]
    [DataRow("Dim x: x = Array()", "IsMissing(x)", "True")]
    [DataRow("", "IsMissing(CVar(Array()))", "True")]
    [DataRow("", "IsMissing(Array(1))", "False")]
    [DataRow("Dim x: x = Array(1)", "IsMissing(x)", "False")]
    // only an empty array of Variant is missing.
    [DataRow("Dim s() As String", "IsMissing(s)", "False")]
    // an array that has no dimensions at all is not empty: it is not allocated.
    [DataRow("Dim a() As Long", "IsMissing(a)", "False")]
    public async Task AnEmptyVariantArray_IsMissing(string setup, string expression, string expected)
        => CollectionAssert.AreEqual(new[] { expected }, await Run(string.Empty, [.. setup.Length == 0 ? [] : new[] { setup }, $"Debug.Print {expression}"]));

    [TestMethod]
    public async Task AnEmptyVariantArrayReturnedByAFunction_IsMissing()
        => CollectionAssert.AreEqual(new[] { "True", "True" }, await Run(
            "Private Function Typed() As Variant()\r\n    Typed = Array()\r\nEnd Function\r\n" +
            "Private Function Untyped() As Variant\r\n    Untyped = Array()\r\nEnd Function",
            "Debug.Print IsMissing(Typed())",
            "Debug.Print IsMissing(Untyped())"));

    [TestMethod]
    public async Task AnEmptyVariantArrayPassedToAnOptionalParameter_IsMissing()
        => CollectionAssert.AreEqual(new[] { "True Variant()" }, await Run(
            "Private Sub Arr(Optional a As Variant)\r\n    Debug.Print IsMissing(a) & \" \" & TypeName(a)\r\nEnd Sub",
            "Arr Array()"));

    [TestMethod]
    [Ignore("A variable declared as a Variant array is passed to IsMissing by reference, and MS-VBA does not read an empty one as missing; " +
        "the standard library's arguments arrive as values, so StdInformation.IsMissing cannot tell it from Array() itself.")]
    public async Task AnEmptyArrayInAVariableDeclaredAsAVariantArray_IsNotMissing()
        => CollectionAssert.AreEqual(new[] { "False" }, await Run(string.Empty, "Dim e() As Variant", "e = Array()", "Debug.Print IsMissing(e)"));

    #endregion
}
