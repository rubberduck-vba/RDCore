using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// The names MS-VBA accepts a type character on where the library declares no form of the name that has one: <c>Len$</c> and <c>CVar$</c> are <c>Len</c> and
/// <c>CVar</c>, the <c>$</c> ignored (<strong>MS-VBAL §5.6.10</strong> matches a name by its identifier).
/// </summary>
/// <remarks>
/// Each expected outcome is what MS-VBA printed for the same code, in Excel (64-bit, en-US, oracle\s46 F18a, F18b, F19), as vb6.exe does (HexIDE findings desk
/// #18, #19).
/// </remarks>
[TestClass]
[TestCategory("RD-VBAL 6 Standard library")]
public sealed class DollarFormTests
{
    private static async Task<string> PrintedAsync(string expression) => string.Join("|", await ModuleWorkspace.RunAsync([], string.Join("\r\n",
        "Attribute VB_Name = \"Program\"",
        "Public Sub Main()",
        $"    Debug.Print {expression}",
        $"    Debug.Print TypeName({expression})",
        "End Sub",
        string.Empty)));

    [TestMethod]
    [DataRow("Len$(\"abc\")", "3|Long", DisplayName = "F18a Len$ is Len")]
    [DataRow("CVar$(5)", "5|Integer", DisplayName = "F18b CVar$ is CVar")]
    [DataRow("LenB$(\"ab\")", "4|Long", DisplayName = "D02 LenB$")]
    [DataRow("Abs$(-3)", "3|Integer", DisplayName = "D04 Abs$")]
    [DataRow("Fix$(2.5)", "2|Double", DisplayName = "D05 Fix$")]
    [DataRow("Sgn$(-3)", "-1|Integer", DisplayName = "D07 Sgn$")]
    [DataRow("CInt$(2.6)", "3|Integer", DisplayName = "D08 CInt$")]
    [DataRow("CBool$(1)", "True|Boolean", DisplayName = "D11 CBool$")]
    [DataRow("CStr$(1)", "1|String", DisplayName = "D12 CStr$")]
    [DataRow("Len%(\"abc\")", "3|Long", DisplayName = "D16 another character, %")]
    [DataRow("Len&(\"abc\")", "3|Long", DisplayName = "D17 &")]
    [DataRow("CVar%(5)", "5|Integer", DisplayName = "D18 CVar%")]
    public async Task ATypeCharacterOnAReservedFunctionOfTheLibrary_IsIgnored(string expression, string expected)
        => Assert.AreEqual(expected, await PrintedAsync(expression));

    // an ordinary function of the library takes no character it is not declared with (oracle\s44 D14, D15).
    [TestMethod]
    [DataRow("Asc$(\"A\")")]
    [DataRow("Sqr$(4)")]
    public async Task ATypeCharacterOnAnyOtherFunctionOfTheLibrary_IsACompileError(string expression)
    {
        var errors = await LoadErrorsAsync([], $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n    Debug.Print {expression}\r\nEnd Sub\r\n");

        StringAssert.StartsWith(Assert.ContainsSingle(errors), "Type-declaration character does not match declared data type");
    }

    // F19: the parameter of AscW is named String, which a named argument names.
    [TestMethod]
    public async Task AscW_TakesItsArgumentByName()
        => Assert.AreEqual("90|Integer", await PrintedAsync("AscW(String:=\"Z\")"));
}
