using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>Err.HelpContext</c> as MS-VBA sets it: 1000000 and the number of a run-time error VBA raises or an <c>Err.Raise</c> or <c>Error</c> statement raises, 95
/// for a number VBA does not define, 440 for one past 65535, and what an <c>Err.Raise</c> gives it (observed in Excel).
/// </summary>
[TestClass]
public sealed class ErrHelpContextTests
{
    private static async Task<string> Run(string body)
        => (await RunAsync([], "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPrivate acc As String\r\n"
            + "Private Sub P(ByVal v As Variant)\r\n    acc = acc & CStr(v) & \"|\"\r\nEnd Sub\r\n"
            + $"Public Sub Main()\r\nOn Error Resume Next\r\nDim z As Variant\r\n{string.Join("\r\n", body.Split(" ;; "))}\r\nP Err.HelpContext\r\nDebug.Print acc\r\nEnd Sub\r\n")).Last();

    [TestMethod]
    [DataRow("z = 1 / 0", "1000011|", DisplayName = "a run-time error")]
    [DataRow("z = CLng(\"x\")", "1000013|", DisplayName = "a coercion's error")]
    [DataRow("Err.Raise 5", "1000005|", DisplayName = "Err.Raise of a VBA error")]
    [DataRow("Err.Raise 1000", "1000095|", DisplayName = "Err.Raise of a number VBA does not define")]
    [DataRow("Err.Raise vbObjectError + 1", "1000440|", DisplayName = "Err.Raise of an object's error")]
    [DataRow("Error 9", "1000009|", DisplayName = "the Error statement")]
    [DataRow("Err.Raise 13, , , , 77", "77|", DisplayName = "Err.Raise given one")]
    [DataRow("z = 1 / 0 ;; Err.Clear", "0|", DisplayName = "cleared")]
    public async Task HelpContext_IsWhatMSVBASetsItTo(string body, string printed)
        => Assert.AreEqual(printed, await Run(body));
}
