using RDCore.Parsing;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Static;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.Semantics.Static;

/// <summary>
/// The static pass checks what needs no name resolution - where an <c>Exit</c> is written, that labels are defined once and jumped to, which statements
/// the language has - and says what it found as the semantic model of the procedure.
/// </summary>
[TestClass]
public sealed class StaticSemanticsStructureTests
{
    private static (StatementBlock Body, MemberKind Kind) Body(params string[] procedureBody)
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), $"Sub Foo()\r\n{string.Join("\r\n", procedureBody)}\r\nEnd Sub\r\n");
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        var member = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        return (new StatementBlock([.. member.Children]), member.MemberKind);
    }

    private static readonly SemanticId Foo = new(new Uri("file:///c:/ws/Mod1.bas#Foo"));

    [TestMethod]
    public void ABodyWithNothingWrongWithIt_HasAValidModel()
    {
        var (body, kind) = Body("Top:", "x = 1", "GoTo Top");

        var model = StatementStaticSemanticsEvaluator.Analyze(Foo, body, kind: kind);

        Assert.IsTrue(model.IsValid);
        Assert.AreEqual(Foo, model.Procedure);
    }

    [TestMethod]
    public void TheModel_HoldsEveryStructuralError_InTraversalOrderWithTheLabelsLast()
    {
        var (body, kind) = Body("GoTo Nowhere", "Exit Do", "Top:", "Top:");

        var model = StatementStaticSemanticsEvaluator.Analyze(Foo, body, kind: kind);

        CollectionAssert.AreEqual(
            new[] { VBCompileErrorId.ExitDoNotWithinDoLoop, VBCompileErrorId.DuplicateLabelDefinition, VBCompileErrorId.LabelNotDefined },
            model.CompileErrors.Select(error => error.VBCompileErrorId).ToArray());
        Assert.IsFalse(model.IsValid);
    }

    [TestMethod]
    public void AnExitOfTheWrongKind_IsKnownOnceTheKindOfProcedureIs()
    {
        var (body, _) = Body("Exit Function");

        Assert.IsEmpty(StatementStaticSemanticsEvaluator.CheckStructure(body, procedure: null));
        Assert.HasCount(1, StatementStaticSemanticsEvaluator.CheckStructure(body, procedure: MemberKind.Procedure));
    }

    [TestMethod]
    public void AStatementInAnExcludedBranch_IsNotAnalyzed_AndDefinesNoLabel()
    {
        var (body, _) = Body("GoTo Elsewhere", "Exit Do", "Elsewhere:");
        var everything = new SourceRange(SourcePosition.Zero, new SourcePosition(int.MaxValue, int.MaxValue));

        Assert.IsEmpty(StatementStaticSemanticsEvaluator.CheckStructure(body, new StaticSemanticsOptions([everything])));
    }

    [TestMethod]
    public void ABarePrint_IsCheckedAgainstTheLanguage()
    {
        var (body, _) = Body("Print \"x\"");

        Assert.IsEmpty(StatementStaticSemanticsEvaluator.CheckStructure(body, new StaticSemanticsOptions(Language: SupportedLanguages.BASIC)));
        Assert.IsEmpty(StatementStaticSemanticsEvaluator.CheckStructure(body, new StaticSemanticsOptions()), "no language, no language's rules");
        Assert.AreEqual(VBCompileErrorId.SubOrFunctionNotDefined,
            StatementStaticSemanticsEvaluator.CheckStructure(body, new StaticSemanticsOptions(Language: SupportedLanguages.Get("vba"))).Single().VBCompileErrorId);
    }

    [TestMethod]
    public void TheStructure_IsCheckedInsideNestedBlocks()
    {
        var (body, kind) = Body("If True Then", "While True", "Exit For", "Wend", "End If");

        Assert.AreEqual(VBCompileErrorId.ExitForNotWithinForNext,
            StatementStaticSemanticsEvaluator.CheckStructure(body, procedure: kind).Single().VBCompileErrorId);
    }

    [TestMethod]
    public void AModuleModel_IsValidWhenEveryProcedureIs_AndCollectsTheirErrors()
    {
        var (bad, kind) = Body("Exit Do");
        var (good, _) = Body("x = 1");
        var module = new Uri("file:///c:/ws/Mod1.bas");

        var model = new ModuleSemanticModel(module, [
            StatementStaticSemanticsEvaluator.Analyze(Foo, good, kind: kind),
            StatementStaticSemanticsEvaluator.Analyze(new SemanticId(new Uri("file:///c:/ws/Mod1.bas#Bar")), bad, kind: kind),
        ]);

        Assert.IsFalse(model.IsValid);
        Assert.HasCount(1, model.CompileErrors);
        Assert.IsTrue(new ModuleSemanticModel(module, [model.Procedures[0]]).IsValid);
    }
}
