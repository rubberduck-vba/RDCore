using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// An array parameter is passed by reference, and one declared <c>ByVal</c> is refused: <strong>MS-VBA</strong> reports <em>Array argument must be ByRef</em>
/// for it, whatever declares it - a procedure, a property, an event, a <c>Declare</c>, an implementation of an interface member. Observed in Excel.
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
    [DataRow("Public Sub X(ByVal a() As Long)|End Sub", DisplayName = "a Sub")]
    [DataRow("Public Sub X(ByVal a() As Variant)|End Sub", DisplayName = "of Variant")]
    [DataRow("Public Sub X(ByVal a())|End Sub", DisplayName = "with no As clause")]
    [DataRow("Public Function X(ByVal a() As String) As Long|End Function", DisplayName = "a Function")]
    [DataRow("Public Sub X(Optional ByVal a() As Long)|End Sub", DisplayName = "optional")]
    [DataRow("Public Sub X(ByVal n As Long, ByVal a() As Long)|End Sub", DisplayName = "after a ByVal scalar")]
    [DataRow("Public Declare PtrSafe Sub X Lib \"kernel32\" Alias \"Sleep\" (ByVal a() As Long)", DisplayName = "a Declare")]
    public async Task AByValArrayParameterOfAStandardModule_MustBeByRef(string declarations)
    {
        var errors = await ErrorsOfModuleAsync(declarations.Split('|'));

        Assert.HasCount(1, errors, string.Join(" / ", errors));
        StringAssert.StartsWith(errors[0], "Array argument must be ByRef");
    }

    [TestMethod]
    [DataRow("Public Event E(ByVal a() As Long)", DisplayName = "an Event")]
    [DataRow("Public Sub X(ByVal a() As Long)|End Sub", DisplayName = "a method")]
    [DataRow("Public Property Let P(ByVal a() As Long)|End Property", DisplayName = "a Property Let's value")]
    [DataRow("Public Property Let P(ByVal i As Long, ByVal a() As Long)|End Property", DisplayName = "a Property Let's value, after an index")]
    [DataRow("Implements IArr|Private Sub IArr_X(ByVal a() As Long)|End Sub", DisplayName = "an implementation")]
    public async Task AByValArrayParameterOfAClassModule_MustBeByRef(string declarations)
        => StringAssert.Contains(string.Join(" / ", await ErrorsOfClassAsync(declarations.Split('|'))), "Array argument must be ByRef");

    // A16, B01: ParamArray comes first, and takes neither ByVal nor ByRef.
    [TestMethod]
    [DataRow("Public Sub X(ByVal ParamArray a() As Variant)|End Sub", DisplayName = "ByVal ParamArray")]
    [DataRow("Public Sub X(ByRef ParamArray a() As Variant)|End Sub", DisplayName = "ByRef ParamArray")]
    public async Task APassingModeWrittenBeforeParamArray_IsASyntaxError(string declarations)
    {
        var errors = await ErrorsOfModuleAsync(declarations.Split('|'));

        Assert.HasCount(1, errors, string.Join(" / ", errors));
        StringAssert.StartsWith(errors[0], "Syntax error");
    }

    [TestMethod]
    [DataRow("Public Sub X(ParamArray a() As Variant)|End Sub", DisplayName = "a ParamArray")]
    [DataRow("Public Sub X(ByRef a() As Long)|End Sub", DisplayName = "ByRef")]
    [DataRow("Public Sub X(a() As Long)|End Sub", DisplayName = "implicitly ByRef")]
    [DataRow("Public Sub X(ByVal v As Variant)|End Sub", DisplayName = "a Variant, which may hold an array")]
    public async Task AnArrayParameterThatIsByRef_IsValid(string declarations)
        => Assert.IsEmpty(await ErrorsOfModuleAsync(declarations.Split('|')));
}
