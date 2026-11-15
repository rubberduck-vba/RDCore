using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Tests.Cli;

/// <summary>
/// The target of a Mid statement is a bound variable expression (<strong>MS-VBAL §5.4.3.5</strong>), and a bound variable expression must be classified as a
/// variable (<strong>§5.6.16.6</strong>): a property or a procedure of a class, bound early, and a member of an object bound late (an unbound member) are
/// "Variable required", reported at the member as it is written after the dot, and a Property Let is not what the statement assigns, as MS-VBA has it (recorded in Excel).
/// So is a public variable of a class reached through an object, <c>Me</c> included: <strong>§5.6.12</strong> classifies it as a variable, and RD-VBA follows MS-VBA,
/// which refuses it.
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
        => $"Attribute VB_Name = \"Program\"\r\nPrivate Type TRec\r\nS As String\r\nEnd Type\r\nPublic ModVar As String\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";

    [TestMethod]
    [DataRow("Mid(c.Name, 1, 1) = \"x\"", ".Name", "the property Name", DisplayName = "a property, by Mid")]
    [DataRow("Mid$(c.Name, 2) = \"x\"", ".Name", "the property Name", DisplayName = "a property, by Mid$")]
    [DataRow("MidB(c.Name, 1, 2) = \"x\"", ".Name", "the property Name", DisplayName = "a property, by MidB")]
    [DataRow("Mid(c.Label, 1, 1) = \"x\"", ".Label", "the procedure Label", DisplayName = "a function")]
    [DataRow("Mid(c.Field, 1, 1) = \"x\"", ".Field", "the public variable Field of a class", DisplayName = "a public variable, by Mid")]
    [DataRow("Mid$(c.Field, 2) = \"x\"", ".Field", "the public variable Field of a class", DisplayName = "a public variable, by Mid$")]
    [DataRow("MidB(c.Field, 1, 2) = \"x\"", ".Field", "the public variable Field of a class", DisplayName = "a public variable, by MidB")]
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
    [DataRow(".Name", DisplayName = "a property")]
    [DataRow(".Field", DisplayName = "a public variable")]
    public async Task AMemberOfTheTargetOfAWithBlock_IsVariableRequired(string member)
    {
        var model = await ModuleWorkspace.ModelAsync(Classes, Program("Dim c As New Class1", "With c", $"Mid({member}, 1, 1) = \"x\"", "End With"));

        CollectionAssert.AreEqual(new[] { (int)VBCompileErrorId.VariableRequired }, model.CompileErrors.Select(error => error.ErrorId).ToArray());
    }

    [TestMethod]
    [DataRow("Mid(Me.Field, 1, 1) = \"x\"", 1, DisplayName = "by Me, Variable required")]
    [DataRow("Mid(Field, 1, 1) = \"x\"", 0, DisplayName = "by its name alone, valid")]
    public async Task APublicVariableOfAClass_InTheClassItself(string statement, int errors)
    {
        (string, string)[] classes =
        [
            ("Class2", ModuleWorkspace.ClassModule("Class2",
                "Public Field As String",
                "Public Function SelfMid() As String", "Field = \"abc\"", statement, "SelfMid = Field", "End Function")),
        ];

        var model = await ModuleWorkspace.ModelAsync(classes, Program("Dim c As New Class2", "Debug.Print c.SelfMid"), moduleName: "Class2");

        CollectionAssert.AreEqual(Enumerable.Repeat((int)VBCompileErrorId.VariableRequired, errors).ToArray(), model.CompileErrors.Select(error => error.ErrorId).ToArray());
    }

    [TestMethod]
    [DataRow("Dim o As Object", "Mid(o.Field, 1, 1) = \"x\"", ".Field", DisplayName = "a member of an Object")]
    [DataRow("Dim v As Variant", "Mid(v.Field, 1, 1) = \"x\"", ".Field", DisplayName = "a member of a Variant")]
    [DataRow("Dim o As Object", "Mid$(o.Name, 2) = \"x\"", ".Name", DisplayName = "a member of an Object, by Mid$")]
    public async Task AMemberOfAnObjectBoundLate_IsVariableRequired_AtTheMemberAfterTheDot(string declaration, string statement, string member)
    {
        var program = Program(declaration, "Set " + declaration[4..declaration.IndexOf(' ', 4)] + " = New Class1", statement);

        var model = await ModuleWorkspace.ModelAsync(Classes, program);

        var error = model.CompileErrors.Single();
        Assert.AreEqual((int)VBCompileErrorId.VariableRequired, error.ErrorId);
        StringAssert.Contains(error.Verbose, $"and this is the member {member[1..]} of an object bound late");
        var lines = program.Split("\r\n");
        var line = Array.IndexOf(lines, statement);
        var column = lines[line].IndexOf(member, StringComparison.Ordinal);
        Assert.AreEqual((line, column, line, column + member.Length),
            (error.Location.Range.Start.Line, error.Location.Range.Start.Character, error.Location.Range.End.Line, error.Location.Range.End.Character));
    }

    [TestMethod]
    public async Task AMemberOfTheTargetOfAWithBlockOnAnObject_IsVariableRequired()
    {
        var model = await ModuleWorkspace.ModelAsync(Classes, Program("Dim o As Object", "Set o = New Class1", "With o", "Mid(.Field, 1, 1) = \"x\"", "End With"));

        CollectionAssert.AreEqual(new[] { (int)VBCompileErrorId.VariableRequired }, model.CompileErrors.Select(error => error.ErrorId).ToArray());
    }

    [TestMethod]
    [DataRow("Dim s As String", "Mid(s, 1, 1) = \"x\"", DisplayName = "a local variable")]
    [DataRow("Dim a(1 To 2) As String", "Mid(a(1), 1, 1) = \"x\"", DisplayName = "an element of an array")]
    [DataRow("Dim t As TRec", "Mid(t.S, 1, 1) = \"x\"", DisplayName = "a member of a user-defined type")]
    [DataRow("Dim s As String", "Mid(Program.ModVar, 1, 1) = \"x\"", DisplayName = "a variable of a module, by its module's name")]
    public async Task AVariable_IsAValidTarget(string declaration, string statement)
        => Assert.IsEmpty((await ModuleWorkspace.ModelAsync(Classes, Program(declaration, statement))).CompileErrors);
}
