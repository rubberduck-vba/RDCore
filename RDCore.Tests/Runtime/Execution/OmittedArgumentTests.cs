using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// What an optional parameter takes when the call supplies no argument for it (<strong>MS-VBAL §5.3.1.11</strong>): a default other than
/// <c>Nothing</c> Let-assigned to the parameter, a default of <c>Nothing</c> Set-assigned to it, and with no default its type's own value.
/// </summary>
/// <remarks>
/// Each row runs a workspace the way the platform does - the language server's composition, then the host's session - since the omitted
/// value is filled in at three places that have to agree: a call's argument binding, the call a Let-coercion makes to an object's default
/// member, and the declared defaults that <c>VarType</c> and the <c>Is</c> functions call a default member with.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.3.1.11 Procedure Invocation Argument Processing")]
public sealed class OmittedArgumentTests
{
    private static (string Name, string Source) DefaultMember(string name, string parameter, string body)
        => (name, ClassModule(name,
            $"Public Function Value({parameter}) As Variant",
            "Attribute Value.VB_UserMemId = 0",
            $"    {body}",
            "End Function"));

    private static readonly IReadOnlyList<(string Name, string Source)> Classes =
    [
        DefaultMember("DOptVar", "Optional ByVal v As Variant = 5", "Value = TypeName(v) & \" \" & v"),
        DefaultMember("DOptLong", "Optional ByVal n As Long = 7", "Value = TypeName(n) & \" \" & n"),
        DefaultMember("DOptByte", "Optional ByVal b As Byte = 300", "Value = TypeName(b) & \" \" & b"),
        DefaultMember("DOptNothing", "Optional v As Variant = Nothing", "Value = TypeName(v)"),
        // what VarType reads: an Integer when the parameter holds an object, a String when it does not.
        DefaultMember("DIsObject", "Optional v As Variant = Nothing", "If IsObject(v) Then Value = 1 Else Value = \"not an object\""),
    ];

    private static Task<string[]> RunProgramAsync(string declarations, params string[] body)
        => RunAsync(Classes,
            $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n{declarations}\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n");

    #region a default member called with no arguments

    [TestMethod]
    public async Task ADefaultMembersVariantDefault_IsHeldAsAVariant()
        // an InvalidCastException out of the host when the folded Integer reached a Variant parameter as it was.
        => CollectionAssert.AreEqual(new[] { "Integer 5" }, await RunProgramAsync(string.Empty, "Dim o As Object", "Set o = New DOptVar", "Dim s As String", "s = o", "Debug.Print s"));

    [TestMethod]
    public async Task ADefaultMembersTypedDefault_IsLetCoercedToItsType()
        => CollectionAssert.AreEqual(new[] { "Long 7" }, await RunProgramAsync(string.Empty, "Dim o As Object", "Set o = New DOptLong", "Dim s As String", "s = o", "Debug.Print s"));

    [TestMethod]
    public async Task ADefaultMembersDefault_ThatOverflowsItsType_IsAnOverflow_AndTheMemberIsNotCalled()
        // what a call that omits it raises, where the member used to be called with a Byte of 300.
        => CollectionAssert.AreEqual(new[] { "|err 6" }, await RunProgramAsync(string.Empty,
            "On Error Resume Next", "Dim o As Object", "Set o = New DOptByte", "Dim s As String", "s = o", "Debug.Print s & \"|err \" & Err.Number"));

    [TestMethod]
    public async Task ADefaultMembersNothingDefault_IsAVariantHoldingNothing()
        => CollectionAssert.AreEqual(new[] { "Nothing" }, await RunProgramAsync(string.Empty, "Dim o As Object", "Set o = New DOptNothing", "Dim s As String", "s = o", "Debug.Print s"));

    [TestMethod]
    public async Task ADefaultMembersNothingDefault_IsNothing_ToTheFunctionsThatReadAnObjectsValue()
        // VarType calls the default member with the declared defaults a variable starts as, which coerced Nothing, failed, and fell back to Empty.
        => CollectionAssert.AreEqual(new[] { "2" }, await RunProgramAsync(string.Empty, "Dim o As Object", "Set o = New DIsObject", "Debug.Print VarType(o)"));

    #endregion

    #region a call that omits the argument

    [TestMethod]
    [DataRow("Optional o As Object = Nothing", "Debug.Print o Is Nothing", "True", DisplayName = "Object")]
    [DataRow("Optional c As Collection = Nothing", "Debug.Print c Is Nothing", "True", DisplayName = "a class")]
    [DataRow("Optional v As Variant = Nothing", "Debug.Print TypeName(v) & \" \" & IsObject(v)", "Nothing True", DisplayName = "Variant")]
    public async Task ANothingDefault_IsSetAssigned_NotLetCoerced(string parameter, string statement, string expected)
        // Let-coercing Nothing asks it for its default member, which is error 91.
        => CollectionAssert.AreEqual(new[] { expected, "err 0" }, await RunProgramAsync(
            $"Private Sub N({parameter})\r\n    {statement}\r\nEnd Sub", "On Error Resume Next", "N", "Debug.Print \"err \" & Err.Number"));

    [TestMethod]
    [DataRow("Optional ByVal v As Variant = 3", DisplayName = "ByVal")]
    [DataRow("Optional v As Variant = 3", DisplayName = "ByRef")]
    public async Task AVariantDefault_IsHeldAsAVariant_AndIsNotMissing(string parameter)
        => CollectionAssert.AreEqual(new[] { "False Integer 3" }, await RunProgramAsync(
            $"Private Sub S({parameter})\r\n    Debug.Print IsMissing(v) & \" \" & TypeName(v) & \" \" & v\r\nEnd Sub", "S"));

    [TestMethod]
    [DataRow("Optional k As Long = 5", DisplayName = "ByRef")]
    [DataRow("Optional ByVal k As Long = 5", DisplayName = "ByVal")]
    public async Task ATypedDefault_IsLetCoercedToItsType(string parameter)
        // an Integer 5 would overflow at 500000.
        => CollectionAssert.AreEqual(new[] { "500000" }, await RunProgramAsync(
            $"Private Function F({parameter}) As Long\r\n    F = k * 100000\r\nEnd Function", "Debug.Print F()"));

    [TestMethod]
    [DataRow("Optional b As Byte = 300", DisplayName = "ByRef")]
    [DataRow("Optional ByVal b As Byte = 300", DisplayName = "ByVal")]
    public async Task ADefaultThatOverflowsItsType_IsAnOverflow(string parameter)
        => CollectionAssert.AreEqual(new[] { "err 6" }, await RunProgramAsync(
            $"Private Sub S({parameter})\r\n    Debug.Print b\r\nEnd Sub", "On Error Resume Next", "S", "Debug.Print \"err \" & Err.Number"));

    #endregion
}
