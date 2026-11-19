using System.Globalization;
using RDCore.Parsing;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;

namespace RDCore.Tests.Parser;

/// <summary>
/// A syntax error is told as MS-VBA tells it when it compiles the module, at its caret (measured in Excel): in a procedure's body, <c>Syntax error</c>
/// over the whole line, from its start to its last character; in a procedure's header or a declaration of the module, <c>Expected: …</c> at the token
/// where what MS-VBA expected is missing, or right where the line ends.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL 2.6.1 Syntax Errors")]
public sealed class SyntaxErrorWordingTests
{
    private static VBSyntaxErrorInfo FirstErrorOf(string source)
    {
        var parse = new ModuleParser().Parse(TestUri.TestModuleUri(), source);
        return parse.SyntaxErrors.OrderBy(error => error.Location.Range.Start).First();
    }

    // the statement is on line 1, after the Sub's header; the range ends after the line's last character, on the line it ends on.
    [TestMethod]
    [DataRow("x = ", 1, 3, DisplayName = "an assignment with no value, the blank after it left out")]
    [DataRow("x = 1 +", 1, 7, DisplayName = "an operator with no right operand")]
    [DataRow("If x", 1, 4, DisplayName = "an If with no Then")]
    [DataRow("Call Foo(1, 2", 1, 13, DisplayName = "a call with no closing parenthesis")]
    [DataRow("Dim x As", 1, 8, DisplayName = "a declaration with no type")]
    [DataRow("On Error GoTo", 1, 13, DisplayName = "an On Error with no label")]
    [DataRow("x = 1 + ' a comment", 1, 19, DisplayName = "the comment after it")]
    [DataRow("    x = 1 +", 1, 11, DisplayName = "the indentation before it")]
    [DataRow("x = 1: s = 1 +", 1, 14, DisplayName = "the statement before it on its line")]
    [DataRow("s = 1 +: x = 1", 1, 14, DisplayName = "the statement after it on its line")]
    [DataRow("L1: x = 1 +", 1, 11, DisplayName = "its label")]
    [DataRow("10 x = 1 +", 1, 10, DisplayName = "its line number")]
    [DataRow("x = 1 + _\r\n    2 +", 2, 7, DisplayName = "the line it continues on")]
    [DataRow("x = 1 + _\r\n", 2, 0, DisplayName = "a line continuation before a blank line")]
    public void InAProcedureBody_ASyntaxError_IsTheWholeLine(string statement, int endLine, int endCharacter)
    {
        var error = FirstErrorOf($"Public Sub S()\r\n{statement}\r\nEnd Sub\r\n");

        Assert.AreEqual("Syntax error", error.Verbose);
        Assert.AreEqual(new SourceRange(new SourcePosition(1, 0), new SourcePosition(endLine, endCharacter)), error.Location.Range);
    }

    // the declaration is on line 0; the caret is on the token MS-VBA marks, or right where the line ends.
    [TestMethod]
    [DataRow("Public Sub ()\r\nEnd Sub", "Expected: identifier", 11, 12, DisplayName = "a Sub with no name")]
    [DataRow("    Public Sub ()\r\nEnd Sub", "Expected: identifier", 15, 16, DisplayName = "a Sub with no name, indented")]
    [DataRow("Public Sub S(x As)\r\nEnd Sub", "Expected: type name", 17, 18, DisplayName = "a parameter with As and no type")]
    [DataRow("Public Sub S(x\r\nEnd Sub", "Expected: list separator or )", 14, 14, DisplayName = "a parameter list with no closing parenthesis")]
    [DataRow("Public Sub S(Optional x As Long = )\r\nEnd Sub", "Expected: expression", 34, 35, DisplayName = "an optional parameter with no default value")]
    [DataRow("Public Sub S(x, )\r\nEnd Sub", "Expected: identifier", 16, 17, DisplayName = "a parameter list with no parameter after a comma")]
    [DataRow("Public Sub S() As Long\r\nEnd Sub", "Expected: end of statement", 15, 17, DisplayName = "a Sub with a return type")]
    [DataRow("Public Function F() As\r\nEnd Function", "Expected: type name", 22, 22, DisplayName = "a Function with As and no type")]
    [DataRow("Public Property X()\r\nEnd Property", "Expected: Get or Let or Set", 16, 17, DisplayName = "a Property with no Get, Let or Set")]
    [DataRow("Private Type\r\nEnd Type", "Expected: identifier", 12, 12, DisplayName = "a Type with no name")]
    [DataRow("Private Enum\r\nEnd Enum", "Expected: identifier", 12, 12, DisplayName = "an Enum with no name")]
    [DataRow("Private Declare PtrSafe Sub Foo", "Expected: Lib", 31, 31, DisplayName = "a Declare with no Lib")]
    [DataRow("Private Declare PtrSafe Function F Lib \"k\" () As", "Expected: type name", 48, 48, DisplayName = "a Declare Function with As and no type")]
    [DataRow("Option Compare", "Expected: Text or Binary", 14, 14, DisplayName = "an Option Compare with no comparison")]
    [DataRow("Private x As", "Expected: New or type name", 12, 12, DisplayName = "a variable with As and no type")]
    public void OutsideAProcedureBody_ASyntaxError_SaysWhatWasExpected(string declaration, string message, int start, int end)
    {
        var error = FirstErrorOf($"{declaration}\r\n");

        Assert.AreEqual(message, error.Verbose);
        Assert.AreEqual(new SourceRange(new SourcePosition(0, start), new SourcePosition(0, end)), error.Location.Range);
    }

    [TestMethod]
    [DataRow("en-US", "Syntax error", "Expected: identifier")]
    [DataRow("fr-CA", "Erreur de syntaxe", "Attendu : identificateur")]
    public void ASyntaxError_IsToldInTheLanguageOfTheUserInterface(string culture, string inABody, string outsideABody)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            Assert.AreEqual(inABody, FirstErrorOf("Public Sub S()\r\nx = 1 +\r\nEnd Sub\r\n").Verbose);
            Assert.AreEqual(outsideABody, FirstErrorOf("Private Type\r\nEnd Type\r\n").Verbose);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
