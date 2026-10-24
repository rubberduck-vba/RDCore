using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// An array parameter is passed by reference, and one declared <c>ByVal</c> is refused: <strong>MS-VBA</strong> reports <em>Array argument must be ByRef</em>
/// for it, whatever declares it - a procedure, a property, an event, a <c>Declare</c>, an implementation of an interface member. Recorded in Excel (oracle\s23).
/// </summary>
[TestClass]
public sealed class ByValArrayParameterTests
{
    private static readonly (string Name, string Source) Interface = ("IArr", ClassModule("IArr", "Option Explicit", "Public Sub X(a() As Long)", "End Sub"));

    private const string Program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\nEnd Sub\r\n";

    private static Task<string[]> ErrorsOfModuleAsync(params string[] declarations)
        => LoadErrorsAsync([Interface], [("Decls", StandardModule("Decls", ["Option Explicit", .. declarations]))], Program);

    private static Task<string[]> ErrorsOfClassAsync(params string[] declarations)
        => LoadErrorsAsync([Interface, ("Holder", ClassModule("Holder", ["Option Explicit", .. declarations]))], [], Program);

    [TestMethod]
    [DataRow("Public Sub X(ByVal a() As Long)|End Sub", DisplayName = "A01 a Sub")]
    [DataRow("Public Sub X(ByVal a() As Variant)|End Sub", DisplayName = "A03 of Variant")]
    [DataRow("Public Sub X(ByVal a())|End Sub", DisplayName = "A04 with no As clause")]
    [DataRow("Public Function X(ByVal a() As String) As Long|End Function", DisplayName = "A05 a Function")]
    [DataRow("Public Sub X(Optional ByVal a() As Long)|End Sub", DisplayName = "A06 optional")]
    [DataRow("Public Sub X(ByVal n As Long, ByVal a() As Long)|End Sub", DisplayName = "A07 after a ByVal scalar")]
    [DataRow("Public Declare PtrSafe Sub X Lib \"kernel32\" Alias \"Sleep\" (ByVal a() As Long)", DisplayName = "A08 a Declare")]
    public async Task AByValArrayParameterOfAStandardModule_MustBeByRef(string declarations)
    {
        var errors = await ErrorsOfModuleAsync(declarations.Split('|'));

        Assert.HasCount(1, errors, string.Join(" / ", errors));
        StringAssert.StartsWith(errors[0], "Array argument must be ByRef");
    }

    [TestMethod]
    [DataRow("Public Event E(ByVal a() As Long)", DisplayName = "A09 an Event")]
    [DataRow("Public Sub X(ByVal a() As Long)|End Sub", DisplayName = "A10 a method")]
    [DataRow("Public Property Let P(ByVal a() As Long)|End Property", DisplayName = "A11 a Property Let's value")]
    [DataRow("Public Property Let P(ByVal i As Long, ByVal a() As Long)|End Property", DisplayName = "A12 a Property Let's value, after an index")]
    [DataRow("Implements IArr|Private Sub IArr_X(ByVal a() As Long)|End Sub", DisplayName = "A18 an implementation")]
    public async Task AByValArrayParameterOfAClassModule_MustBeByRef(string declarations)
        => StringAssert.Contains(string.Join(" / ", await ErrorsOfClassAsync(declarations.Split('|'))), "Array argument must be ByRef");

    // A16, B01: ParamArray comes first, and takes neither ByVal nor ByRef.
    [TestMethod]
    [DataRow("Public Sub X(ByVal ParamArray a() As Variant)|End Sub", DisplayName = "A16 ByVal ParamArray")]
    [DataRow("Public Sub X(ByRef ParamArray a() As Variant)|End Sub", DisplayName = "B01 ByRef ParamArray")]
    public async Task APassingModeWrittenBeforeParamArray_IsASyntaxError(string declarations)
    {
        var errors = await ErrorsOfModuleAsync(declarations.Split('|'));

        Assert.HasCount(1, errors, string.Join(" / ", errors));
        StringAssert.StartsWith(errors[0], "Syntax error");
    }

    [TestMethod]
    [DataRow("Public Sub X(ParamArray a() As Variant)|End Sub", DisplayName = "B04 a ParamArray")]
    [DataRow("Public Sub X(ByRef a() As Long)|End Sub", DisplayName = "A13 ByRef")]
    [DataRow("Public Sub X(a() As Long)|End Sub", DisplayName = "A14 implicitly ByRef")]
    [DataRow("Public Sub X(ByVal v As Variant)|End Sub", DisplayName = "A15 a Variant, which may hold an array")]
    public async Task AnArrayParameterThatIsByRef_IsValid(string declarations)
        => Assert.IsEmpty(await ErrorsOfModuleAsync(declarations.Split('|')));
}
