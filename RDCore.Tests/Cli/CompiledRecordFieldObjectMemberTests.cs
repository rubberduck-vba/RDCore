using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A member of an object of a class of the workspace, called on the object an object field of a record holds (<c>this.State.Exists(K)</c>), its default
/// member included (<c>this.State(K)</c>): compiled code calls it on the object the field holds, where the record holds it, as the interpreter calls it.
/// A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledRecordFieldObjectMemberTests
{
    // a bag of values by key, whose Item is its default member, and a class that holds one in the object field of its record.
    private static readonly (string Name, string Source)[] Classes =
    [
        ("Bag", ClassModule("Bag",
            "Option Explicit",
            "Private m As Collection",
            "Private Sub Class_Initialize()",
            "    Set m = New Collection",
            "End Sub",
            "Public Sub Add(Key As Variant, Value As Variant)",
            "    m.Add Value, Key",
            "End Sub",
            "Public Function Exists(Key As Variant) As Boolean",
            "    On Error Resume Next",
            "    Dim V As Variant",
            "    V = m(Key)",
            "    Exists = Err.Number = 0",
            "    Err.Clear",
            "End Function",
            "Public Property Get Item(Key As Variant) As Variant",
            "Attribute Item.VB_UserMemId = 0",
            "    Item = m(Key)",
            "End Property",
            "Public Property Get Count() As Long",
            "    Count = m.Count",
            "End Property")),
        ("Holder", ClassModule("Holder",
            "Option Explicit",
            "Private Type THolder",
            "    State As Bag",
            "End Type",
            "Private this As THolder",
            "Private Sub Class_Initialize()",
            "    Set this.State = New Bag",
            "End Sub",
            "Public Sub Keep(ByVal K As String, ByVal V As Boolean)",
            "    this.State.Add K, V",
            "End Sub",
            "Public Function Hit(ByVal K As String) As Boolean",
            "    Dim S As String",
            "    S = K",
            "    If this.State.Exists(S) Then Hit = True",
            "End Function",
            "Public Function IsSet(ByVal K As String) As Boolean",
            "    If this.State(K) Then IsSet = True",
            "End Function",
            "Public Function HitV(ByVal K As String) As Boolean",
            "    Dim S As Variant",
            "    S = K",
            "    If this.State.Exists(S) Then HitV = True",
            "End Function",
            "Public Function HitA(ByVal K As String) As Boolean",
            "    Dim S As Variant, B As Boolean",
            "    S = K",
            "    B = this.State.Exists(S)",
            "    HitA = B",
            "End Function",
            "Public Function IsSetV(ByVal K As String) As Boolean",
            "    Dim V As Variant",
            "    V = K",
            "    If this.State(V) Then IsSetV = True",
            "End Function",
            "Public Function IsSetI(ByVal K As String) As Boolean",
            "    Dim V As Variant",
            "    V = K",
            "    If this.State.Item(V) Then IsSetI = True",
            "End Function",
            "Public Property Get SizeL() As Long",
            "    SizeL = this.State.Count",
            "End Property",
            "Public Property Get Size() As Byte",
            "    Size = this.State.Count",
            "End Property")),
    ];

    private static readonly string Main = Program(
        "Public Sub Main()",
        "    Dim H As New Holder",
        "    H.Keep \"a\", True",
        "    H.Keep \"b\", False",
        "    Debug.Print H.Hit(\"a\"); H.Hit(\"z\"); H.IsSet(\"a\"); H.IsSet(\"b\"); H.Size;",
        "    Debug.Print H.HitV(\"a\"); H.HitA(\"a\"); H.IsSetV(\"a\"); H.IsSetI(\"a\"); H.SizeL",
        "End Sub");

    // the program run both ways: each procedure named compiled, with no statement stepped.
    private static async Task AssertNoneSteppedAsync(params string[] procedures)
    {
        var compiled = await AssertAlikeAsync(Main, Classes, expected: "TrueFalseTrueFalse 2 TrueTrueTrueTrue 2");
        foreach (var procedure in procedures)
        {
            Assert.IsTrue(compiled.TryGetValue(procedure, out var result), $"{procedure} was not called");
            Assert.IsNotNull(result.Body, $"{procedure}: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
            Assert.IsEmpty(result.Stepped, $"{procedure} steps: {string.Join("; ", result.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
        }
    }

    // a function given a String, and a Variant, by reference; and what it returns assigned.
    [TestMethod]
    public async Task AFunctionOfTheObjectAFieldHolds_IsCalledByCompiledCode()
        => await AssertNoneSteppedAsync("Hit", "HitV", "HitA");

    // the default member, given a String and a Variant, the condition of an If.
    [TestMethod]
    public async Task TheDefaultMemberOfTheObjectAFieldHolds_IsCalledByCompiledCode()
        => await AssertNoneSteppedAsync("IsSet", "IsSetV");

    // the same member named, which returns a Variant.
    [TestMethod]
    public async Task APropertyWithAnArgumentOfTheObjectAFieldHolds_IsCalledByCompiledCode()
        => await AssertNoneSteppedAsync("IsSetI");

    // a Long property, read as a Long and as a Byte.
    [TestMethod]
    public async Task APropertyOfTheObjectAFieldHolds_IsReadByCompiledCode()
        => await AssertNoneSteppedAsync("Size", "SizeL");
}
