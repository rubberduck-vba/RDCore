using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.3.1.11</strong> A <c>ParamArray</c> is no variable a <c>ByRef</c> parameter of the code's own can be bound to: VBC09305, Invalid
/// ParamArray use. Each row is what MS-VBA reported compiling the same statement in Excel (64-bit, en-US).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.3.1.11 Argument Lists")]
public sealed class InvalidParamArrayUseTests
{
    private static readonly IReadOnlyList<(string Name, string Source)> Classes =
        [("Taker", ClassModule("Taker", "Public Sub Take(ByRef a As Variant)", "End Sub"))];

    private static Task<string[]> ErrorsOf(string statement)
        => LoadErrorsAsync(Classes,
            "Attribute VB_Name = \"Program\"\r\n"
            + "Private Sub Inner(ByRef a As Variant)\r\nEnd Sub\r\nPrivate Sub InnerVal(ByVal a As Variant)\r\nEnd Sub\r\n"
            + "Private Sub InnerArr(a() As Variant)\r\nEnd Sub\r\nPrivate Sub InnerImplicit(a)\r\nEnd Sub\r\n"
            + "Private Sub InnerPA(ParamArray q() As Variant)\r\nEnd Sub\r\nPrivate Function Fn(a As Variant) As Variant\r\nEnd Function\r\n"
            + "Public Sub Main(ParamArray p() As Variant)\r\n    Dim v As Variant\r\n"
            + $"    {statement}\r\nEnd Sub\r\n");

    [TestMethod]
    [DataRow("Inner p")]
    [DataRow("Inner (p)")]
    [DataRow("Call Inner(p)")]
    [DataRow("InnerArr p")]
    [DataRow("InnerImplicit p")]
    [DataRow("Dim t As New Taker: t.Take p")]
    [DataRow("v = Fn(p)")]
    [DataRow("Inner a:=p")]
    public async Task AParamArray_PassedToAByRefParameterOfTheCodesOwn_IsInvalidParamArrayUse(string statement)
        => StringAssert.StartsWith(Assert.ContainsSingle(await ErrorsOf(statement)), RDCore.Runtime.LanguageCore.Exceptions.VBCompileError_InvalidParamArrayUse);

    [TestMethod]
    [DataRow("Inner p(0)")]
    [DataRow("InnerVal p")]
    [DataRow("InnerPA p")]
    [DataRow("v = IsMissing(p)")]
    [DataRow("v = p")]
    public async Task AParamArray_PassedAnyOtherWay_Compiles(string statement)
        => Assert.IsEmpty(await ErrorsOf(statement));
}
