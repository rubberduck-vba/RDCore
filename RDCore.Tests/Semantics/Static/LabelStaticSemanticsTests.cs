using RDCore.Parsing;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Semantics.Static;
using RDCore.SDK.Semantics.Static.Abstract;
using System.Collections.Immutable;

namespace RDCore.Tests.Semantics.Static;

/// <summary>
/// The operand of a jump statement names a label, not a value: it must resolve against the labels the
/// procedure defines (MS-VBAL §5.4.2.12–§5.4.2.16, §5.4.4.1, §5.4.4.2), never against the symbols in scope.
/// Parse-driven, so the sentinel rules are proven against the AST shapes the parser really builds.
/// </summary>
[TestClass]
public sealed class LabelStaticSemanticsTests
{
    private static readonly Uri Root = new("file://rdcore-test");

    // Option Explicit is on so that a label name leaking into the expression evaluator would surface as
    // VariableNotDefined instead of quietly resolving to Unknown.
    private static ImmutableArray<VBCompileErrorInfo> Walk(params string[] procedureBody)
    {
        var source = $"Option Explicit\r\nSub Foo()\r\n{string.Join("\r\n", procedureBody)}\r\nEnd Sub\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        var member = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var module = new VBStandardModuleSymbol(Root, Root, "Mod1") with { Directives = new ModuleDirectives(Explicit: true) };
        var tree = ScopeTreeBuilder.Build([module]);
        var context = new StaticEvaluationContext(new ScopeTreeSymbolResolver(tree), tree.ScopeFor(module.Uri));

        return StatementStaticSemanticsEvaluator.Evaluate(context, new StatementBlock([.. member.Children]));
    }

    private static void AssertNoErrors(ImmutableArray<VBCompileErrorInfo> errors)
        => Assert.IsEmpty(errors, string.Join("; ", errors.Select(error => $"{error.VBCompileErrorId}: {error.Verbose}")));

    private static void AssertSingleUndefinedLabel(ImmutableArray<VBCompileErrorInfo> errors, string label)
    {
        Assert.HasCount(1, errors, string.Join("; ", errors.Select(error => $"{error.VBCompileErrorId}: {error.Verbose}")));
        Assert.AreEqual(VBCompileErrorId.LabelNotDefined, errors[0].VBCompileErrorId);
        Assert.AreEqual(label, errors[0].Verbose);
    }

    [TestMethod]
    public void GoTo_ALabelDefinedLater_IsValid()
        => AssertNoErrors(Walk("GoTo Done", "Done:"));

    [TestMethod]
    public void GoTo_ALabelDefinedEarlier_IsValid()
        => AssertNoErrors(Walk("Top:", "GoTo Top"));

    [TestMethod]
    public void GoTo_LabelNames_AreCaseInsensitive()
        => AssertNoErrors(Walk("GoTo done", "DONE:"));

    [TestMethod]
    public void GoTo_AnUndefinedLabel_IsLabelNotDefined_NotAnUndefinedVariable()
        => AssertSingleUndefinedLabel(Walk("GoTo Nowhere"), "Nowhere");

    [TestMethod]
    public void GoTo_ALineNumberDefinedAsALineNumberLabel_IsValid()
        => AssertNoErrors(Walk("GoTo 100", "100:"));

    [TestMethod]
    public void GoTo_ALineNumberNothingDefines_IsLabelNotDefined()
        => AssertSingleUndefinedLabel(Walk("GoTo 200", "100:"), "200");

    [TestMethod]
    public void GoSub_ADefinedLabel_IsValid()
        => AssertNoErrors(Walk("GoSub Sub1", "Exit Sub", "Sub1:", "Return"));

    [TestMethod]
    public void GoSub_AnUndefinedLabel_IsLabelNotDefined()
        => AssertSingleUndefinedLabel(Walk("GoSub Nowhere"), "Nowhere");

    [TestMethod]
    public void OnGoTo_ChecksEveryLabelInTheList()
        => AssertSingleUndefinedLabel(Walk("On 2 GoTo A, B, C", "A:", "C:"), "B");

    [TestMethod]
    public void OnGoSub_ChecksEveryLabelInTheList()
        => AssertSingleUndefinedLabel(Walk("On 1 GoSub A, B", "Exit Sub", "A:", "Return"), "B");

    [TestMethod]
    public void OnGoTo_StillEvaluatesItsSelectorAsAnExpression()
    {
        var errors = Walk("On n GoTo A", "A:");

        Assert.HasCount(1, errors);
        Assert.AreEqual(VBCompileErrorId.VariableNotDefined, errors[0].VBCompileErrorId);
        Assert.AreEqual("n", errors[0].Verbose);
    }

    [TestMethod]
    public void OnErrorGoTo_ADefinedHandler_IsValid()
        => AssertNoErrors(Walk("On Error GoTo Handler", "Exit Sub", "Handler:", "Resume Next"));

    [TestMethod]
    public void OnErrorGoTo_AnUndefinedLabel_IsLabelNotDefined()
        => AssertSingleUndefinedLabel(Walk("On Error GoTo NonExistingLabel"), "NonExistingLabel");

    [TestMethod]
    public void OnErrorGoToZero_IsNotALabelReference()
        // MS-VBAL §5.4.4.1: line number 0 disables error handling - it is never looked up, defined or not.
        => AssertNoErrors(Walk("On Error GoTo 0"));

    [TestMethod]
    public void OnErrorGoToMinusOne_IsNotALabelReference()
        // -1 clears the active error; like 0 it is a sentinel, and the parser builds it as a unary minus over 1.
        => AssertNoErrors(Walk("On Error GoTo -1"));

    [TestMethod]
    public void OnErrorGoTo_AnyOtherNumber_IsStillALabelReference()
        => AssertSingleUndefinedLabel(Walk("On Error GoTo 5"), "5");

    [TestMethod]
    public void OnErrorGoTo_AnyOtherNumber_ResolvesAgainstALineNumberLabel()
        => AssertNoErrors(Walk("On Error GoTo 5", "Exit Sub", "5:", "Resume Next"));

    [TestMethod]
    public void ResumeZero_IsNotALabelReference()
        // MS-VBAL §5.4.4.2 carves out line number 0 for Resume the same way.
        => AssertNoErrors(Walk("Resume 0"));

    [TestMethod]
    public void ResumeMinusOne_IsNotSpecialCased()
        // the -1 sentinel is an On Error GoTo convention only; a Resume has no such carve-out, and a
        // negative number cannot be a line number, so nothing can ever define it.
    {
        var errors = Walk("Resume -1");

        Assert.HasCount(1, errors);
        Assert.AreEqual(VBCompileErrorId.LabelNotDefined, errors[0].VBCompileErrorId);
    }

    [TestMethod]
    public void Resume_ADefinedLabel_IsValid()
        => AssertNoErrors(Walk("Resume Retry", "Retry:"));

    [TestMethod]
    public void Resume_AnUndefinedLabel_IsLabelNotDefined()
        => AssertSingleUndefinedLabel(Walk("Resume Retry"), "Retry");

    [TestMethod]
    public void BareResume_AndResumeNext_ReferenceNoLabel()
        => AssertNoErrors(Walk("Resume", "Resume Next"));

    [TestMethod]
    public void ALabelInsideANestedBlock_IsATargetFromAnywhereInTheProcedure()
        => AssertNoErrors(Walk("GoTo Inside", "If True Then", "Inside:", "Exit Sub", "End If"));
}
