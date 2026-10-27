namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §6.1.2.7.1.6</strong> <c>IsMissing</c>, end to end, by the parameter it is given: only a <c>Variant</c> parameter with no default value can be
/// missing. Each row is what MS-VBA printed in Excel for the same procedures (oracle\ismissing I01-I09).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.7 Information")]
public sealed class IsMissingParameterTests
{
    private static Task<string[]> RunAsync(string procedures, string calls)
        => ModuleWorkspace.RunAsync([], string.Join("\r\n",
            "Attribute VB_Name = \"Program\"",
            string.Join("\r\n", procedures.Split(" ;; ")),
            "Public Sub Main()",
            string.Join("\r\n", calls.Split(" ;; ").Select(call => $"Debug.Print {call}")),
            "End Sub",
            string.Empty));

    [TestMethod]
    [DataRow("Private Function F(Optional n As Long) As Boolean ;; F = IsMissing(n) ;; End Function", "F() ;; F(3)", "False|False", DisplayName = "I01 an Optional Long")]
    [DataRow("Private Function F(Optional v As Variant = 5) As Boolean ;; F = IsMissing(v) ;; End Function", "F() ;; F(3)", "False|False", DisplayName = "I02 a default")]
    [DataRow("Private Function F(Optional v As Variant) As Boolean ;; F = IsMissing(v) ;; End Function", "F() ;; F(3) ;; F(Empty)", "True|False|False", DisplayName = "I03 an Optional Variant")]
    [DataRow("Private Function R(v As Variant) As Boolean ;; R = IsMissing(v) ;; End Function ;; Private Function F(Optional v As Variant) As Boolean ;; F = R(v) ;; End Function",
        "F() ;; F(1)", "True|False", DisplayName = "I04 passed on to a required Variant")]
    [DataRow("Private Function F(ParamArray a() As Variant) As Boolean ;; F = IsMissing(a) ;; End Function", "F ;; F(1)", "True|False", DisplayName = "I05 a ParamArray")]
    [DataRow("Private Function FS(Optional s As String) As Boolean ;; FS = IsMissing(s) ;; End Function ;; Private Function FO(Optional o As Object) As Boolean ;; FO = IsMissing(o) ;; End Function",
        "FS() ;; FO()", "False|False", DisplayName = "I06 a String, an Object")]
    [DataRow("Private Function U(Optional v) As Boolean ;; U = IsMissing(v) ;; End Function ;; Private Function W(ByVal v As Variant) As Boolean ;; W = IsMissing(v) ;; End Function ;; Private Function X(Optional v As Variant) As Boolean ;; X = W(v) ;; End Function",
        "U() ;; X()", "True|True", DisplayName = "I08 no As clause, ByVal")]
    [DataRow("Private Function T(Optional v As Variant) As Boolean ;; v = 1 ;; T = IsMissing(v) ;; End Function ;; Private Function N(Optional n As Long = 2) As Boolean ;; N = IsMissing(n) ;; End Function",
        "T() ;; N()", "False|False", DisplayName = "I09 assigned, a Long with a default")]
    public async Task IsMissing_IsWhatMsVbaAnswers(string procedures, string calls, string expected)
        => CollectionAssert.AreEqual(expected.Split('|'), await RunAsync(procedures, calls));
}
