using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// What <c>Fix</c>, <c>Int</c>, <c>Abs</c> and <c>Sgn</c> of an <c>Integer</c> return, and what <c>Asc</c> returns, is an <c>Integer</c>: an operation of it
/// is one of <c>Integer</c>s, which overflows past 32767 (<strong>MS-VBAL §6.1.2.7</strong>, §6.1.2.11).
/// </summary>
/// <remarks>
/// Each expected outcome is what MS-VBA printed for the same code, in Excel (64-bit, en-US, oracle\s46 F10), as vb6.exe does (HexIDE findings desk #10).
/// </remarks>
[TestClass]
[TestCategory("RD-VBAL 6 Standard library")]
public sealed class IntegerResultFunctionTests
{
    private static async Task<string> RunAsync(string body) => (await ModuleWorkspace.RunAsync([], string.Join("\r\n",
        "Attribute VB_Name = \"Program\"",
        "Public Sub Main()",
        "    Dim n5 As Integer, x As Variant",
        "    n5 = 5",
        "    On Error Resume Next",
        "    " + string.Join("\r\n    ", body.Split(" ;; ")),
        "    Debug.Print CStr(Err.Number) & \" \" & TypeName(x)",
        "End Sub",
        string.Empty))).Last();

    [TestMethod]
    [DataRow("x = Fix(n5) * 7000", "6 Empty", DisplayName = "Fix of an Integer")]
    [DataRow("x = Int(n5) * 7000", "6 Empty", DisplayName = "Int of an Integer")]
    [DataRow("x = Abs(n5) * 7000", "6 Empty", DisplayName = "Abs of an Integer")]
    [DataRow("x = Asc(\"A\") * 700", "6 Empty", DisplayName = "Asc")]
    [DataRow("x = Sgn(n5) * 7000", "0 Integer", DisplayName = "Sgn of an Integer")]
    public async Task AProductOfWhatItReturns_IsAnIntegerProduct(string statement, string expected)
        => Assert.AreEqual(expected, await RunAsync(statement));

    [TestMethod]
    public async Task WhatItReturns_IsAnInteger()
    {
        var printed = await ModuleWorkspace.RunAsync([], string.Join("\r\n",
            "Attribute VB_Name = \"Program\"", "Public Sub Main()", "    Dim n5 As Integer", "    n5 = 5",
            "    Debug.Print TypeName(Fix(n5)) & \" \" & TypeName(Asc(\"A\"))", "End Sub", string.Empty));

        Assert.AreEqual("Integer Integer", printed.Last());
    }
}
