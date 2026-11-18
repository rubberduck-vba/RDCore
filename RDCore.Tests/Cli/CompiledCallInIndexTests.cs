using RDCore.Runtime.Compiled;
using RDCore.Runtime.LanguageCore.Instructions;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A function of the workspace called for the index of a <c>VBA.Collection</c>'s <c>Item</c>, its default member (<strong>MS-VBAL §6.1.3.1.1.2</strong>,
/// <strong>§5.6.13</strong>), and a function whose <c>Variant</c> result is given to the standard library, as a keyed lookup makes them: compiled, with no
/// statement stepped. A program runs as it does in the interpreter, the error <c>Item</c> raises for a key it does not hold included.
/// </summary>
[TestClass]
public sealed class CompiledCallInIndexTests
{
    // a lookup of values by key, as the cross-platform Dictionary keeps them: a Collection of [key, value] arrays keyed by the key's text.
    private static readonly (string Name, string Source)[] Classes =
    [
        ("Lookup", ModuleWorkspace.ClassModule("Lookup", "Option Explicit",
            "Private pKeyValues As Collection",
            "Private Sub Class_Initialize()",
            "    Set pKeyValues = New Collection",
            "End Sub",
            "Public Sub Add(Key As Variant, Value As Variant)",
            "    pKeyValues.Add Array(Key, Value), FormattedKey(Key)",
            "End Sub",
            "Public Function Exists(Key As Variant) As Boolean",
            "    Exists = Not IsEmpty(GetKeyValue(Key))",
            "End Function",
            "Public Function Item(Key As Variant) As Variant",
            "    Dim KeyValue As Variant",
            "    KeyValue = GetKeyValue(Key)",
            "    If Not IsEmpty(KeyValue) Then Item = KeyValue(1)",
            "End Function",
            "Private Function GetKeyValue(Key As Variant) As Variant",
            "    On Error Resume Next",
            "    GetKeyValue = pKeyValues(FormattedKey(Key))",
            "    Err.Clear",
            "End Function",
            "Private Function FormattedKey(Key As Variant) As String",
            "    If VarType(Key) = vbString Then",
            "        FormattedKey = Key",
            "    Else",
            "        FormattedKey = Key & \"__\" & VarType(Key)",
            "    End If",
            "End Function")),
    ];

    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    // the program run both ways, printing expected: the procedures named compiled, with no statement stepped.
    private static async Task AssertNoneSteppedAsync(string program, string expected, params string[] procedures)
    {
        var compiled = await AssertAlikeAsync(program, Classes, expected);
        foreach (var procedure in procedures)
        {
            Assert.IsTrue(compiled.TryGetValue(procedure, out var result), $"{procedure} was not called");
            Assert.IsNotNull(result.Body, $"{procedure}: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
            Assert.IsEmpty(result.Stepped, $"{procedure}: {string.Join("; ", result.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
        }
    }

    // a key looked up, held and not held: the index of the Collection is what a function of the class returns, and a key it does not hold raises error 5
    // there, which On Error Resume Next goes past, leaving the result Empty.
    [TestMethod]
    public async Task AKeyLookedUp_ByTheTextAFunctionMakesOfIt_IsCompiled()
        => await AssertNoneSteppedAsync(Program(
            "Public Sub Main()",
            "    Dim L As Lookup, I As Long, Found As Long",
            "    Set L = New Lookup",
            "    L.Add \"a\", 1",
            "    L.Add 2, \"two\"",
            "    For I = 1 To 3",
            "        If L.Exists(\"a\") Then Found = Found + 1",
            "        If L.Exists(\"z\") Then Found = Found + 10",
            "        If L.Exists(2) Then Found = Found + 100",
            "    Next",
            "    Debug.Print Found; L.Item(\"a\"); L.Item(2); IsEmpty(L.Item(\"z\"))",
            "End Sub"), expected: "303  1 twoTrue", "GetKeyValue", "Exists");

    // an item of each kind a Collection holds (a number, a String, an array) assigned whole to the Variant result of a function, and to a Variant
    // variable, by a key a function of the module makes: the number written as compiled code writes one, the others assigned as the interpreter assigns them.
    [TestMethod]
    public async Task AnItemOfAnyKind_AssignedWholeToAVariant_IsCompiled()
        => await AssertNoneSteppedAsync(Program(
            "Private Function Named(Key As Variant) As String",
            "    Named = \"k\" & Key",
            "End Function",
            "Private Function Counted(C As Collection, Key As Variant) As Variant",
            "    On Error Resume Next",
            "    Counted = C(Named(Key))",
            "End Function",
            "Private Function Held(C As Collection, Key As Variant) As Boolean",
            "    Dim V As Variant",
            "    On Error Resume Next",
            "    V = C(Named(Key))",
            "    Held = Not IsEmpty(V)",
            "End Function",
            "Public Sub Main()",
            "    Dim C As New Collection, I As Long, Total As Long",
            "    C.Add 5, \"k1\"",
            "    C.Add \"x\", \"k2\"",
            "    C.Add Array(1, 2), \"k3\"",
            "    For I = 1 To 3",
            "        Total = Total + Counted(C, 1)",
            "    Next",
            "    Debug.Print Total; Counted(C, 2); IsArray(Counted(C, 3)); IsEmpty(Counted(C, 4)); Held(C, 1); Held(C, 3); Held(C, 4)",
            "End Sub"), expected: " 15 xTrueTrueTrueTrueFalse", "Counted", "Held");
}
