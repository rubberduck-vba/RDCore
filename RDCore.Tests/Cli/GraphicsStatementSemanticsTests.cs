using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Workspace;
using RDCore.Runtime.LanguageCore.Instructions;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>Circle</c>, <c>Line</c>, <c>PSet</c> and <c>Scale</c> draw on an object. Written with no object they are undefined, except in a Visual Basic 6.0 form's
/// code, where they are the form's; written with an object they are a call of its method, which an object that cannot draw does not have. Neither is a
/// statement that does nothing: a drawing that is skipped is a program that lies about what it did.
/// </summary>
[TestClass]
public sealed class GraphicsStatementSemanticsTests
{
    // drawsOnMe: the body is a Visual Basic 6.0 form's code.
    private static InstructionListLoweringResult Lower(SupportedLanguage? language, string statement, bool drawsOnMe = false)
    {
        var parse = new RDCore.Parsing.ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), $"Sub Foo()\r\n{statement}\r\nEnd Sub\r\n");
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        var member = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        return InstructionListLowering.Lower(new StatementBlock([.. member.Children]), new InstructionLoweringOptions(Language: language, DrawsOnMe: drawsOnMe));
    }

    [TestMethod]
    [DataRow("Circle (1, 1), 2")]
    [DataRow("PSet (1, 1)")]
    [DataRow("Scale (0, 0)-(1, 1)")]
    [DataRow("Line (0, 0)-(1, 1)")]
    public void AGraphicsStatementWithNoObject_InAVisualBasicFormsCode_IsACallToTheForm(string statement)
    {
        var result = Lower(SupportedLanguages.Get("vb6"), statement, drawsOnMe: true);

        Assert.IsEmpty(result.Errors);
        Assert.IsNotEmpty(result.InstructionList.Items);
    }

    [TestMethod]
    [DataRow("Circle (1, 1), 2", "Circle")]
    [DataRow("PSet (1, 1)", "PSet")]
    [DataRow("Scale (0, 0)-(1, 1)", "Scale")]
    [DataRow("Line (0, 0)-(1, 1)", "Line")]
    public void AGraphicsStatementWithNoObject_IsUndefined_InEveryLanguage(string statement, string word)
    {
        foreach (var language in new[] { SupportedLanguages.BASIC, SupportedLanguages.Get("vba"), SupportedLanguages.Get("vb6") })
        {
            var result = Lower(language, statement);

            var error = result.Errors.Single();
            Assert.AreEqual(VBCompileErrorId.SubOrFunctionNotDefined, error.VBCompileErrorId);
            StringAssert.Contains(error.Verbose, word);
            Assert.IsEmpty(result.InstructionList.Items, "there is nothing for it to be");
        }
    }

    [TestMethod]
    [DataRow("Form1.Circle (1, 1), 2")]
    [DataRow("Form1.PSet (1, 1)")]
    [DataRow("Form1.Scale (0, 0)-(1, 1)")]
    [DataRow("Form1.Line (0, 0)-(1, 1)")]
    [DataRow("With Form1\r\n.Circle (1, 1), 2\r\nEnd With")]
    public void AGraphicsStatementWithAnObject_IsACallToIt(string statement)
    {
        var result = Lower(SupportedLanguages.Get("vba"), statement);

        Assert.IsEmpty(result.Errors);
        Assert.IsNotEmpty(result.InstructionList.Items);
    }

    [TestMethod]
    public async Task AGraphicsStatementOfAnObject_IsAnErrorOfTheObject_WhenItIsRun()
    {
        var result = await ShellHost.Compose().RunAsync([(10, "Form1.CIRCLE (1, 1), 2"), (20, "PRINT 7")]);

        Assert.AreEqual(ExecutionOutcome.RuntimeError, result.Outcome, result.ErrorMessage);
        Assert.AreEqual((int)VBRuntimeErrorId.ObjectDoesntSupportThisPropertyOrMethod, result.ErrorNumber);
        Assert.IsEmpty(result.Output, "the statement after it is not run");
    }

    [TestMethod]
    public async Task AGraphicsStatementWithNoObject_IsNotRun_AndTheProgramSaysSo()
    {
        var result = await ShellHost.Compose().RunAsync([(10, "CIRCLE (1, 1), 2"), (20, "PRINT 7")]);

        Assert.AreNotEqual(ExecutionOutcome.Completed, result.Outcome, "a drawing that was skipped is a program that lies about what it did");
    }
}
