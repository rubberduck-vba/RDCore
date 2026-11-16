using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.6.2.1, §5.3.1.11</strong>: a function or a Property Get named without an argument list, where its value is taken, is called with no
/// arguments. One that has a parameter that is neither <c>Optional</c> nor a <c>ParamArray</c> is "Argument not optional" when it compiles, in any module
/// (MS-VBA and VB6, observed for a function of the library). Its own name in its body is its result.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.2.1 Evaluation to a data value")]
public sealed class BareFunctionReferenceTests
{
    private static string Program(string declarations, string body)
        => $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n{declarations.Replace(" ;; ", "\r\n")}\r\nPublic Sub Main()\r\n{body.Replace(" ;; ", "\r\n")}\r\nEnd Sub\r\n";

    private const string Required = "Function F(ByVal a As Long) As Long ;; F = a ;; End Function";
    private const string RequiredProperty = "Property Get Item(ByVal i As Long) As Long ;; Item = i ;; End Property";

    [TestMethod]
    [DataRow("", "Dim s As String ;; s = Left", DisplayName = "s = Left")]
    [DataRow("", "Debug.Print Left", DisplayName = "Debug.Print Left")]
    [DataRow("", "Dim s As String ;; s = Mid", DisplayName = "s = Mid")]
    [DataRow("", "Dim s As String ;; s = Left$", DisplayName = "s = Left$")]
    [DataRow(Required, "Dim s As String ;; s = F", DisplayName = "s = F, a function of the workspace")]
    [DataRow(Required, "Debug.Print F", DisplayName = "Debug.Print F")]
    [DataRow("", "Dim v As Variant ;; v = Left", DisplayName = "assigned to a Variant")]
    [DataRow("", "Dim s As String ;; s = Strings.Left", DisplayName = "qualified by its module")]
    [DataRow(Required, "Dim s As String ;; s = Program.F", DisplayName = "qualified by the module that declares it")]
    [DataRow(Required, "Dim n As Long ;; n = F + 1", DisplayName = "an operand")]
    [DataRow(Required, "If F Then Debug.Print 1", DisplayName = "a condition")]
    [DataRow(Required, "Debug.Print UCase(F)", DisplayName = "an argument")]
    [DataRow(RequiredProperty, "Debug.Print Item", DisplayName = "a Property Get that takes an argument")]
    [DataRow("Private Declare PtrSafe Function lstrlenW Lib \"kernel32\" (ByVal s As LongPtr) As Long", "Debug.Print lstrlenW", DisplayName = "a Declare")]
    public async Task AFunctionThatTakesAnArgument_NamedWithoutIt_IsArgumentNotOptional(string declarations, string body)
    {
        var errors = await LoadErrorsAsync([], Program(declarations, body));

        Assert.HasCount(1, errors, string.Join(" | ", errors));
        StringAssert.StartsWith(errors[0], "Argument not optional");
    }

    [TestMethod]
    public async Task TheError_NamesTheFunction_AndTheParameterItIsNotGiven()
    {
        var errors = await LoadErrorsAsync([], Program(Required, "Debug.Print F"));

        CollectionAssert.AreEqual(
            new[] { "Argument not optional: 'F' is named without an argument list, which calls it with no arguments; its parameter 'a' is not optional." },
            errors);
    }

    [TestMethod]
    public async Task InAClassModule_ItIsTheSameError()
    {
        var c = ("C", ClassModule("C",
            "Public Function F(ByVal a As Long) As Long", "    F = a", "End Function",
            "Public Sub M()", "    Debug.Print F", "End Sub"));

        var errors = await LoadErrorsAsync([c], Program(string.Empty, "Dim o As New C ;; o.M"));

        Assert.HasCount(1, errors, string.Join(" | ", errors));
        StringAssert.StartsWith(errors[0], "Argument not optional");
    }

    [TestMethod]
    public async Task AFunctionOfAnObject_NamedWithoutItsArgument_IsTheSameError()
    {
        var c = ("C", ClassModule("C", "Public Function F(ByVal a As Long) As Long", "    F = a", "End Function"));

        var errors = await LoadErrorsAsync([c], Program(string.Empty, "Dim o As New C ;; Debug.Print o.F"));

        Assert.HasCount(1, errors, string.Join(" | ", errors));
        StringAssert.StartsWith(errors[0], "Argument not optional");
    }

    [TestMethod]
    [DataRow("Function O(Optional ByVal a As Long = 2) As Long ;; O = a ;; End Function", "Debug.Print O", "2", DisplayName = "only Optional parameters")]
    [DataRow("Function P(ParamArray a() As Variant) As Long ;; P = UBound(a) + 1 ;; End Function", "Debug.Print P", "0", DisplayName = "only a ParamArray")]
    [DataRow("Function R(ByVal n As Long) As Long ;; R = n ;; R = R + 1 ;; End Function", "Debug.Print R(1)", "2", DisplayName = "its own name in its body")]
    [DataRow("Property Get Item(ByVal i As Long) As Long ;; Item = i ;; Item = Item * 2 ;; End Property", "Debug.Print Item(3)", "6",
        DisplayName = "its own name in the body of a Property Get")]
    [DataRow("Property Get PL() As Long ;; PL = 1 ;; End Property", "Debug.Print PL", "1", DisplayName = "a Property Get with no parameters")]
    [DataRow(Required, "Debug.Print F(4)", "4", DisplayName = "given its argument")]
    [DataRow(Required, "Debug.Print Left(\"ab\", 1)", "a", DisplayName = "a library function given its arguments")]
    [DataRow("Sub Takes(ByVal p As LongPtr) ;; Debug.Print \"taken\" ;; End Sub ;; " + Required, "Takes AddressOf F", "taken", DisplayName = "AddressOf")]
    public async Task WhatNeedsNoArgument_OrWhoseValueIsNotTaken_Compiles(string declarations, string body, string printed)
    {
        var program = Program(declarations, body);
        CollectionAssert.AreEqual(Array.Empty<string>(), await LoadErrorsAsync([], program));
        Assert.AreEqual(printed, string.Join("|", await RunAsync([], program)));
    }
}
