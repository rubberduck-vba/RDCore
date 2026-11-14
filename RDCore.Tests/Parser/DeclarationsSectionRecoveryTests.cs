using RDCore.Parsing;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;

namespace RDCore.Tests.Parser;

/// <summary>
/// A syntax error in the declarations section of a module is in one declaration: the procedures after it are read as they are written, whether the module
/// is read as a whole or in segments.
/// </summary>
[TestClass]
public sealed class DeclarationsSectionRecoveryTests
{
    private static readonly Uri Module = new("file:///c:/ws/Module1.bas");

    private static string Source(string declaration) => string.Join("\r\n",
        "Option Explicit",
        declaration,
        "Public y As Long",
        "",
        "Public Sub Main()",
        "    Debug.Print 1",
        "End Sub",
        "",
        "Public Function Two() As Long",
        "    Two = 2",
        "End Function",
        "");

    [TestMethod]
    [DataRow("Public x As Integer +", false, DisplayName = "an operator after the type, read as a whole")]
    [DataRow("Public x As Integer +", true, DisplayName = "an operator after the type, in segments")]
    [DataRow("Public x As", false, DisplayName = "no type after As, read as a whole")]
    [DataRow("Public x As", true, DisplayName = "no type after As, in segments")]
    [DataRow("Private Const c = ", false, DisplayName = "a constant with no value, read as a whole")]
    [DataRow("Private Const c = ", true, DisplayName = "a constant with no value, in segments")]
    [DataRow("Dim (x)", false, DisplayName = "no name, read as a whole")]
    [DataRow("Dim (x)", true, DisplayName = "no name, in segments")]
    [DataRow("Public z As Long -", false, DisplayName = "an operator after the declaration, read as a whole")]
    [DataRow("Public z As Long -", true, DisplayName = "an operator after the declaration, in segments")]
    [DataRow("    x = = 1", false, DisplayName = "an assignment, read as a whole")]
    [DataRow("    x = = 1", true, DisplayName = "an assignment, in segments")]
    [DataRow("    If x Then", false, DisplayName = "an If, read as a whole")]
    [DataRow("    If x Then", true, DisplayName = "an If, in segments")]
    [DataRow("    Loop", false, DisplayName = "a Loop, read as a whole")]
    [DataRow("    Loop", true, DisplayName = "a Loop, in segments")]
    [DataRow("    Foo(1", false, DisplayName = "a call not closed, read as a whole")]
    [DataRow("    Foo(1", true, DisplayName = "a call not closed, in segments")]
    public void AnErrorInTheDeclarationsSection_LeavesTheProceduresAsTheyAre(string declaration, bool segmented)
    {
        var parse = new ModuleParser { Segmented = segmented }.Parse(Module, Source(declaration));

        var members = parse.SyntaxTree!.Descendants().OfType<MemberDeclarationNode>().ToArray();
        CollectionAssert.AreEqual(new[] { "Main", "Two" }, members.Select(member => member.Name).ToArray(), string.Join(", ", members.Select(member => member.Name)));

        // the error is the declaration's, before the procedures: none of them is said to be wrong.
        var error = Assert.ContainsSingle(parse.SyntaxErrors, string.Join("; ", parse.SyntaxErrors.Select(error => $"{error.Location.Range.Start.Line}: {error.Verbose}")));
        Assert.IsLessThan(members[0].SourceLocation.Range.Start.Line, error.Location.Range.Start.Line);
    }

    // a statement written outside the procedures is still source: it is kept as the text it is, where it is written.
    [TestMethod]
    [DataRow(false, DisplayName = "read as a whole")]
    [DataRow(true, DisplayName = "in segments")]
    public void AStatementInTheDeclarationsSection_IsKeptAsItsText(bool segmented)
    {
        var parse = new ModuleParser { Segmented = segmented }.Parse(Module, Source("    x = = 1"));

        var statement = Assert.ContainsSingle(parse.SyntaxTree!.Children.OfType<UnbuiltStatementTriviaNode>());
        Assert.AreEqual("x = = 1", statement.Source);
    }

    // between two procedures, a statement is "Only comments may appear after End Sub, End Function, or End Property": the procedure after it is read as it is.
    [TestMethod]
    [DataRow("    x = = 1", false, DisplayName = "an assignment, read as a whole")]
    [DataRow("    x = = 1", true, DisplayName = "an assignment, in segments")]
    [DataRow("x = 1", false, DisplayName = "a statement that compiles in a procedure, read as a whole")]
    [DataRow("x = 1", true, DisplayName = "a statement that compiles in a procedure, in segments")]
    [DataRow("    Loop", false, DisplayName = "a Loop, read as a whole")]
    [DataRow("    Loop", true, DisplayName = "a Loop, in segments")]
    [DataRow("    Foo(1", false, DisplayName = "a call not closed, read as a whole")]
    [DataRow("    Foo(1", true, DisplayName = "a call not closed, in segments")]
    public void AStatementBetweenTwoProcedures_LeavesTheProceduresAsTheyAre(string statement, bool segmented)
    {
        var source = string.Join("\r\n",
            "Option Explicit",
            "",
            "Public Sub Main()",
            "    Debug.Print 1",
            "End Sub",
            statement,
            "Public Function Two() As Long",
            "    Two = 2",
            "End Function",
            "");
        var parse = new ModuleParser { Segmented = segmented }.Parse(Module, source);

        var members = parse.SyntaxTree!.Descendants().OfType<MemberDeclarationNode>().ToArray();
        CollectionAssert.AreEqual(new[] { "Main", "Two" }, members.Select(member => member.Name).ToArray(), string.Join(", ", members.Select(member => member.Name)));
        var error = Assert.ContainsSingle(parse.SyntaxErrors, string.Join("; ", parse.SyntaxErrors.Select(error => $"{error.Location.Range.Start.Line}: {error.Verbose}")));
        Assert.IsGreaterThan(members[0].SourceLocation.Range.End.Line, error.Location.Range.Start.Line);
        Assert.IsLessThan(members[1].SourceLocation.Range.Start.Line, error.Location.Range.Start.Line);
    }
}
