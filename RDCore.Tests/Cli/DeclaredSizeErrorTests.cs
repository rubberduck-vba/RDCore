using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.2.3.1.3, §5.2.3.1.4</strong> The sizes a declaration is written with - the bounds of a fixed-size array, the length of a fixed-length
/// string - are constant expressions Let-coerced to <c>Long</c>, which MS-VBA reduces when it compiles, and refuses when they are no size at all.
/// </summary>
/// <remarks>
/// Each expected outcome is what MS-VBA did with the same code, in Excel (64-bit, en-US): the compile error it reported, or what the probe printed.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.2.3.1 Module Variable Declaration Lists")]
public sealed class DeclaredSizeErrorTests
{
    private static readonly IReadOnlyList<(string Name, string Source)> Modules =
        [("Globals", StandardModule("Globals", "Public Count As Long", "Public Enum Color", "    Red = 1", "End Enum"))];

    // the declarations of the module, then the body of Main.
    private static string Program(string[] declarations, params string[] body) => string.Join("\r\n",
        "Attribute VB_Name = \"Program\"",
        "Option Explicit",
        string.Join("\r\n", declarations),
        "Public Sub Main()",
        "    " + string.Join("\r\n    ", body),
        "End Sub",
        string.Empty);

    private static async Task AssertError(string expected, string[] declarations, params string[] body)
    {
        var errors = await LoadErrorsAsync([], Modules, Program(declarations, body));

        Assert.HasCount(1, errors, string.Join("; ", errors));
        StringAssert.StartsWith(errors[0], expected);
    }

    private static async Task AssertPrinted(string expected, string[] declarations, params string[] body)
    {
        var printed = await RunAsync([], Modules, Program(declarations, body));

        Assert.AreEqual(expected, string.Join(" | ", printed));
    }

    [TestMethod]
    [DataRow("Dim s As String * 0")]
    [DataRow("Dim s As String * 65527")]
    [DataRow("Dim s As String * 65535")]
    [DataRow("Static s As String * 0")]
    [DataRow("Dim a(1) As String * 0")]
    [DataRow("ReDim a(1) As String * 0")]
    [DataRow("Const K = 0: Dim s As String * K")]
    [DataRow("Const K = -1: Dim s As String * K")]
    [DataRow("Const K = 65527: Dim s As String * K")]
    [DataRow("Const K = 70000: Dim s As String * K")]
    public Task ALengthNoFixedLengthStringCanBe_IsInvalid(string statement)
        => AssertError("Invalid length for fixed-length string", [], statement);

    [TestMethod]
    [DataRow("Private m As String * 0")]
    [DataRow("Private Type TRecord: s As String * 0: End Type")]
    public Task ALengthNoFixedLengthStringCanBe_IsInvalid_InTheModule(string declaration)
        => AssertError("Invalid length for fixed-length string", [.. declaration.Split(": ")], "Debug.Print 1");

    [TestMethod]
    [DataRow("Dim s As String * 65536")]
    [DataRow("Dim s As String * 70000")]
    [DataRow("Dim s As String * 1.5")]
    public Task ALengthThatIsNoIntegerUpTo65535_IsNoLength(string statement)
        => AssertError("Syntax error", [], statement);

    [TestMethod]
    [DataRow("Dim n As Long: Dim s As String * n", "Constant expression required")]
    [DataRow("Dim s As String * Count", "Constant expression required")]
    [DataRow("Dim s As String * NoSuch", "Variable not defined")]
    public Task ALengthThatIsNoConstant_IsRefused(string statement, string expected)
        => AssertError(expected, [], statement);

    [TestMethod]
    [DataRow("Dim s As String * 65526", "65526")]
    [DataRow("Dim s As String * &H10", "16")]
    [DataRow("Dim s As String * 1&", "1")]
    [DataRow("Const K = 2.5: Dim s As String * K", "2")]
    [DataRow("Const K = \"3\": Dim s As String * K", "3")]
    [DataRow("Dim s As String * Red", "1")]
    [DataRow("Const K = 3: ReDim s(1) As String * K", "3")]
    public Task ALengthIsLetCoercedToLong(string statement, string expected)
        => AssertPrinted(expected, [], statement, statement.Contains("ReDim") ? "Debug.Print Len(s(0))" : "Debug.Print Len(s)");

    [TestMethod]
    [DataRow("Const K = 1 + 2: Dim s As String * K", "3")]
    [DataRow("Const J = 2: Const K = J * (J + 1): Dim s As String * K", "6")]
    [DataRow("Const K = -(-7) \\ 2: Dim s As String * K", "3")]
    [DataRow("Const K = 10 Mod 4 + 2 ^ 2: Dim s As String * K", "6")]
    [DataRow("Const K = 5 / 2: Dim s As String * K", "2")]
    public Task ALengthAConstantExpressionGives_IsTheLengthOfTheString(string statement, string expected)
        => AssertPrinted(expected, [], statement, "Debug.Print Len(s)");

    [TestMethod]
    [DataRow("Dim a(1E10) As Long")]
    [DataRow("Dim a(2147483648#) As Byte")]
    [DataRow("Dim a(-2147483649#) As Byte")]
    [DataRow("Dim a(-5 To -2147483649#) As Byte")]
    [DataRow("Dim a(1 To 2147483648#) As Byte")]
    [DataRow("Const K = 1E10: Dim a(K) As Byte")]
    public Task ABoundPastTheRangeOfALong_IsAnOverflow(string statement)
        => AssertError("Overflow", [], statement);

    [TestMethod]
    [DataRow("Private m(1E10) As Long")]
    [DataRow("Private Type TRecord: X(1E10) As Long: End Type")]
    public Task ABoundPastTheRangeOfALong_IsAnOverflow_InTheModule(string declaration)
        => AssertError("Overflow", [.. declaration.Split(": ")], "Debug.Print 1");

    [TestMethod]
    [DataRow("Private Type TRecord: X(Count) As Long: End Type", "Constant expression required")]
    [DataRow("Private Type TRecord: X(2 To 1) As Long: End Type", "Range has no values")]
    [DataRow("Private Type TRecord: s As String * Count: End Type", "Constant expression required")]
    public Task AFieldOfAUserDefinedType_IsSizedAsAVariableIs(string declaration, string expected)
        => AssertError(expected, [.. declaration.Split(": ")], "Debug.Print 1");

    // a variable has no value until the code runs: the bound is no size to reduce, and its error is the one the bound has.
    [TestMethod]
    [DataRow("Dim n As Long: Dim a(n) As Byte")]
    [DataRow("Dim n As Long: Dim a(1 To n, 2) As Byte")]
    public Task ABoundAVariableGives_IsAConstantExpressionRequired(string statement)
        => AssertError("Constant expression required", [], statement.Split(": "));

    // a call written in a bound is checked as one written anywhere else is, and the bound is no constant expression either (MS-VBA's errors on these are not
    // measured).
    [TestMethod]
    public async Task AReservedNameWithoutItsArguments_InABound_IsWrittenWithThem_AndNoConstantExpression()
        => AssertErrors(await LoadErrorsAsync([], Modules, Program([], "Dim b(LenB) As Long")),
            "Constant expression required", string.Format(RDCore.Runtime.LanguageCore.Exceptions.VBCompileError_ReservedNameArguments_Verbose, "LenB", 1));

    [TestMethod]
    public async Task ACallEndingInAnOmittedArgument_InABound_IsATrailingOmittedArgument_AndNoConstantExpression()
        => AssertErrors(await LoadErrorsAsync([], Modules, Program(["Private Function F(ByVal a As Long, Optional ByVal b As Long) As Long", "End Function"],
            "Dim a(F(1, )) As Long")), "Constant expression required", RDCore.Runtime.LanguageCore.Exceptions.VBCompileError_TrailingOmittedArgument_Verbose);

    // two errors: one that starts with what is said first, and one that says what is said second.
    private static void AssertErrors(string[] errors, string first, string second)
    {
        Assert.HasCount(2, errors, string.Join("; ", errors));
        Assert.IsTrue(errors.Any(error => error.StartsWith(first, StringComparison.Ordinal)), string.Join("; ", errors));
        Assert.IsTrue(errors.Any(error => error.Contains(second, StringComparison.Ordinal)), string.Join("; ", errors));
    }

    [TestMethod]
    [DataRow("Const K = -1: Dim a(K) As Byte", "Range has no values")]
    [DataRow("Const L = 5, U = 2: Dim a(L To U) As Byte", "Range has no values")]
    public Task ABoundAConstantGives_IsARange(string statement, string expected)
        => AssertError(expected, [], statement);

    [TestMethod]
    [DataRow("Dim a(2147483640 To 2147483647) As Byte", "2147483647")]
    [DataRow("Const K = 3: Dim a(K) As Byte", "3")]
    [DataRow("Const K = \"abcd\": Dim a(Len(K)) As Long", "4")]
    [DataRow("Const K = \"abcd\": Dim a(LenB(K)) As Long", "8")]
    public Task ABoundInTheRangeOfALong_IsTheBound(string statement, string expected)
        => AssertPrinted(expected, [], statement, "Debug.Print UBound(a)");

    // a module's array is bounded the same way (observed in Excel).
    [TestMethod]
    public Task ABoundOfAModulesArray_CanBeTheLenOfAConstant()
        => AssertPrinted("4", ["Const K = \"abcd\"", "Private d(Len(K)) As Long"], "Debug.Print UBound(d)");

    // MS-VBA compiles a Const of a procedure valued Len of a constant (observed in Excel; its run crashes Excel), which RDC11021 reports:
    // it is the length MS-VBAL gives it.
    [TestMethod]
    [DataRow("Const K2 = Len(\"abc\")", "Debug.Print K2", "3")]
    [DataRow("Const K = \"abcd\": Const K3 = Len(K)", "Debug.Print K3", "4")]
    public Task AConstOfAProcedure_CanBeTheLenOfAConstant(string statement, string print, string expected)
        => AssertPrinted(expected, [], statement, print);

    // ...where a module's is refused (C01, C02), and so is LenB in a procedure's (C05).
    [TestMethod]
    public Task AConstOfAProcedure_IsNotTheLenBOfAConstant()
        => AssertError("Constant expression required", [], "Const K = \"abcd\": Const K4 = LenB(K)", "Debug.Print K4");

    [TestMethod]
    [DataRow("Const K2 = Len(\"abc\")")]
    [DataRow("Const K = \"abcd\": Const K3 = Len(K)")]
    public Task AConstOfAModule_IsNotTheLenOfAConstant(string declaration)
        => AssertError("Constant expression required", [.. declaration.Split(": ")], "Debug.Print 1");
}
