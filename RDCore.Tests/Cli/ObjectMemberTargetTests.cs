using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.4.3.8, §5.4.3.9</strong> A member of an object written as the target of an assignment that is no property: a function is called, with the
/// arguments the target gives it, and what it returns is assigned through its default member; a Sub has no value to assign through.
/// </summary>
/// <remarks>
/// Each expected outcome is what MS-VBA did with the same code, early-bound, in Excel (64-bit, en-US, oracle\s44 E01-E18, F01-F13): the compile error it
/// reported, or the error number the statement raised.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.8 Let Statement")]
public sealed class ObjectMemberTargetTests
{
    private static readonly IReadOnlyList<(string Name, string Source)> Classes =
    [
        ("Counter", ClassModule("Counter",
            "Private mValue As Long",
            "Public Sub Increment()", "    mValue = mValue + 1", "End Sub")),
        ("Opt", ClassModule("Opt",
            "Public Function F(Optional ByVal n As Long = 7) As Long", "    F = n", "End Function",
            "Public Function V(Optional ByVal x As Variant = 3) As Variant", "    V = x", "End Function")),
        ("Wrapper", ClassModule("Wrapper",
            "Private mItem As Collection",
            "Public Property Get Item() As Collection", "Attribute Item.VB_UserMemId = 0", "    If mItem Is Nothing Then Set mItem = New Collection",
            "    Set Item = mItem", "End Property")),
    ];

    private static readonly IReadOnlyList<(string Name, string Source)> Modules =
        [("Globals", StandardModule("Globals", "Public Sub SomeSub()", "End Sub"))];

    private static string Program(string body) => string.Join("\r\n",
        "Attribute VB_Name = \"Program\"",
        "Public Sub Main()",
        "    On Error Resume Next",
        "    " + string.Join("\r\n    ", body.Split(" ;; ")),
        "    Debug.Print CStr(Err.Number)",
        "End Sub",
        string.Empty);

    [TestMethod]
    [DataRow("Dim c As New Collection ;; c.Add 1 ;; c(1) = 5", "424", DisplayName = "E01: the default member of a Collection, given an argument")]
    [DataRow("Dim c As New Collection ;; c.Add 1 ;; c.Item(1) = 5", "424", DisplayName = "E02: named")]
    [DataRow("Dim c As New Collection ;; c.Add 1 ;; Set c(1) = New Collection", "424", DisplayName = "E07: a Set")]
    [DataRow("Dim c As New Collection ;; c.Add \"a\" ;; c(\"zz\") = 5", "5", DisplayName = "E15: the call raises first")]
    [DataRow("Dim o As New Opt ;; o.V(1) = 5", "424", DisplayName = "E11: a Variant function of a class")]
    [DataRow("Dim o As New Opt ;; o.V = 5", "424", DisplayName = "E13: with no argument")]
    [DataRow("Dim o As New Opt ;; Set o.V(1) = New Collection", "424", DisplayName = "E18: a Set")]
    [DataRow("Dim w As New Wrapper ;; w.Item.Add 1 ;; w.Item(1) = 5", "424", DisplayName = "E16: a Property Get that takes no argument, indexed")]
    [DataRow("Dim c As New Collection, o As New Collection ;; c.Add o ;; c(1) = 5", "438", DisplayName = "E05: what it returns is an object whose default member takes an argument")]
    [DataRow("Dim c As New Collection ;; c.Add New Collection ;; Set c(1) = New Collection", "438", DisplayName = "E08: a Set of one")]
    [DataRow("Dim c As New Collection, w As New Wrapper ;; c.Add w ;; c(1) = 5", "438", DisplayName = "E17: through an object's own default member")]
    public async Task AFunctionOfAnObject_IsCalled_AndWhatItReturnsAssignedThrough(string body, string expected)
        => Assert.AreEqual(expected, (await RunAsync(Classes, Modules, Program(body))).Last());

    [TestMethod]
    [DataRow("Dim c As New Collection ;; c.Count = 3", "Function call on left-hand side of assignment must return Variant or Object", DisplayName = "E03: a library's")]
    [DataRow("Dim o As New Opt ;; o.F = 5", "Function call on left-hand side of assignment must return Variant or Object", DisplayName = "F01: a class's")]
    [DataRow("Dim c As New Collection ;; Set c.Count = Nothing", "Invalid use of property", DisplayName = "F05: a Set")]
    [DataRow("Dim o As New Opt ;; Set o.F(1) = Nothing", "Type mismatch", DisplayName = "F07: a Set, given an argument")]
    public async Task AFunctionOfAnObjectThatReturnsNoObjectNorVariant_IsNoTarget(string body, string expected)
    {
        var errors = await LoadErrorsAsync(Classes, Modules, Program(body));

        StringAssert.StartsWith(Assert.ContainsSingle(errors), expected);
    }

    [TestMethod]
    [DataRow("Dim c As New Collection ;; c.Remove(1) = 5", DisplayName = "E04: a library's, given an argument")]
    [DataRow("Err.Raise(5) = 1", DisplayName = "E10: of the ErrObject")]
    [DataRow("Err.Clear = 1", DisplayName = "F02: with no argument")]
    [DataRow("Dim k As New Counter ;; k.Increment = 1", DisplayName = "F03: a class's")]
    [DataRow("Dim c As New Collection ;; Set c.Remove(1) = Nothing", DisplayName = "F08: a Set")]
    [DataRow("Globals.SomeSub = 1", DisplayName = "F09: of a module, through it")]
    public async Task ASubOfAnObjectOrAModule_IsNoTarget(string body)
    {
        var errors = await LoadErrorsAsync(Classes, Modules, Program(body));

        StringAssert.StartsWith(Assert.ContainsSingle(errors), "Expected Function or variable");
    }
}
