using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// What a member called late-bound returns is a <c>Variant</c>, whatever the member declares (<strong>MS-VBAL §5.6.12</strong>): an operation of it is a
/// <c>Variant</c> operation, which widens where the declared type's would overflow.
/// </summary>
/// <remarks>
/// Each expected outcome is what MS-VBA printed for the same code, in Excel (64-bit, en-US, oracle\s46 F11), as vb6.exe does (HexIDE findings desk #11).
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.6.12 Member Access Expressions")]
public sealed class LateBoundResultTypeTests
{
    private static readonly (string, string) Foo = ("Foo", ClassModule("Foo", "Public Property Get P() As Integer", "    P = 5", "End Property",
        "Public Function F() As Integer", "    F = 5", "End Function"));

    private static async Task<string> RunAsync(string declared, string expression) => (await ModuleWorkspace.RunAsync([Foo], string.Join("\r\n",
        "Attribute VB_Name = \"Program\"",
        "Public Sub Main()",
        $"    Dim vo As {declared}, x As Variant",
        "    Set vo = New Foo",
        "    On Error Resume Next",
        $"    x = {expression}",
        "    Debug.Print CStr(Err.Number) & \" \" & CStr(x) & \" \" & TypeName(x)",
        "End Sub",
        string.Empty))).Last();

    [TestMethod]
    [DataRow("Variant", "vo.P * 7000", DisplayName = "F11 a property, through a Variant")]
    [DataRow("Object", "vo.P * 7000", DisplayName = "a property, through an Object")]
    [DataRow("Variant", "vo.F * 7000", DisplayName = "a function, through a Variant")]
    public async Task AnIntegerAMemberReturnsLateBound_IsAVariant_WhichWidens(string declared, string expression)
        => Assert.AreEqual("0 35000 Long", await RunAsync(declared, expression));

    // early-bound, the call is of the type the member declares: an Integer product, which overflows.
    [TestMethod]
    public async Task AnIntegerAMemberReturnsEarlyBound_IsAnInteger_WhichOverflows()
        => Assert.AreEqual("6  Empty", await RunAsync("Foo", "vo.P * 7000"));
}
