using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics.Instructions;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>Circle</c>, <c>Line</c>, <c>PSet</c> and <c>Scale</c> draw on an object, and the platform has no object that can be drawn on. Written with no object
/// they are undefined; written with one they are a method it does not have. Neither is a statement that does nothing: a drawing that is skipped is a program
/// that lies about what it did.
/// </summary>
[TestClass]
public sealed class GraphicsStatementSemanticsTests
{
    private static InstructionListLoweringResult Lower(SupportedLanguage? language, string statement)
    {
        var parse = new RDCore.Parsing.ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), $"Sub Foo()\r\n{statement}\r\nEnd Sub\r\n");
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        var member = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        return InstructionListLowering.Lower(new StatementBlock([.. member.Children]), new InstructionLoweringOptions(Language: language));
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
