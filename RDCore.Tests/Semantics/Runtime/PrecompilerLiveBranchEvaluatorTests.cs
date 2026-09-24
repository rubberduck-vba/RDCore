using NSubstitute;
using RDCore.Parsing;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.Runtime.Semantics.Operators;
using RDCore.Runtime.Semantics.Precompiler;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// <see cref="PrecompilerLiveBranchEvaluator"/> — MS-VBAL §3.4.2: correlates each <c>#If</c>/<c>#ElseIf</c>/
/// <c>#Else</c> branch (from <c>ModuleParseResult.PrecompilerTrivia</c>) to its source range and yields
/// the ranges of the branches that are not live, evaluating conditions through the real
/// <see cref="RuntimeExpressionEvaluator"/>. Parse-driven: both passes (the main parse and the
/// precompiler-directive parse) run over the exact same source text a real workspace would produce.
/// </summary>
[TestClass]
public sealed class PrecompilerLiveBranchEvaluatorTests
{
    private sealed class Provider(Symbol[] symbols) : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => symbols;
    }

    private static IRuntimeSession ComposeSession(params Symbol[] symbols)
        => RuntimeSessionComposer.Compose(new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), new Provider(symbols));

    private static RuntimeExpressionEvaluator Evaluator() => new(new OperatorRuntimeSemanticsProvider(RealCoercionProvider(), Substitute.For<IVerboseMessageBuilder>()));

    // The real Numeric/Boolean let-coercion strategies - for operators to determine their effective
    // type for real, rather than the identity passthrough a bare NSubstitute fake would give every operand.
    private static ILetCoercionRuntimeSemanticsProvider RealCoercionProvider()
    {
        var formatter = Substitute.For<IVerboseMessageBuilder>();
        var handle = new ProviderHandle();
        ILetCoercionRuntimeSemantics[] strategies =
        [
            new VBNumericLetCoercionTypeRuntimeSemantics(formatter, handle),
            new VBStringLetCoercionRuntimeSemantics(formatter),
            new VBBooleanLetCoercionRuntimeSemantics(handle, formatter),
        ];
        var provider = new LetCoercionRuntimeSemanticsProvider(strategies, formatter);
        handle.Inner = provider;
        return provider;
    }

    private sealed class ProviderHandle : ILetCoercionRuntimeSemanticsProvider
    {
        public ILetCoercionRuntimeSemanticsProvider Inner { get; set; } = default!;
        public LetCoercionResult EvaluateLetCoercionSemantics(ISymbolResolver resolver, ExpressionNode expression, LetCoercionStackFrame frame)
            => Inner.EvaluateLetCoercionSemantics(resolver, expression, frame);
        public RDCore.SDK.Semantics.Analysis.LetCoercionAnalysisContext Analyze(ISymbolResolver resolver, RDCore.SDK.Semantics.Builders.ILetCoercionSemanticContextBuilder builder, ExpressionNode expression, LetCoercionStackFrame frame)
            => Inner.Analyze(resolver, builder, expression, frame);
    }

    private static RDCore.SDK.Model.AST.ModuleParseResult ParseSub(string source)
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
        return parse;
    }

    [TestMethod]
    public void IfElse_TheTrueBranch_ReportsTheElseBranchDead()
    {
        var parse = ParseSub("Sub Foo()\r\n#If DEBUGMODE Then\r\nx = 1\r\n#Else\r\nx = 2\r\n#End If\r\nEnd Sub\r\n");
        var session = ComposeSession(new PrecompilerConstantSymbol("DEBUGMODE", new VBIntegerValue(1)));

        var deadRanges = PrecompilerLiveBranchEvaluator.GetDeadRanges(session, Evaluator(), new(StaticSymbol.GlobalUri), parse.PrecompilerTrivia);

        Assert.HasCount(1, deadRanges);
    }

    [TestMethod]
    public void IfElse_TheFalseCondition_ReportsTheIfBranchDeadInstead()
    {
        var parse = ParseSub("Sub Foo()\r\n#If DEBUGMODE Then\r\nx = 1\r\n#Else\r\nx = 2\r\n#End If\r\nEnd Sub\r\n");
        var session = ComposeSession(new PrecompilerConstantSymbol("DEBUGMODE", new VBIntegerValue(0)));

        var deadRanges = PrecompilerLiveBranchEvaluator.GetDeadRanges(session, Evaluator(), new(StaticSymbol.GlobalUri), parse.PrecompilerTrivia);

        Assert.HasCount(1, deadRanges);
    }

    [TestMethod]
    public void AnUndefinedConstant_IsZero_NotAnError_SoTheFalseBranchIsDead()
        // MS-VBAL §5.6.16.2: an undefined conditional-compilation constant is the value 0 - not a
        // compile error, and there is no Option Explicit for #If.
    {
        var parse = ParseSub("Sub Foo()\r\n#If NEVERDEFINED Then\r\nx = 1\r\n#Else\r\nx = 2\r\n#End If\r\nEnd Sub\r\n");
        var session = ComposeSession();

        var deadRanges = PrecompilerLiveBranchEvaluator.GetDeadRanges(session, Evaluator(), new(StaticSymbol.GlobalUri), parse.PrecompilerTrivia);

        Assert.HasCount(1, deadRanges); // the #If branch (x = 1) is dead; NEVERDEFINED = 0 = False
    }

    [TestMethod]
    public void ElseIfChain_OnlyTheMatchingBranchStaysLive()
    {
        var parse = ParseSub("Sub Foo()\r\n#If A Then\r\nx = 1\r\n#ElseIf B Then\r\nx = 2\r\n#Else\r\nx = 3\r\n#End If\r\nEnd Sub\r\n");
        var session = ComposeSession(
            new PrecompilerConstantSymbol("A", VBBooleanValue.False),
            new PrecompilerConstantSymbol("B", VBBooleanValue.True));

        var deadRanges = PrecompilerLiveBranchEvaluator.GetDeadRanges(session, Evaluator(), new(StaticSymbol.GlobalUri), parse.PrecompilerTrivia);

        Assert.HasCount(2, deadRanges); // #If and #Else are both dead; #ElseIf B is live
    }

    [TestMethod]
    public void NoBranchMatches_AndNoElse_EveryBranchIsDead()
    {
        var parse = ParseSub("Sub Foo()\r\n#If A Then\r\nx = 1\r\n#ElseIf B Then\r\nx = 2\r\n#End If\r\nEnd Sub\r\n");
        var session = ComposeSession(
            new PrecompilerConstantSymbol("A", VBBooleanValue.False),
            new PrecompilerConstantSymbol("B", VBBooleanValue.False));

        var deadRanges = PrecompilerLiveBranchEvaluator.GetDeadRanges(session, Evaluator(), new(StaticSymbol.GlobalUri), parse.PrecompilerTrivia);

        Assert.HasCount(2, deadRanges);
    }

    [TestMethod]
    public void ANestedIfInsideTheLiveBranch_IsAlsoEvaluated()
    {
        var parse = ParseSub(
            "Sub Foo()\r\n#If OUTER Then\r\n#If INNER Then\r\nx = 1\r\n#Else\r\nx = 2\r\n#End If\r\n#End If\r\nEnd Sub\r\n");
        var session = ComposeSession(
            new PrecompilerConstantSymbol("OUTER", VBBooleanValue.True),
            new PrecompilerConstantSymbol("INNER", VBBooleanValue.True));

        var deadRanges = PrecompilerLiveBranchEvaluator.GetDeadRanges(session, Evaluator(), new(StaticSymbol.GlobalUri), parse.PrecompilerTrivia);

        Assert.HasCount(1, deadRanges); // the nested #If's own Else branch
    }

    [TestMethod]
    public void ANestedIfInsideADeadBranch_IsNotSeparatelyReported()
        // the outer branch's own range already covers everything inside it.
    {
        var parse = ParseSub(
            "Sub Foo()\r\n#If OUTER Then\r\ny = 0\r\n#Else\r\n#If INNER Then\r\nx = 1\r\n#Else\r\nx = 2\r\n#End If\r\n#End If\r\nEnd Sub\r\n");
        var session = ComposeSession(
            new PrecompilerConstantSymbol("OUTER", VBBooleanValue.True),
            new PrecompilerConstantSymbol("INNER", VBBooleanValue.True));

        var deadRanges = PrecompilerLiveBranchEvaluator.GetDeadRanges(session, Evaluator(), new(StaticSymbol.GlobalUri), parse.PrecompilerTrivia);

        Assert.HasCount(1, deadRanges); // just the outer #Else - the nested #If inside it is never visited
    }

    [TestMethod]
    public void ComparisonAndLogicalOperators_EvaluateThroughTheRealOperatorSemantics()
        // proves reuse of RuntimeExpressionEvaluator - not a hand-rolled subset - by exercising an
        // operator (Like) the old standalone precompiler evaluator never supported.
    {
        var parse = ParseSub("Sub Foo()\r\n#If NAME Like \"A*\" Then\r\nx = 1\r\n#Else\r\nx = 2\r\n#End If\r\nEnd Sub\r\n");
        var session = ComposeSession(new PrecompilerConstantSymbol("NAME", new VBStringValue("ABC")));

        var deadRanges = PrecompilerLiveBranchEvaluator.GetDeadRanges(session, Evaluator(), new(StaticSymbol.GlobalUri), parse.PrecompilerTrivia);

        Assert.HasCount(1, deadRanges); // "ABC" Like "A*" is True - the #Else branch is dead
    }
}
