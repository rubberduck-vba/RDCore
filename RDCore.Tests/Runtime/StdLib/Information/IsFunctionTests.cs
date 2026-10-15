using static RDCore.Tests.Runtime.StdLib.Information.InformationWorkspace;

namespace RDCore.Tests.Runtime.StdLib.Information;

/// <summary>
/// <strong>MS-VBAL §6.1.2.7.1.2 to §6.1.2.7.1.9</strong> The <c>Is</c> functions, and how an object's default member answers for it.
/// </summary>
/// <remarks>
/// Each expected value is what MS-VBA printed for the same source, in Excel (en-US).
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.7 Information")]
public sealed class IsFunctionTests
{
    // IsArray, IsDate, IsEmpty, IsError, IsNull, IsNumeric, IsObject, in that order: T for True, - for False.
    private static async Task AssertIs(string declarations, string setup, string expression, string expected)
    {
        string[] functions = ["IsArray", "IsDate", "IsEmpty", "IsError", "IsNull", "IsNumeric", "IsObject"];
        var printed = await Run(declarations, [.. (setup.Length == 0 ? [] : new[] { setup }), .. functions.Select(function => $"Debug.Print {function}({expression})")]);
        var actual = string.Concat(printed.Select(line => line == "True" ? "T" : line == "False" ? "-" : $"[{line}]"));
        Assert.AreEqual(expected, actual, $"Is*({expression}): IsArray, IsDate, IsEmpty, IsError, IsNull, IsNumeric, IsObject");
    }

    [TestMethod]
    //                                     ADEENNO
    [DataRow("1", "-----T-")]
    [DataRow("1.5", "-----T-")]
    [DataRow("1@", "-----T-")]
    [DataRow("CDec(1)", "-----T-")]
    [DataRow("CByte(1)", "-----T-")]
    // a Boolean is numeric, and Empty is too.
    [DataRow("True", "-----T-")]
    [DataRow("Empty", "--T--T-")]
    [DataRow("Null", "----T--")]
    // a Date is a date, and not numeric.
    [DataRow("#1/2/2000#", "-T-----")]
    [DataRow("\"a\"", "-------")]
    // a string is numeric when it Let-coerces to a number.
    [DataRow("\"1\"", "-----T-")]
    [DataRow("CVErr(5)", "---T---")]
    [DataRow("Nothing", "------T")]
    [DataRow("New Collection", "------T")]
    [DataRow("Array(1)", "T------")]
    [DataRow("Array()", "T------")]
    public Task OfAValue(string expression, string expected)
        => AssertIs(string.Empty, string.Empty, expression, expected);

    [TestMethod]
    //                                                                              ADEENNO
    [DataRow("Private a() As Long", "", "a", "T------")]
    [DataRow("Private x As Variant", "", "x", "--T--T-")]
    [DataRow("Private x As Object", "", "x", "------T")]
    [DataRow("Private x As Variant", "Set x = Nothing", "x", "------T")]
    [DataRow("Private x As String", "", "x", "-------")]
    [DataRow("Private x As Date", "", "x", "-T-----")]
    [DataRow("Private x As Long", "", "x", "-----T-")]
    public Task OfAVariable(string declaration, string setup, string expression, string expected)
        => AssertIs(declaration, setup, expression, expected);

    [TestMethod]
    // every Is function but IsObject answers for the object's default member: an object that is an object to IsObject.
    //                       ADEENNO
    [DataRow("DfltStr", "------T")]
    [DataRow("DfltNum", "-----TT")]
    [DataRow("DfltNull", "----T-T")]
    [DataRow("DfltEmpty", "--T--TT")]
    [DataRow("DfltObj", "------T")]
    [DataRow("DfltArr", "T-----T")]
    [DataRow("DfltArgs", "------T")]
    // the error object's default member is Number, a Long.
    [DataRow("Err", "-----TT")]
    public Task OfAnObjectWithADefaultMember(string className, string expected)
        => AssertIs(
            className == "Err" ? string.Empty : $"Private x As {className}",
            className == "Err" ? string.Empty : $"Set x = New {className}",
            className == "Err" ? "Err" : "x",
            expected);

    [TestMethod]
    public Task OfAnUnsetVariableOfAClassWithADefaultMember_IsAnObjectOnly()
        => AssertIs("Private x As DfltNum", string.Empty, "x", "------T");

    [TestMethod]
    [DataRow("IsArray")]
    [DataRow("IsDate")]
    [DataRow("IsEmpty")]
    [DataRow("IsError")]
    [DataRow("IsNull")]
    [DataRow("IsNumeric")]
    public async Task OfAnObjectWhoseDefaultMemberFails_IsFalse_AndLeavesTheErrorInErr_WithoutRaisingIt(string function)
        // MS-VBA answers as if there were no default member to read, and does not jump to the handler: Err holds what failed.
        => CollectionAssert.AreEqual(new[] { "False 5" }, await Run(string.Empty,
            "Dim r As New DfltRaise",
            "On Error GoTo Handler",
            $"Debug.Print {function}(r) & \" \" & Err.Number",
            "Exit Sub",
            "Handler:",
            "Debug.Print \"raised \" & Err.Number"));

    [TestMethod]
    public async Task VarTypeOfAnObjectWhoseDefaultMemberFails_IsObject_AndLeavesTheErrorInErr_WithoutRaisingIt()
        => CollectionAssert.AreEqual(new[] { "9 5" }, await Run(string.Empty,
            "Dim r As New DfltRaise",
            "On Error GoTo Handler",
            "Debug.Print VarType(r) & \" \" & Err.Number",
            "Exit Sub",
            "Handler:",
            "Debug.Print \"raised \" & Err.Number"));

    [TestMethod]
    public async Task TypeNameAndIsObjectOfAnObjectWhoseDefaultMemberFails_NeverCallIt()
        => CollectionAssert.AreEqual(new[] { "DfltRaise True 0" }, await Run(string.Empty,
            "Dim r As New DfltRaise",
            "Debug.Print TypeName(r) & \" \" & IsObject(r) & \" \" & Err.Number"));

    [TestMethod]
    public async Task ADefaultMembersOptionalParameters_TakeTheirDefaults()
        // the default member is called with no arguments: n takes its declared default, and v is Missing.
        => CollectionAssert.AreEqual(new[] { "8", "True 7" }, await Run(string.Empty,
            "Dim o As New DfltOpt",
            "Dim s As String",
            "Debug.Print VarType(o)",
            "s = o",
            "Debug.Print s"));

    #region IsNumeric and IsDate of a String

    [TestMethod]
    [DataRow("1")]
    [DataRow(" 1 ")]
    [DataRow("1e3")]
    [DataRow("1d3")]
    [DataRow("1E+3")]
    [DataRow("1.5")]
    [DataRow(".5")]
    [DataRow("5.")]
    [DataRow("-1")]
    [DataRow("+1")]
    [DataRow("2000")]
    [DataRow("0")]
    public async Task IsNumeric_OfANumberString_IsTrue(string text)
        => CollectionAssert.AreEqual(new[] { "True" }, await Run(string.Empty, $"Debug.Print IsNumeric(\"{text}\")"));

    [TestMethod]
    [DataRow("&H")]
    [DataRow("--1")]
    [DataRow("1%")]
    [DataRow("1&")]
    [DataRow("1#")]
    [DataRow("1@")]
    [DataRow("abc")]
    [DataRow("")]
    [DataRow(" ")]
    // the Boolean tokens Let-coerce to a Boolean, which is numeric, but are not numbers themselves.
    [DataRow("True")]
    [DataRow("False")]
    [DataRow("1 2")]
    [DataRow("1.2.3")]
    [DataRow("1/2/2000")]
    [DataRow("12:30")]
    public async Task IsNumeric_OfAStringThatIsNotANumber_IsFalse(string text)
        => CollectionAssert.AreEqual(new[] { "False" }, await Run(string.Empty, $"Debug.Print IsNumeric(\"{text}\")"));

    [TestMethod]
    [Ignore("The numeric Let-coercion grammar (ValueConversions.StringToNumeric) reads neither &H/&O literals, thousands separators, a currency " +
        "symbol, parentheses nor a trailing sign, all of which MS-VBA's (the host's regional number format, MS-VBAL 5.5.1.2.4) does.")]
    [DataRow("&H10")]
    [DataRow("&HFFFF")]
    [DataRow("&O7")]
    [DataRow("1,000")]
    [DataRow("1,0,0")]
    [DataRow("1-")]
    [DataRow("(1)")]
    [DataRow("$1")]
    [DataRow("1$")]
    public async Task IsNumeric_OfARegionalNumberString_IsTrue(string text)
        => CollectionAssert.AreEqual(new[] { "True" }, await Run(string.Empty, $"Debug.Print IsNumeric(\"{text}\")"));

    [TestMethod]
    [DataRow("1/2/2000")]
    [DataRow("12/31/2000")]
    [DataRow("2000-01-02")]
    [DataRow("2000/1/2")]
    [DataRow("1-2-2000")]
    [DataRow("Jan 2 2000")]
    [DataRow("2 Jan 2000")]
    [DataRow("January 2, 2000")]
    [DataRow("Feb 2000")]
    [DataRow("2/2000")]
    [DataRow("1/2")]
    [DataRow("12:30")]
    [DataRow("12:30 PM")]
    [DataRow("12:30:15")]
    [DataRow("1/2/2000 12:30")]
    // two numbers with a separator between them are a month and a day.
    [DataRow("1 2")]
    [DataRow("1.5")]
    [DataRow("1.2.3")]
    public async Task IsDate_OfADateOrTimeString_IsTrue(string text)
        => CollectionAssert.AreEqual(new[] { "True" }, await Run(string.Empty, $"Debug.Print IsDate(\"{text}\")"));

    [TestMethod]
    // a number Let-coerces to a Date, but a string that is one only by being a number is not a date to IsDate.
    [DataRow("1")]
    [DataRow("2000")]
    [DataRow("0")]
    [DataRow(".5")]
    [DataRow("abc")]
    [DataRow("")]
    [DataRow("13:70")]
    [DataRow("25:00")]
    [DataRow("2/30/2000")]
    [DataRow("noon")]
    [DataRow("Monday")]
    [DataRow("True")]
    public async Task IsDate_OfAStringThatIsNotADate_IsFalse(string text)
        => CollectionAssert.AreEqual(new[] { "False" }, await Run(string.Empty, $"Debug.Print IsDate(\"{text}\")"));

    [TestMethod]
    [Ignore("Date strings are read by .NET's invariant DateTime.TryParse (ValueConversions.TryInterpretDate), not by MS-VBA's own grammar, " +
        "which swaps day and month when the month is out of range and reads a comma as a date separator.")]
    [DataRow("31/12/2000")]
    [DataRow("1,000")]
    public async Task IsDate_OfADateStringOnlyMSVBAReads_IsTrue(string text)
        => CollectionAssert.AreEqual(new[] { "True" }, await Run(string.Empty, $"Debug.Print IsDate(\"{text}\")"));

    [TestMethod]
    [Ignore("Date strings are read by .NET's invariant DateTime.TryParse (ValueConversions.TryInterpretDate), which accepts forms MS-VBA's " +
        "own grammar refuses: a literal's # delimiters, a weekday name, dots as date separators.")]
    [DataRow("#1/2/2000#")]
    [DataRow("Mon, Jan 3 2000")]
    [DataRow("1.1.2000")]
    public async Task IsDate_OfAStringMSVBARefuses_IsFalse(string text)
        => CollectionAssert.AreEqual(new[] { "False" }, await Run(string.Empty, $"Debug.Print IsDate(\"{text}\")"));

    [TestMethod]
    public async Task IsDate_OfANumberOrNothing_IsFalse()
        => CollectionAssert.AreEqual(new[] { "False False False False False" }, await Run(string.Empty,
            "Debug.Print IsDate(1) & \" \" & IsDate(36526#) & \" \" & IsDate(Empty) & \" \" & IsDate(Null) & \" \" & IsDate(CCur(1))"));

    #endregion
}
