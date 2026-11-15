using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Tests.Cli;

/// <summary>
/// The target of a Mid statement is a bound variable expression (<strong>MS-VBAL §5.4.3.5</strong>), and a bound variable expression must be classified as a
/// variable (<strong>§5.6.16.6</strong>): a property or a procedure of a class, bound early, is "Variable required", reported at the member as it is written after the
/// dot, and its Property Let is not what the statement assigns, as MS-VBA has it (recorded in Excel).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.5 Mid Statement")]
[TestCategory("MS-VBAL 5.6.16.6 Bound Variable Expressions")]
public sealed class MidStatementTargetTests
{
    private static readonly (string, string)[] Classes =
    [
        ("Class1", ModuleWorkspace.ClassModule("Class1",
            "Private mName As String",
            "Public Field As String",
            "Public Property Get Name() As String", "Name = mName", "End Property",
            "Public Property Let Name(ByVal Value As String)", "mName = Value", "End Property",
            "Public Function Label() As String", "Label = mName", "End Function")),
    ];

    private static string Program(params string[] body)
        => $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";

    [TestMethod]
    [DataRow("Mid(c.Name, 1, 1) = \"x\"", ".Name", "the property Name", DisplayName = "a property, by Mid")]
    [DataRow("Mid$(c.Name, 2) = \"x\"", ".Name", "the property Name", DisplayName = "a property, by Mid$")]
    [DataRow("MidB(c.Name, 1, 2) = \"x\"", ".Name", "the property Name", DisplayName = "a property, by MidB")]
    [DataRow("Mid(c.Label, 1, 1) = \"x\"", ".Label", "the procedure Label", DisplayName = "a function")]
    public async Task AMemberOfAClassThatIsNoVariable_IsVariableRequired_AtTheMemberAfterTheDot(string statement, string member, string what)
    {
        var program = Program("Dim c As New Class1", "c.Name = \"abc\"", statement);

        var model = await ModuleWorkspace.ModelAsync(Classes, program);

        var error = model.CompileErrors.Single();
        Assert.AreEqual((int)VBCompileErrorId.VariableRequired, error.ErrorId);
        StringAssert.Contains(error.Verbose, $"and this is {what}");
        var lines = program.Split("\r\n");
        var line = Array.IndexOf(lines, statement);
        var column = lines[line].IndexOf(member, StringComparison.Ordinal);
        Assert.AreEqual((line, column, line, column + member.Length),
            (error.Location.Range.Start.Line, error.Location.Range.Start.Character, error.Location.Range.End.Line, error.Location.Range.End.Character));
    }

    [TestMethod]
    public async Task APropertyOfTheTargetOfAWithBlock_IsVariableRequired()
    {
        var model = await ModuleWorkspace.ModelAsync(Classes, Program("Dim c As New Class1", "With c", "Mid(.Name, 1, 1) = \"x\"", "End With"));

        CollectionAssert.AreEqual(new[] { (int)VBCompileErrorId.VariableRequired }, model.CompileErrors.Select(error => error.ErrorId).ToArray());
    }

    [TestMethod]
    [DataRow("Dim c As New Class1", "Mid(c.Field, 1, 1) = \"x\"", DisplayName = "a public variable of a class")]
    [DataRow("Dim s As String", "Mid(s, 1, 1) = \"x\"", DisplayName = "a local variable")]
    [DataRow("Dim o As Object", "Mid(o.Name, 1, 1) = \"x\"", DisplayName = "a member of an object bound late")]
    public async Task AVariable_OrAMemberBoundLate_IsAValidTarget(string declaration, string statement)
        => Assert.IsEmpty((await ModuleWorkspace.ModelAsync(Classes, Program(declaration, statement))).CompileErrors);
}
