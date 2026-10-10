using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Variant</c> parameter passed by reference - which a parameter declared with neither is - given a variable of another type is bound to that variable
/// (<strong>MS-VBAL §5.3.1.11</strong>): it "is treated as having a declared type of Variant, except when used as the &lt;l-expression&gt; within Let-assignment or
/// Set-assignment, in which case it is treated as having the declared type of the argument's referenced variable".
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.3.1.11 Procedure Invocation Argument Processing")]
public sealed class ByRefVariantParameterTests
{
    private static Task<string[]> Run(string procedures, params string[] lines)
        => RunAsync([], $"Attribute VB_Name = \"Program\"\r\n{procedures}\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n");

    [TestMethod]
    public async Task AParameterDeclaredWithNeitherByRefNorAType_ReadsTheVariableItIsGiven()
        => CollectionAssert.AreEqual(new[] { "6" }, await Run(
            "Public Sub Show(v)\r\nDebug.Print v + 1\r\nEnd Sub",
            "Dim i As Long",
            "i = 5",
            "Show i"));

    [TestMethod]
    public async Task AValueAssignedToIt_IsLetCoercedToTheVariablesDeclaredType()
        // a Long rounds to even: what a Variant would have held, 2.5, is not what the variable holds.
        => CollectionAssert.AreEqual(new[] { "2" }, await Run(
            "Public Sub Assign(ByRef v As Variant)\r\nv = 2.5\r\nEnd Sub",
            "Dim i As Long",
            "Assign i",
            "Debug.Print i"));

    [TestMethod]
    public async Task AValueTheVariablesTypeCannotHold_IsATypeMismatch()
        => CollectionAssert.AreEqual(new[] { "13", "5" }, await Run(
            "Public Sub Assign(v)\r\nv = \"duck\"\r\nEnd Sub",
            "Dim i As Long",
            "i = 5",
            "On Error Resume Next",
            "Assign i",
            "Debug.Print Err.Number",
            "Debug.Print i"));

    [TestMethod]
    public async Task AStringVariable_IsReadAndAssignedAsTheStringItIs()
        => CollectionAssert.AreEqual(new[] { "quack!" }, await Run(
            "Public Sub Shout(v)\r\nv = v & \"!\"\r\nEnd Sub",
            "Dim s As String",
            "s = \"quack\"",
            "Shout s",
            "Debug.Print s"));

    [TestMethod]
    public async Task AParameterHandedOnByReference_IsStillTheVariable_OfItsDeclaredType()
        => CollectionAssert.AreEqual(new[] { "8" }, await Run(
            "Public Sub Outer(v)\r\nInner v\r\nEnd Sub\r\nPublic Sub Inner(w)\r\nw = 7.9\r\nEnd Sub",
            "Dim i As Long",
            "Outer i",
            "Debug.Print i"));

    [TestMethod]
    public async Task AnObjectVariable_IsSetAssignedAsTheObjectVariableItIs()
        => CollectionAssert.AreEqual(new[] { "False", "True" }, await Run(
            "Public Sub Forget(v)\r\nDebug.Print v Is Nothing\r\nSet v = Nothing\r\nEnd Sub",
            "Dim c As Collection",
            "Set c = New Collection",
            "Forget c",
            "Debug.Print c Is Nothing"));

    [TestMethod]
    public async Task AVariantVariable_IsAVariantStill_AndHoldsWhateverIsAssigned()
        => CollectionAssert.AreEqual(new[] { "2.5" }, await Run(
            "Public Sub Assign(v)\r\nv = 2.5\r\nEnd Sub",
            "Dim x As Variant",
            "x = 1",
            "Assign x",
            "Debug.Print x"));
}
