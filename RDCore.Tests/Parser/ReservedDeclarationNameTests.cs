using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Tests.Parser;

/// <summary>
/// A declaration is named with an <c>IDENTIFIER</c>, which is no reserved identifier (<strong>MS-VBAL §3.3.5.2</strong>): a reserved name (<c>Len</c>,
/// <c>Date</c>), a special form (<c>Array</c>, <c>LBound</c>), a statement keyword, a marker keyword or a literal identifier names no variable, constant,
/// procedure or Enum member. A member of a user-defined type can be named with one (<strong>MS-VBAL §5.2.3.3</strong> <c>reserved-name-member-dcl</c>), and so can a
/// name in brackets (<strong>MS-VBAL §3.3.5.3</strong>).
/// </summary>
[TestClass]
[TestCategory("RD-VBAL 2.6.1 Syntax Errors")]
public sealed class ReservedDeclarationNameTests
{
    private static readonly string[] Names =
        ["Len", "Array", "LBound", "Date", "String", "Me", "Input", "Open", "Then", "To", "True", "Nothing", "Decimal", "Attribute", "VB_Name", "LineInput"];

    // each kind of declaration, the name written where {0} is.
    private static readonly (string Kind, string[] Lines)[] Kinds =
    [
        ("a parameter", ["Public Sub S(ByVal {0} As Long)", "    x = 1", "End Sub"]),
        ("a local variable", ["Public Sub S()", "    Dim {0} As Long", "    x = 1", "End Sub"]),
        ("a Static variable", ["Public Sub S()", "    Static {0} As Long", "    x = 1", "End Sub"]),
        ("a local constant", ["Public Sub S()", "    Const {0} As Long = 1", "    x = 1", "End Sub"]),
        ("a module variable", ["Private {0} As Long", "Public Sub S()", "    x = 1", "End Sub"]),
        ("a module constant", ["Private Const {0} As Long = 1", "Public Sub S()", "    x = 1", "End Sub"]),
        ("a Sub", ["Public Sub {0}()", "    x = 1", "End Sub"]),
        ("a Function", ["Public Function {0}() As Long", "    x = 1", "End Function"]),
        ("a Property Get", ["Public Property Get {0}() As Long", "    x = 1", "End Property"]),
        ("an Enum member", ["Private Enum E", "    {0}", "End Enum"]),
        ("a Declare", ["Private Declare PtrSafe Sub {0} Lib \"x\" ()"]),
    ];

    // MS-VBA compiles an Enum member named Date, and so does RD-VBA (ANameMSVBAAcceptsThatTheSpecificationReserves_IsValid).
    public static IEnumerable<object[]> Declarations => Kinds.SelectMany(kind => Names
        .Where(name => !(kind.Kind == "an Enum member" && name == "Date"))
        .Select(name => new object[] { kind.Kind, name }));

    public static string DeclarationName(System.Reflection.MethodInfo method, object[] row) => $"{row[1]}, {row[0]}";

    [TestMethod]
    [DynamicData(nameof(Declarations), DynamicDataDisplayName = nameof(DeclarationName))]
    public void AReservedIdentifierAsTheNameOfADeclaration_IsOneSyntaxErrorAtTheName_AndTheModuleIsRead(string kind, string name)
    {
        var template = Kinds.Single(row => row.Kind == kind).Lines;
        var lines = template.Select(line => string.Format(line, name)).ToArray();
        var line = Array.FindIndex(template, text => text.Contains("{0}", StringComparison.Ordinal));
        var at = template[line].IndexOf("{0}", StringComparison.Ordinal);
        var after = lines.Length;
        var module = string.Join("\r\n", lines) + "\r\nPublic Sub T()\r\n    y = 2\r\nEnd Sub\r\n";

        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), module);

        var said = string.Join(" | ", parse.SyntaxErrors.Select(error => $"{error.Location.Range}: {error.Verbose}"));
        var error = Assert.ContainsSingle(parse.SyntaxErrors, said);
        Assert.AreEqual((int)VBCompileErrorId.SyntaxError, error.ErrorId, said);
        var message = kind == "a parameter" ? RDCore.Parsing.Exceptions.VBCompileError_ReservedParameterName_Verbose : RDCore.Parsing.Exceptions.VBCompileError_ReservedDeclarationName_Verbose;
        Assert.AreEqual(string.Format(message, name), error.Verbose, "the error says the name is reserved");
        Assert.AreEqual(new SourceRange(new SourcePosition(line, at), new SourcePosition(line, at + name.Length)), error.Location.Range, "the error is the name");

        var next = Assert.ContainsSingle(parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Where(member => member.Name == "T"), "the procedure after it is read");
        Assert.AreEqual(after, next.SourceLocation.Range.Start.Line, "the procedure after it begins at its header");
        Assert.IsTrue(next.Children.OfType<AssignmentStatementNode>().Any(node => node.SourceLocation.Range.Start.Line == after + 1), "its body is its own");
    }

    // reserved identifiers the grammar reads as no name at all: a marker keyword, a literal identifier, a statement keyword, a future-reserved word.
    [TestMethod]
    [DataRow("Each")]
    [DataRow("Empty")]
    [DataRow("EndIf")]
    [DataRow("CDecl")]
    public void AReservedIdentifierTheGrammarReadsAsNoName_AsTheNameOfAParameter_IsOneSyntaxErrorAtTheName_AndTheProcedureIsWhole(string name)
    {
        var module = $"Public Sub S(ByVal {name} As Long)\r\n    x = 1\r\nEnd Sub\r\nPublic Sub T()\r\n    y = 2\r\nEnd Sub\r\n";

        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), module);

        var said = string.Join(" | ", parse.SyntaxErrors.Select(error => $"{error.Location.Range}: {error.Verbose}"));
        var error = Assert.ContainsSingle(parse.SyntaxErrors, said);
        Assert.AreEqual(new SourceRange(new SourcePosition(0, 19), new SourcePosition(0, 19 + name.Length)), error.Location.Range, "the error is the name");
        var members = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().ToList();
        var procedure = Assert.ContainsSingle(members.Where(member => member.Name == "S"), "the procedure is read");
        Assert.AreEqual(2, procedure.SourceLocation.Range.End.Line, "the procedure ends at its end");
        Assert.IsTrue(procedure.Children.OfType<AssignmentStatementNode>().Any(node => node.SourceLocation.Range.Start.Line == 1), "its body is its own");
        Assert.ContainsSingle(members.Where(member => member.Name == "T"), "the procedure after it is read");
    }

    // MS-VBA refuses these, where MS-VBAL §3.3.5.2 reads an IDENTIFIER: a reserved-type-identifier is no reserved-identifier, a reserved-member-name can be a
    // reserved-name, and Name is no reserved-identifier. RD-VBA reads them as the specification does, and a warning says MS-VBA refuses them (RDC11034).
    [TestMethod]
    [DataRow("Public Sub S()\r\n    Dim Long As Long\r\nEnd Sub", DisplayName = "a variable named Long")]
    [DataRow("Private Type T\r\n    Me As Long\r\nEnd Type", DisplayName = "a member of a user-defined type named Me")]
    [DataRow("Private Enum E\r\n    Name\r\nEnd Enum", DisplayName = "an Enum member named Name")]
    public void ANameMSVBARefusesThatTheSpecificationAllows_IsValid(string module)
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), module + "\r\n");

        Assert.IsEmpty(parse.SyntaxErrors, string.Join(" | ", parse.SyntaxErrors.Select(error => $"{error.Location.Range}: {error.Verbose}")));
    }

    // MS-VBA compiles these, which MS-VBAL §3.3.5.2 reserves: RD-VBA compiles them too, and a warning says the specification reserves the name (RDC11033).
    [TestMethod]
    [DataRow("Date")]
    [DataRow("DoEvents")]
    [DataRow("CDec")]
    public void ANameMSVBAAcceptsThatTheSpecificationReserves_IsValid(string name)
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), $"Private Enum E\r\n    {name}\r\nEnd Enum\r\n");

        Assert.IsEmpty(parse.SyntaxErrors, string.Join(" | ", parse.SyntaxErrors.Select(error => $"{error.Location.Range}: {error.Verbose}")));
        Assert.ContainsSingle(parse.SyntaxTree!.Descendants().OfType<ConstantDeclarationNode>().Where(member => member.Name == name), "the member is declared");
    }

    [TestMethod]
    [DataRow("Private Type T\r\n    Len As Long\r\n    Date As Date\r\n    Then As Long\r\nEnd Type", DisplayName = "the members of a user-defined type")]
    [DataRow("Private [Len] As Long", DisplayName = "a name in brackets")]
    [DataRow("Public Sub S()\r\n    Dim [Array] As Long\r\nEnd Sub", DisplayName = "a local name in brackets")]
    [DataRow("Public Sub S(ByVal Step As Long)\r\nEnd Sub", DisplayName = "Step, which is no reserved identifier")]
    [DataRow("Public Sub S()\r\n    Dim Name As String, Text As String\r\nEnd Sub", DisplayName = "Name and Text, which are no reserved identifiers")]
    [DataRow("Public Sub S()\r\n    Dim Lenient As Long\r\nEnd Sub", DisplayName = "a name a reserved name starts")]
    public void ANameThatIsNoReservedIdentifier_IsValid(string module)
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), module + "\r\n");

        Assert.IsEmpty(parse.SyntaxErrors, string.Join(" | ", parse.SyntaxErrors.Select(error => $"{error.Location.Range}: {error.Verbose}")));
    }
}
