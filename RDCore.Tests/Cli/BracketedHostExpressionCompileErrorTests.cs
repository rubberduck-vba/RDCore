using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A name in brackets (<strong>MS-VBAL §3.3.5.3</strong>) that something declares is that name, as any other. One that nothing declares is the text of an
/// expression for the host application to evaluate (<c>[A1]</c> in Excel), and RD-VBA hosts none: it is
/// <see cref="RDCore.Runtime.LanguageCore.Errors.VBCompileErrorId.NoHostApplicationToEvaluateExpression"/>, with or without <c>Option Explicit</c>, and
/// never an implicit variable.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.10 Simple Name Expressions")]
public sealed class BracketedHostExpressionCompileErrorTests
{
    private const string NoHost = "There is no host application to evaluate the expression";

    private static Task<string[]> ErrorsOf(bool optionExplicit, params string[] body)
        => LoadErrorsAsync([], [], string.Join("\r\n",
            [
                "Attribute VB_Name = \"Program\"",
                .. optionExplicit ? new[] { "Option Explicit" } : [],
                "Public Sub Main()",
                "    Dim x As Variant",
                .. body,
                "End Sub",
            ]) + "\r\n");

    [TestMethod]
    [DataRow("x = [A1]", true, DisplayName = "read, Option Explicit")]
    [DataRow("x = [A1]", false, DisplayName = "read")]
    [DataRow("[A1] = 5", true, DisplayName = "assigned, Option Explicit")]
    [DataRow("[A1] = 5", false, DisplayName = "assigned")]
    [DataRow("Debug.Print [A1]", true, DisplayName = "printed, Option Explicit")]
    [DataRow("Debug.Print [A1]", false, DisplayName = "printed")]
    [DataRow("x = [A1].Value", true, DisplayName = "the owner of a member access, Option Explicit")]
    [DataRow("x = [A1].Value", false, DisplayName = "the owner of a member access")]
    public async Task ANameInBrackets_ThatNothingDeclares_HasNoHostToEvaluateIt(string statement, bool optionExplicit)
    {
        var errors = await ErrorsOf(optionExplicit, statement);

        Assert.HasCount(1, errors, string.Join(" | ", errors));
        Assert.StartsWith(NoHost, errors[0]);
        Assert.Contains("'[A1]'", errors[0]);
    }

    [TestMethod]
    [DataRow(true, DisplayName = "Option Explicit")]
    [DataRow(false, DisplayName = "no Option Explicit")]
    public async Task ANameInBrackets_ThatALocalDeclares_IsThatLocal(bool optionExplicit)
    {
        var errors = await ErrorsOf(optionExplicit, "Dim [My Field] As Long", "[My Field] = 3", "x = [My Field] + 1");

        Assert.IsEmpty(errors, string.Join(" | ", errors));
    }

    [TestMethod]
    public async Task ANameInBrackets_ThatAModuleDeclares_RunsAsThatVariable()
    {
        var output = await RunAsync([], string.Join("\r\n",
            "Attribute VB_Name = \"Program\"",
            "Option Explicit",
            "Private [My Field] As Long",
            "Public Sub Main()",
            "    [My Field] = 3",
            "    Debug.Print [My Field] + 1",
            "End Sub",
            ""));

        Assert.AreEqual("4", Assert.ContainsSingle(output));
    }

    // the name is the one an unbracketed use declared implicitly: it binds, as it would were the variable declared with Dim.
    [TestMethod]
    public async Task ANameInBrackets_OfAVariableDeclaredImplicitly_IsThatVariable()
    {
        var output = await RunAsync([], string.Join("\r\n",
            "Attribute VB_Name = \"Program\"",
            "Public Sub Main()",
            "    A1 = 2",
            "    Debug.Print [A1] + 1",
            "End Sub",
            ""));

        Assert.AreEqual("3", Assert.ContainsSingle(output));
    }
}
