using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;

namespace RDCore.Tests.Parser;

/// <summary>
/// The declaration pass must never throw. On any input it returns either a usable tree or a
/// <see cref="ModuleParseResult"/> with <c>IsSuccess == false</c> and located syntax errors — never
/// an unhandled exception. A live editor sends malformed / half-typed modules constantly.
/// </summary>
[TestClass]
public sealed class ParserResilienceTests
{
    private static readonly Uri Uri = TestUri.TestModuleUri();

    private static ModuleParseResult Parse(string source)
        => new ModuleParser().Parse(Uri, ModuleType.StdModule, source);

    [TestMethod]
    // the whole point: none of these — valid, half-typed, or garbage — may throw.
    [DataRow("", DisplayName = "empty")]
    [DataRow("   ", DisplayName = "spaces")]
    [DataRow("\r\n\r\n", DisplayName = "blank lines")]
    [DataRow("' just a comment", DisplayName = "comment only")]
    [DataRow("Option Explicit", DisplayName = "option only")]
    [DataRow("Dim value As", DisplayName = "Dim ... As <missing type>")]
    [DataRow("Dim value As New", DisplayName = "Dim ... As New <missing class>")]
    [DataRow("Private x As", DisplayName = "Private ... As <missing type>")]
    [DataRow("Public Sub Foo(", DisplayName = "unclosed paren")]
    [DataRow("Public Sub", DisplayName = "Sub <missing name>")]
    [DataRow("Public Function", DisplayName = "Function <missing name>")]
    [DataRow("Public Property Get", DisplayName = "Property Get <missing name>")]
    [DataRow("Implements", DisplayName = "Implements <missing type>")]
    [DataRow("Type T", DisplayName = "Type <no body>")]
    [DataRow("Enum E", DisplayName = "Enum <no body>")]
    [DataRow("Public Declare Sub Foo Lib", DisplayName = "Declare ... Lib <missing string>")]
    [DataRow("Public Const BIG = 99999999999999999999999999", DisplayName = "integer literal overflows every type")]
    [DataRow("Public Const N = 99999%", DisplayName = "suffix cannot hold the value")]
    [DataRow("Public Const H = &HFFFFFFFFFFFFFFFFFF", DisplayName = "hex literal overflows Int64")]
    [DataRow("???", DisplayName = "garbage")]
    [DataRow("End Sub", DisplayName = "stray End Sub")]
    [DataRow("Attribute VB_Name", DisplayName = "half-typed attribute")]
    public void NeverThrows_AndAnyErrorIsLocated(string source)
    {
        ModuleParseResult result = null!;
        var thrown = Record(() => result = Parse(source));

        Assert.IsNull(thrown, $"parsing threw {thrown?.GetType().Name}: {thrown?.Message}");
        Assert.IsTrue(
            result.SyntaxErrors.All(error => error.Location.Uri == Uri),
            "every syntax error must be located in the parsed document");
    }

    [TestMethod]
    // the flagged NRE: an As-type clause with no type token.
    [DataRow("Dim value As")]
    [DataRow("Private x As")]
    [DataRow("Public Sub Foo(")]
    [DataRow("???")]
    public void MalformedInput_DegradesToLocatedFailure(string source)
    {
        var result = Parse(source);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsNotEmpty(result.SyntaxErrors);
    }

    [TestMethod]
    // valid VBA the declaration pass used to reject — casing, culture, empty forms.
    [DataRow("public Sub Foo()\r\nEnd Sub", DisplayName = "lowercase visibility keyword")]
    [DataRow("PRIVATE Function F() As Long\r\nEnd Function", DisplayName = "uppercase visibility keyword")]
    [DataRow("fRiEnD Property Get P()\r\nEnd Property", DisplayName = "mixed-case visibility keyword")]
    [DataRow("' just a comment", DisplayName = "comment only")]
    [DataRow("\r\n", DisplayName = "blank")]
    [DataRow("#Const RDDEBUG = 1.5\r\n#If RDDEBUG Then\r\nPublic X As Long\r\n#End If", DisplayName = "#Const float literal")]
    [DataRow("Public Const Big = 3000000000", DisplayName = "unsuffixed integer past Long")]
    public void ParsesValidVbaClean(string source)
    {
        var result = Parse(source);

        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);
        Assert.IsNotNull(result.SyntaxTree);
    }

    [TestMethod]
    public void ListenerException_StillYieldsLocatedErrorsAndTrivia()
    {
        // a #Const so there is precompiler trivia to preserve, then a construct that stresses recovery.
        const string source = """
            #Const DEBUG = 1
            Public Property Get
            """;

        var result = Parse(source);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsNotEmpty(result.SyntaxErrors);
        Assert.IsTrue(result.SyntaxErrors.All(error => error.Location.Uri == Uri));
    }

    [TestMethod]
    public void EmptyModule_IsSuccessWithAnEmptyTree()
    {
        var result = Parse("");

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNotNull(result.SyntaxTree);
        Assert.IsEmpty(result.SyntaxTree!.Children);
    }

    [TestMethod]
    public void CommentsOnlyModule_IsSuccess()
    {
        var result = Parse("' header\r\n' more\r\n");

        Assert.IsTrue(result.IsSuccess);
        Assert.IsEmpty(result.SyntaxTree!.Children.OfType<MemberDeclarationNode>());
    }

    private static Exception? Record(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
