using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Variant</c> holding a number, a <c>Date</c> or a <c>Boolean</c>, compared with a <c>String</c> that is no <c>Variant</c>, is compared as text: MS-VBA
/// let-coerces the <c>Variant</c>'s value to <c>String</c>, as Microsoft Learn documents it, where <strong>MS-VBAL §5.6.9.5</strong>'s table makes the
/// effective type the number's - a type mismatch (13) when the <c>String</c> is no number. Every row is Excel's (oracle\s41 V01-V08, W01-W10).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.9.5 Relational Operators")]
public sealed class VariantComparedWithStringTests
{
    private static Task<string[]> RunAsync(params string[] body)
        => ModuleWorkspace.RunAsync([], $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nOn Error Resume Next\r\n{string.Join("\r\n", body)}\r\nDebug.Print Err.Number\r\nEnd Sub\r\n");

    [TestMethod]
    [DataRow(new[] { "Dim v As Variant: v = CCur(0)", "Dim s As String: s = \".\"", "Debug.Print v = s" }, new[] { "False", "0" }, DisplayName = "V01 a Currency and a separator")]
    [DataRow(new[] { "Dim v As Variant: v = CCur(0)", "Debug.Print v = \".\"" }, new[] { "False", "0" }, DisplayName = "V02 a literal")]
    [DataRow(new[] { "Dim v As Variant: v = CCur(5)", "Dim s As String: s = \".\"", "Debug.Print v < s", "Debug.Print v > s" }, new[] { "False", "True", "0" },
        DisplayName = "V03 \"5\" is after \".\"")]
    [DataRow(new[] { "Dim v As Variant: v = 0", "Dim s As String: s = \"x\"", "Debug.Print v = s", "Debug.Print v <> s" }, new[] { "False", "True", "0" },
        DisplayName = "V04 an Integer and a word")]
    [DataRow(new[] { "Dim v As Variant: v = CCur(10)", "Dim s As String: s = \"9\"", "Debug.Print v > s", "Debug.Print v < s" }, new[] { "False", "True", "0" },
        DisplayName = "W01 \"10\" is before \"9\", a number though it is")]
    [DataRow(new[] { "Dim v As Variant: v = 10", "Dim s As String: s = \"9x\"", "Debug.Print v > s", "Debug.Print v < s" }, new[] { "False", "True", "0" },
        DisplayName = "W04 and before \"9x\"")]
    [DataRow(new[] { "Dim v As Variant: v = 1.5", "Dim s As String: s = \"1.5\"", "Debug.Print v = s" }, new[] { "True", "0" }, DisplayName = "W06 the same text")]
    [DataRow(new[] { "Dim v As Variant: v = 10", "Dim s As String: s = \"\"", "Debug.Print v > s", "Debug.Print v = s" }, new[] { "True", "False", "0" },
        DisplayName = "W07 after an empty String")]
    [DataRow(new[] { "Dim v As Variant: v = #1/2/2020#", "Dim s As String: s = \"x\"", "Debug.Print v = s", "Debug.Print v < s" }, new[] { "False", "True", "0" },
        DisplayName = "W08 a Date")]
    [DataRow(new[] { "Dim v As Variant: v = True", "Dim s As String: s = \"x\"", "Debug.Print v = s" }, new[] { "False", "0" }, DisplayName = "W10 a Boolean")]
    public async Task AVariantComparedWithAString_IsComparedAsText(string[] body, string[] printed)
        => CollectionAssert.AreEqual(printed, await RunAsync(body));

    // what the specification says still holds where neither operand is a Variant, and between two Variants.
    [TestMethod]
    [DataRow(new[] { "Dim c As Currency", "Dim s As String: s = \".\"", "Debug.Print c = s" }, new[] { "13" }, DisplayName = "V05 two declared types: a type mismatch")]
    [DataRow(new[] { "Dim s As String: s = \"9x\"", "Dim n As Long: n = 10", "Debug.Print n > s" }, new[] { "13" }, DisplayName = "W09 and a Long")]
    [DataRow(new[] { "Dim v As Variant: v = CCur(0)", "Dim w As Variant: w = \".\"", "Debug.Print v = w" }, new[] { "False", "0" },
        DisplayName = "V06 two Variants: the number is less")]
    [DataRow(new[] { "Dim v As Variant: v = CCur(0)", "Dim s As String: s = \"5\"", "Debug.Print v = s", "Debug.Print v < s" }, new[] { "False", "True", "0" },
        DisplayName = "V08 a number in a String")]
    public async Task AComparisonOfAnotherKind_IsAsSpecified(string[] body, string[] printed)
        => CollectionAssert.AreEqual(printed, await RunAsync(body));
}
