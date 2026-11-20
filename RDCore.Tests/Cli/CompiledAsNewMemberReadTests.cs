using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A public variable of an object variable declared <c>As New</c>, read and assigned, a <c>Property Let</c> of one, and the members of a
/// <c>Collection</c> compiled code calls on one: compiled code reads and assigns them on the object the variable holds. While the variable holds
/// <c>Nothing</c>, the interpreter reads it, which makes the object (MS-VBAL §5.2.3.1.4). A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledAsNewMemberReadTests
{
    // a class whose Initialize prints, with a public variable of each kind and a property.
    private static readonly (string Name, string Source)[] Classes =
    [
        ("Tally", ClassModule("Tally",
            "Option Explicit",
            "Public Total As Long",
            "Public Name As String",
            "Private m As Long",
            "Private Sub Class_Initialize()",
            "    Debug.Print \"made\";",
            "End Sub",
            "Public Property Get Size() As Long",
            "    Size = m",
            "End Property",
            "Public Property Let Size(ByVal N As Long)",
            "    m = N",
            "End Property")),
    ];

    // the program run both ways: the procedure compiled, with no statement stepped or made a support call.
    private static async Task AssertNoneSteppedAsync(string procedure, string expected, params string[] lines)
    {
        var compiled = await AssertAlikeAsync(Program(lines), Classes, expected);
        Assert.IsTrue(compiled.TryGetValue(procedure, out var result), $"{procedure} was not called");
        Assert.IsNotNull(result.Body, $"{procedure}: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
        Assert.IsEmpty(result.Stepped, $"{procedure} steps: {string.Join("; ", result.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
        Assert.IsEmpty(result.SupportCalls,
            $"{procedure} support calls: {string.Join("; ", result.SupportCalls.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
    }

    // a scalar public variable of a local As New, read and assigned: the first read makes the object.
    [TestMethod]
    public async Task AScalarPublicVariableOfALocalAsNew_IsReadAndAssignedByCompiledCode()
        => await AssertNoneSteppedAsync("Summed", "made 10 ",
            "Public Sub Main()",
            "    Debug.Print Summed()",
            "End Sub",
            "Private Function Summed() As Long",
            "    Dim M As New Tally",
            "    Dim I As Long",
            "    For I = 1 To 3",
            "        M.Total = M.Total + I",
            "        Summed = Summed + M.Total",
            "    Next",
            "End Function");

    // a String public variable of a local As New, read.
    [TestMethod]
    public async Task AStringPublicVariableOfALocalAsNew_IsReadByCompiledCode()
        => await AssertNoneSteppedAsync("Named", "madeabc",
            "Public Sub Main()",
            "    Debug.Print Named()",
            "End Sub",
            "Private Function Named() As String",
            "    Dim M As New Tally",
            "    Named = M.Name & \"abc\"",
            "End Function");

    // a Property Let of a local As New: the first assignment makes the object.
    [TestMethod]
    public async Task APropertyLetOfALocalAsNew_IsCalledByCompiledCode()
        => await AssertNoneSteppedAsync("Sized", "made 6 ",
            "Public Sub Main()",
            "    Debug.Print Sized()",
            "End Sub",
            "Private Function Sized() As Long",
            "    Dim M As New Tally",
            "    Dim I As Long",
            "    For I = 1 To 3",
            "        M.Size = I",
            "        Sized = Sized + M.Size",
            "    Next",
            "End Function");

    // a public variable of a variable of the module declared As New, made by the first read of the first activation.
    [TestMethod]
    public async Task APublicVariableOfAModuleVariableAsNew_IsReadByCompiledCode()
        => await AssertNoneSteppedAsync("Counted", "made 1  2 ",
            "Private Kept As New Tally",
            "Public Sub Main()",
            "    Debug.Print Counted(); Counted()",
            "End Sub",
            "Private Function Counted() As Long",
            "    Kept.Total = Kept.Total + 1",
            "    Counted = Kept.Total",
            "    If Counted > 1 Then Kept.Total = Kept.Total + 1",
            "End Function");

    // Count and Item, the default member, of a Collection of the module declared As New (MS-VBAL §6.1.3.1.1.1, §6.1.3.1.1.2): the first Count makes it.
    [TestMethod]
    public async Task TheMembersOfACollectionAsNew_AreCalledByCompiledCode()
        => await AssertNoneSteppedAsync("Collected", " 0  6 ",
            "Private Bag As New Collection",
            "Public Sub Main()",
            "    Debug.Print Collected();",
            "    Bag.Add 2",
            "    Bag.Add 4",
            "    Debug.Print Collected()",
            "End Sub",
            "Private Function Collected() As Long",
            "    Collected = Bag.Count",
            "    If Collected > 1 Then Collected = Collected + Bag(2)",
            "End Function");
}
