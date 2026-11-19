using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A variable read through the slot it is bound to is the variable of the activation that reads it: its own in each activation of a
/// recursive procedure, the caller's through a ByRef parameter, read as the type it is declared to be through a Variant ByRef parameter, and a Static
/// variable that outlives every activation.
/// </summary>
[TestClass]
public sealed class BoundVariableTests
{
    private static Task<string[]> RunModuleAsync(params string[] lines)
        => RunAsync([], $"Attribute VB_Name = \"Program\"\r\n{string.Join("\r\n", lines)}\r\n");

    [TestMethod]
    public async Task EachActivation_OfARecursiveFunction_ReadsItsOwnVariable()
        => CollectionAssert.AreEqual(new[] { "10" }, await RunModuleAsync(
            "Public Sub Main()",
            "    Debug.Print SumTo(4)",
            "End Sub",
            "Private Function SumTo(ByVal N As Long) As Long",
            "    Dim Here As Long",
            "    Here = N",
            "    If N > 0 Then SumTo = Here + SumTo(N - 1)",
            "End Function"));

    [TestMethod]
    public async Task AByRefParameter_ReadsAndWritesTheCallersVariable()
        => CollectionAssert.AreEqual(new[] { "8", "8" }, await RunModuleAsync(
            "Public Sub Main()",
            "    Dim Value As Long",
            "    Value = 7",
            "    Increment Value",
            "    Debug.Print Value",
            "End Sub",
            "Private Sub Increment(ByRef Target As Long)",
            "    Target = Target + 1",
            "    Debug.Print Target",
            "End Sub"));

    // MS-VBAL 5.3.1.11: a Variant reference parameter bound to a variable of another type reads as the type of the variable, in a Variant.
    [TestMethod]
    public async Task AVariantByRefParameter_ReadsTheCallersVariable_AsItsDeclaredType()
        => CollectionAssert.AreEqual(new[] { "Integer", "7" }, await RunModuleAsync(
            "Public Sub Main()",
            "    Dim Value As Integer",
            "    Value = 7",
            "    Show Value",
            "End Sub",
            "Private Sub Show(ByRef Target As Variant)",
            "    Debug.Print TypeName(Target)",
            "    Debug.Print Target",
            "End Sub"));

    [TestMethod]
    public async Task AStaticVariable_KeepsItsValue_FromOneActivationToTheNext()
        => CollectionAssert.AreEqual(new[] { "1", "2", "3" }, await RunModuleAsync(
            "Public Sub Main()",
            "    Count",
            "    Count",
            "    Count",
            "End Sub",
            "Private Sub Count()",
            "    Static Calls As Long",
            "    Calls = Calls + 1",
            "    Debug.Print Calls",
            "End Sub"));
}
