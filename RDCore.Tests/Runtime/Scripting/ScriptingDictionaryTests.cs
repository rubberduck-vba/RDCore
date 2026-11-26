using RDCore.SDK.Runtime.Libraries;
using RDCore.Tests.Cli;
using System.IO.Abstractions;

namespace RDCore.Tests.Runtime.Scripting;

/// <summary>
/// The Scripting library's <c>Dictionary</c>, as scrrun.dll has it (measured over COM on Windows): made by <c>New</c> and by <c>CreateObject</c>, with or
/// without a reference to the library, its members early and late bound, how it compares its keys, the errors it raises, and the objects it holds.
/// </summary>
/// <remarks>
/// The dictionary is the platform's own on every platform: the machine of these tests has no automation server, as one that is not Windows has none.
/// </remarks>
[TestClass]
public sealed class ScriptingDictionaryTests
{
    private static ModuleWorkspace.WorkspaceLibraries Referenced => new(
        ["Scripting"], new DirectoryLibrarySource(new FileSystem(), RDCore.Tests.Runtime.Libraries.ShippedLibrariesTests.Symbols),
        RDCore.External.Automation.UnavailableAutomationServer.Instance);

    private static ModuleWorkspace.WorkspaceLibraries Unreferenced => Referenced with { References = [] };

    private static readonly (string Name, string Source)[] Classes =
    [
        ("Widget", ModuleWorkspace.ClassModule("Widget",
            "Option Explicit",
            "Private Sub Class_Terminate()",
            "    Debug.Print \"terminated\"",
            "End Sub")),
        ("Defaulted", ModuleWorkspace.ClassModule("Defaulted",
            "Option Explicit",
            "Public Property Get Value() As Long",
            "Attribute Value.VB_UserMemId = 0",
            "    Value = 42",
            "End Property")),
    ];

    private static Task<string[]> RunAsync(ModuleWorkspace.WorkspaceLibraries libraries, params string[] lines)
        => ModuleWorkspace.RunAsync(Classes, Source(lines), libraries);

    private static string Source(string[] lines) => string.Join("\r\n",
    [
        "Attribute VB_Name = \"Program\"",
        "Option Explicit",
        "Public Sub Main()",
        "    Dim o As Object, v As Variant, k As Variant",
        .. lines.Select(line => "    " + line),
        "End Sub",
        "",
    ]);

    // what the program printed, all of it in the message when it is not what was expected.
    private static void Printed(string[] expected, string[] printed)
        => Assert.IsTrue(expected.SequenceEqual(printed), $"printed [{string.Join(" | ", printed)}], expected [{string.Join(" | ", expected)}]");

    // the members a program commonly uses, the same whether the dictionary is bound early or late: o is declared As Object, d As the library's class.
    private static readonly string[] Members =
    [
        "x.Add \"a\", 10",
        "x.Add \"b\", 20",
        "x(\"c\") = 30",
        "Debug.Print x.Count & \" \" & x(\"a\") & \" \" & x.Item(\"b\") & \" \" & x.Exists(\"c\") & \" \" & x.Exists(\"z\")",
        "Debug.Print Join(x.Keys, \",\") & \" \" & Join(x.Items, \",\")",
        "x.Remove \"a\"",
        "x.Key(\"b\") = \"B\"",
        "Debug.Print Join(x.Keys, \",\") & \" \" & TypeName(x)",
        "x.RemoveAll",
        "Debug.Print x.Count",
    ];

    private static readonly string[] MembersPrinted = ["3 10 20 True False", "a,b,c 10,20,30", "B,c Dictionary", "0"];

    [TestMethod]
    public async Task ADictionaryMadeByNew_WithTheReference_RunsItsMembersEarlyBound()
        => Printed(MembersPrinted, await RunAsync(Referenced,
            ["Dim x As Scripting.Dictionary", "Set x = New Scripting.Dictionary", .. Members]));

    [TestMethod]
    public async Task ADictionaryMadeByNew_WithTheReference_RunsItsMembersLateBound()
        => Printed(MembersPrinted, await RunAsync(Referenced,
            ["Dim x As Object", "Set x = New Scripting.Dictionary", .. Members]));

    [TestMethod]
    public async Task ADictionaryMadeByCreateObject_WithTheReference_IsAnInstanceOfTheLibrarysClass()
        => Printed(MembersPrinted, await RunAsync(Referenced,
            ["Dim x As Scripting.Dictionary", "Set x = CreateObject(\"Scripting.Dictionary\")", .. Members]));

    [TestMethod]
    public async Task ADictionaryMadeByCreateObject_WithNoReference_RunsItsMembersByName()
        => Printed(MembersPrinted, await RunAsync(Unreferenced,
            ["Dim x As Object", "Set x = CreateObject(\"scripting.dictionary\")", .. Members]));

    [TestMethod]
    public async Task ADictionary_IsMade_InAnEnvironmentThatAllowsNoAutomation()
        => Printed(["1"], await ModuleWorkspace.RunAsync(Classes, Source(
            ["Set o = CreateObject(\"Scripting.Dictionary\")", "o.Add 1, 1", "Debug.Print o.Count"]), Unreferenced with { AllowAutomation = false }));

    [TestMethod]
    [DataRow(true, DisplayName = "with the reference")]
    [DataRow(false, DisplayName = "with no reference")]
    public async Task ForEach_OverADictionary_VisitsItsKeys_InTheOrderTheyWereAdded(bool referenced)
        => Printed(["b", "a", "c"], await RunAsync(referenced ? Referenced : Unreferenced,
            "Set o = CreateObject(\"Scripting.Dictionary\")",
            "o.Add \"b\", 1: o.Add \"a\", 2: o.Add \"c\", 3",
            "For Each k In o",
            "    Debug.Print k",
            "Next"));

    [TestMethod]
    public async Task ForEach_OverAnEarlyBoundDictionary_VisitsItsKeys_AndOverItsKeysAndItems_ItsArrays()
        => Printed(["a", "b", "a", "b", "1", "2"], await RunAsync(Referenced,
            "Dim d As New Scripting.Dictionary",
            "d.Add \"a\", 1: d.Add \"b\", 2",
            "For Each v In d",
            "    Debug.Print v",
            "Next",
            "For Each v In d.Keys",
            "    Debug.Print v",
            "Next",
            "For Each v In d.Items",
            "    Debug.Print v",
            "Next"));

    // the key and the item are Variants passed by reference, given typed variables: the dictionary holds their values as the types they are declared with
    // (MS-VBAL §5.3.1.11), an enumeration's a Long.
    [TestMethod]
    public async Task TypedVariablesPassedByReference_AreHeldAsTheTypesTheyAreDeclaredWith()
        => Printed(["3 6 Long", "Long String"], await RunAsync(Referenced,
            "Dim d As New Scripting.Dictionary",
            "Dim i As Long, s As Long",
            "For i = 1 To 3",
            "    d.Add i, i",
            "Next",
            "For i = 1 To 3",
            "    s = s + d.Item(i)",
            "Next",
            "k = d.Keys",
            "Debug.Print d.Count & \" \" & s & \" \" & TypeName(k(0))",
            "Dim e As VbCompareMethod, t As String",
            "e = vbTextCompare: t = \"text\"",
            "d.Add \"e\", e: d.Add \"t\", t",
            "Debug.Print TypeName(d(\"e\")) & \" \" & TypeName(d(\"t\"))"));

    // numbers of any type are one key when their values are equal, to the digits a Decimal keeps of a Single (7) and of a Double (15).
    [TestMethod]
    [DataRow("CInt(1)", "CDbl(1)", true)]
    [DataRow("CLng(1)", "CCur(1)", true)]
    [DataRow("CByte(1)", "CDec(1)", true)]
    [DataRow("CSng(0.1)", "CDbl(0.1)", true)]
    [DataRow("CDbl(1.5)", "CSng(1.5)", true)]
    [DataRow("CDbl(0.1)", "CDbl(0.10000000001)", false)]
    [DataRow("CLng(16777216)", "CLng(16777217)", false)]
    [DataRow("CDbl(1E+20)", "CDec(\"100000000000000000000\")", true)]
    [DataRow("CDate(1)", "1", true)]
    [DataRow("True", "-1", true)]
    [DataRow("True", "1", false)]
    [DataRow("\"1\"", "1", false)]
    [DataRow("Empty", "\"\"", true)]
    [DataRow("Null", "Null", true)]
    [DataRow("Null", "\"\"", false)]
    public async Task TwoValues_AreOneKey_WhenScrrunComparesThemEqual(string added, string looked, bool same)
        => Printed([$"{same} {(same ? 1 : 2)}"], await RunAsync(Unreferenced,
            "Set o = CreateObject(\"Scripting.Dictionary\")",
            $"o.Add {added}, 1",
            $"v = o.Exists({looked})",
            "On Error Resume Next",
            $"o.Add {looked}, 2",
            "Debug.Print v & \" \" & o.Count"));

    [TestMethod]
    public async Task AKey_IsGivenBackWithItsOwnType()
        => Printed(["Integer Double String Date Boolean"], await RunAsync(Unreferenced,
            "Set o = CreateObject(\"Scripting.Dictionary\")",
            "o.Add CInt(1), 0: o.Add 2.5, 0: o.Add \"s\", 0: o.Add #1/2/2000#, 0: o.Add False, 0",
            "k = o.Keys",
            "Debug.Print TypeName(k(0)) & \" \" & TypeName(k(1)) & \" \" & TypeName(k(2)) & \" \" & TypeName(k(3)) & \" \" & TypeName(k(4))"));

    // an error value is a Variant like any other: an item, an element of Items, and a key For Each visits.
    [TestMethod]
    public async Task AnErrorValue_IsHeld_AsAnItemAndAsAKey()
        => Printed(["True 10", "True False", "True", "False"], await RunAsync(Unreferenced,
            "Set o = CreateObject(\"Scripting.Dictionary\")",
            "o.Add \"k\", CVErr(2007): o.Add \"b\", 1",
            "Debug.Print IsError(o(\"k\")) & \" \" & VarType(o(\"k\"))",
            "v = o.Items",
            "Debug.Print IsError(v(0)) & \" \" & IsError(v(1))",
            "Set o = CreateObject(\"Scripting.Dictionary\")",
            "o.Add CVErr(2042), 1: o.Add \"b\", 2",
            "For Each k In o",
            "    Debug.Print IsError(k)",
            "Next"));

    [TestMethod]
    public async Task WhatADictionaryRefuses_IsTheErrorScrrunRaises()
        => Printed(["457", "32811", "32811", "457", "5", "5", "5"], await RunAsync(Unreferenced,
            "Set o = CreateObject(\"Scripting.Dictionary\")",
            "o.Add \"a\", 1: o.Add \"b\", 2",
            "On Error Resume Next",
            "o.Add \"a\", 3: Debug.Print Err.Number: Err.Clear",
            "o.Remove \"z\": Debug.Print Err.Number: Err.Clear",
            "o.Key(\"z\") = \"y\": Debug.Print Err.Number: Err.Clear",
            "o.Key(\"a\") = \"b\": Debug.Print Err.Number: Err.Clear",
            "o.Add Array(1, 2), 3: Debug.Print Err.Number: Err.Clear",
            "o.CompareMode = 1: Debug.Print Err.Number: Err.Clear",
            "Set o = CreateObject(\"Scripting.Dictionary\")",
            "o.CompareMode = -1: Debug.Print Err.Number: Err.Clear"));

    [TestMethod]
    public async Task TheDuplicateKeysError_HasTheHelpContextOfItsMessage_AndADescription()
        => Printed(["457 1000457 True"], await RunAsync(Unreferenced,
            "Set o = CreateObject(\"Scripting.Dictionary\")",
            "o.Add 1, 1",
            "On Error Resume Next",
            "o.Add 1, 1",
            "Debug.Print Err.Number & \" \" & Err.HelpContext & \" \" & (Len(Err.Description) > 0)"));

    [TestMethod]
    [DataRow(0, "False 2")]
    [DataRow(1, "True 1")]
    public async Task CompareMode_DecidesWhetherKeysThatDifferInCaseAreOne(int mode, string printed)
        => Printed([$"{mode} {printed}"], await RunAsync(Unreferenced,
            "Set o = CreateObject(\"Scripting.Dictionary\")",
            $"o.CompareMode = {mode}",
            "o.Add \"A\", 1",
            "v = o.Exists(\"a\")",
            "On Error Resume Next",
            "o.Add \"a\", 2",
            "Debug.Print o.CompareMode & \" \" & v & \" \" & o.Count"));

    [TestMethod]
    public async Task ReadingTheItemOfAKeyThatIsNotThere_AddsTheKey_HoldingEmpty()
        => Printed(["True 1 True", "2 5"], await RunAsync(Unreferenced,
            "Set o = CreateObject(\"Scripting.Dictionary\")",
            "v = o(\"new\")",
            "Debug.Print IsEmpty(v) & \" \" & o.Count & \" \" & o.Exists(\"new\")",
            "o(\"other\") = 5",
            "Debug.Print o.Count & \" \" & o(\"other\")"));

    [TestMethod]
    public async Task AKeyRemovedAndAddedAgain_GoesLast_AndARenamedKeyKeepsItsPlace()
        => Printed(["y,x", "z,x"], await RunAsync(Unreferenced,
            "Set o = CreateObject(\"Scripting.Dictionary\")",
            "o.Add \"x\", 1: o.Add \"y\", 2: o.Remove \"x\": o.Add \"x\", 3",
            "Debug.Print Join(o.Keys, \",\")",
            "o.Key(\"y\") = \"z\"",
            "Debug.Print Join(o.Keys, \",\")"));

    [TestMethod]
    public async Task TheKeysAndItemsOfAnEmptyDictionary_AreAnArrayWithNoElement()
        => Printed(["0 -1 0 -1 8204"], await RunAsync(Unreferenced,
            "Set o = CreateObject(\"Scripting.Dictionary\")",
            "Debug.Print LBound(o.Keys) & \" \" & UBound(o.Keys) & \" \" & LBound(o.Items) & \" \" & UBound(o.Items) & \" \" & VarType(o.Keys)"));

    // the Keys and Items arrays are copies: what the program does to one is not the dictionary's.
    [TestMethod]
    public async Task TheKeysArray_IsACopyOfTheKeys()
        => Printed(["changed a"], await RunAsync(Unreferenced,
            "Set o = CreateObject(\"Scripting.Dictionary\")",
            "o.Add \"a\", 1",
            "k = o.Keys",
            "k(0) = \"changed\"",
            "v = o.Keys",
            "Debug.Print k(0) & \" \" & v(0)"));

    [TestMethod]
    public async Task AnObjectItem_IsTheObject_AndTheDictionaryHoldsItUntilItIsRemoved()
        => Printed(["True Widget", "removing", "terminated", "removed"], await RunAsync(Unreferenced,
            "Set o = CreateObject(\"Scripting.Dictionary\")",
            "Dim w As Widget",
            "Set w = New Widget",
            "o.Add \"w\", w",
            "Debug.Print (o(\"w\") Is w) & \" \" & TypeName(o(\"w\"))",
            "Set w = Nothing",
            "Debug.Print \"removing\"",
            "o.Remove \"w\"",
            "Debug.Print \"removed\""));

    [TestMethod]
    public async Task ADictionaryThatIsDestroyed_LetsGoOfTheObjectsItHolds()
        => Printed(["dropping", "terminated", "dropped"], await RunAsync(Unreferenced,
            "Set o = CreateObject(\"Scripting.Dictionary\")",
            "o.Add \"w\", New Widget",
            "Debug.Print \"dropping\"",
            "Set o = Nothing",
            "Debug.Print \"dropped\""));

    [TestMethod]
    public async Task AnObjectKey_IsTheObject_WhateverItsDefaultMember()
        => Printed(["True False 2"], await RunAsync(Unreferenced,
            "Set o = CreateObject(\"Scripting.Dictionary\")",
            "Dim a As Defaulted, b As Defaulted",
            "Set a = New Defaulted: Set b = New Defaulted",
            "o.Add a, 1",
            "o.Add b, 2",
            "Debug.Print o.Exists(a) & \" \" & o.Exists(42) & \" \" & o(b)"));

    [TestMethod]
    public async Task AnObjectSetToAnItem_IsHeld_AndAnObjectLetToOne_IsItsDefaultMembersValue()
        => Printed(["True", "Long False", "91", "450", "terminated"], await RunAsync(Unreferenced,
            "Set o = CreateObject(\"Scripting.Dictionary\")",
            "Dim w As New Widget",
            "Set o(\"w\") = w",
            "Debug.Print o(\"w\") Is w",
            "o(\"d\") = New Defaulted",
            "Debug.Print TypeName(o(\"d\")) & \" \" & IsObject(o(\"d\"))",
            "On Error Resume Next",
            "o(\"n\") = Nothing: Debug.Print Err.Number: Err.Clear",
            "o(\"c\") = New Collection: Debug.Print Err.Number: Err.Clear"));

    [TestMethod]
    public async Task ANamedArgument_ReachesTheParameterOfItsName_AndANameTheMemberHasNot_Is448()
        => Printed(["a,b,c 1,2,3", "448"], await RunAsync(Unreferenced,
            "Set o = CreateObject(\"Scripting.Dictionary\")",
            "o.Add Key:=\"a\", Item:=1",
            "o.Add Item:=2, Key:=\"b\"",
            "o.Add \"c\", Item:=3",
            "Debug.Print Join(o.Keys, \",\") & \" \" & Join(o.Items, \",\")",
            "On Error Resume Next",
            "o.Add Nope:=\"d\", Item:=4: Debug.Print Err.Number: Err.Clear"));

    // a late-bound call's arguments are what IDispatch checks: too few or too many, a name the object has no member of.
    [TestMethod]
    public async Task ALateBoundCall_TheDictionaryCannotTake_IsTheErrorMSVBARaises()
        => Printed(["450", "450", "438", "450"], await RunAsync(Unreferenced,
            "Set o = CreateObject(\"Scripting.Dictionary\")",
            "On Error Resume Next",
            "o.Add \"a\": Debug.Print Err.Number: Err.Clear",
            "o.Add \"a\", 1, 2: Debug.Print Err.Number: Err.Clear",
            "o.Nope: Debug.Print Err.Number: Err.Clear",
            "v = o: Debug.Print Err.Number: Err.Clear"));
}
