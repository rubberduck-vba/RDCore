using RDCore.SDK.Model.Errors;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics.Instructions;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// Which statements exist is the language's to say. A <c>Print</c> with no file number is a statement of the platform's BASIC and of no other
/// language: in VBA it is the member of a form or a report, and there is no form for a bare one to be a member of - as undefined as the
/// <c>Screen</c> VB6 has and VBA does not.
/// </summary>
[TestClass]
public sealed class BarePrintLanguageTests
{
    private static InstructionListLoweringResult Lower(SupportedLanguage? language, params string[] body)
    {
        var parse = new RDCore.Parsing.ModuleParser().Parse(
            new Uri("file:///c:/ws/Mod1.bas"), $"Sub Foo()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n");
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        var member = parse.SyntaxTree!.Children.OfType<RDCore.SDK.Model.AST.Declarations.MemberDeclarationNode>().Single();
        return InstructionListLowering.Lower(
            new RDCore.SDK.Model.AST.Statements.StatementBlock([.. member.Children]), new InstructionLoweringOptions(Language: language));
    }

    [TestMethod]
    public void ABarePrint_IsAStatementOfBasic()
    {
        var result = Lower(SupportedLanguages.BASIC, "Print \"x\"");

        Assert.IsEmpty(result.Errors);
        Assert.HasCount(1, result.InstructionList.Items);
    }

    [TestMethod]
    [DataRow("vba")]
    [DataRow("vb6")]
    public void ABarePrint_IsUndefinedInEveryOtherLanguage(string id)
    {
        var result = Lower(SupportedLanguages.Get(id), "Print \"x\"");

        var error = result.Errors.Single();
        Assert.AreEqual(VBCompileErrorId.SubOrFunctionNotDefined, error.VBCompileErrorId);
        StringAssert.Contains(error.Verbose, "Print");
        Assert.IsEmpty(result.InstructionList.Items, "the statement is not lowered, there being nothing for it to be");
    }

    [TestMethod]
    public void WhereTheErrorIs_IsTheStatement()
    {
        var error = Lower(SupportedLanguages.RDVBA, "Dim x As Long", "Print x").Errors.Single();

        Assert.AreEqual(2, error.Location.Range.Start.Line, "the third line of the module: the Sub statement is the first");
    }

    [TestMethod]
    public void WithNoLanguageStated_NoLanguagesRulesApply()
        => Assert.IsEmpty(Lower(language: null, "Print \"x\"").Errors);

    [TestMethod]
    [DataRow("vba")]
    [DataRow("vb6")]
    [DataRow("basic")]
    public void ThePrintsThatAreNotBare_AreStatementsOfEveryLanguage(string id)
    {
        var result = Lower(SupportedLanguages.Get(id), "Print #1, \"x\"", "Debug.Print \"x\"");

        Assert.IsEmpty(result.Errors);
        Assert.HasCount(2, result.InstructionList.Items);
    }

    [TestMethod]
    public async Task ABarePrint_RunsInTheBasicEnvironment()
    {
        var result = await StandardLibraryNameTests.RunAsync("RDC", expression: "", statement: "Print \"hello\"", language: SupportedLanguages.BASIC);

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "hello" }, result.Output.ToArray());
    }

    [TestMethod]
    public async Task ABarePrint_IsACompileErrorInTheVBAEnvironment_AndNothingRuns()
    {
        var result = await StandardLibraryNameTests.RunAsync("VBA", expression: "", statement: "Debug.Print \"before\"\r\nPrint \"hello\"", language: SupportedLanguages.RDVBA);

        Assert.AreEqual(ExecutionOutcome.SyntaxError, result.Outcome);
        Assert.IsEmpty(result.Output, "a compile error is found before anything runs");
        StringAssert.Contains(result.Diagnostics.Single(), "Print");
    }
}
