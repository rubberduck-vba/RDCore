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
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Services.VerboseMessages;
using System.Collections.Immutable;

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

    // the condition is evaluated a call a level: the module's conditions are evaluated on a large-stack thread, from a thread no larger than one the
    // environment host defines a module's symbols on.
    [TestMethod]
    public void ACondition_NestedAsDeepAsRDVBAReads_IsEvaluated_OnASmallStack()
    {
        var parse = ParseSub("Sub Foo()\r\n#If DEBUGMODE + 0 Then\r\nx = 1\r\n#Else\r\nx = 2\r\n#End If\r\nEnd Sub\r\n");
        var trivia = WithCondition(parse.PrecompilerTrivia, condition => Chain((VBBinaryOperatorExpressionNode)condition, ModuleParser.MaxExpressionNesting));
        var session = ComposeSession(new PrecompilerConstantSymbol("DEBUGMODE", new VBIntegerValue(1)));

        ImmutableArray<SourceRange> deadRanges = [];
        var thrown = SmallStack.Run(() => deadRanges = PrecompilerLiveBranchEvaluator.GetDeadRanges(session, Evaluator(), new(StaticSymbol.GlobalUri), trivia));

        Assert.IsNull(thrown, thrown?.ToString());
        Assert.AreEqual(4, Assert.ContainsSingle(deadRanges).Start.Line, "the #Else branch");
    }

    // what is nested past the limit (VBC00060) is an error node of its text, which is not evaluated: the condition has no value, neither branch is dead, and
    // the syntax error is the module's only error.
    [TestMethod]
    public void ACondition_PastTheLimit_IsNotEvaluated_AndNoBranchIsDead()
    {
        var parse = ParseSub("Sub Foo()\r\n#If DEBUGMODE Then\r\nx = 1\r\n#Else\r\nx = 2\r\n#End If\r\nEnd Sub\r\n");
        var trivia = WithCondition(parse.PrecompilerTrivia, KeptAsText);
        var session = ComposeSession(new PrecompilerConstantSymbol("DEBUGMODE", new VBIntegerValue(1)));

        var evaluation = PrecompilerLiveBranchEvaluator.Evaluate(session, Evaluator(), new(StaticSymbol.GlobalUri), trivia);

        Assert.IsEmpty(evaluation.DeadRanges);
        Assert.IsEmpty(evaluation.Errors);
    }

    // a #Const whose value is past the limit has no value either, and no more does a condition that names it, or names a constant that does.
    [TestMethod]
    [DataRow("#Const L = 1\r\nSub Foo()\r\n#If L Then\r\nx = 1\r\n#Else\r\nx = 2\r\n#End If\r\nEnd Sub\r\n", DisplayName = "named by the condition")]
    [DataRow("#Const L = 1\r\n#Const M = L\r\nSub Foo()\r\n#If M Then\r\nx = 1\r\n#Else\r\nx = 2\r\n#End If\r\nEnd Sub\r\n", DisplayName = "named by another constant")]
    public void AConstant_PastTheLimit_HasNoValue_AndNoBranchIsDead(string source)
    {
        var parse = ParseSub(source);
        var trivia = WithConstantValue(parse.PrecompilerTrivia, "L", KeptAsText);

        var evaluation = PrecompilerLiveBranchEvaluator.Evaluate(ComposeSession(), Evaluator(), new(StaticSymbol.GlobalUri), trivia);

        Assert.IsEmpty(evaluation.DeadRanges);
        Assert.IsEmpty(evaluation.Errors);
    }

    // an expression kept as text, at the place of one the parser built: what the parser makes of one nested past the limit.
    private static ExpressionNode KeptAsText(ExpressionNode expression)
        => new UnbuiltExpressionTriviaNode(expression.Identity, expression.Location, "<past the limit>", []);

    // a chain of a binary operator `levels` deep, its left operand the innermost: `x + 0 + 0 …`, as the directive parse builds it.
    private static ExpressionNode Chain(VBBinaryOperatorExpressionNode binary, int levels)
    {
        var chain = binary.Left;
        for (var level = 0; level < levels; level++)
        {
            chain = binary with { Children = [chain, binary.Right] };
        }

        return chain;
    }

    // the directives of a module, with the condition of its #If replaced.
    private static ImmutableArray<SyntaxNode> WithCondition(ImmutableArray<SyntaxNode> trivia, Func<ExpressionNode, ExpressionNode> replace)
        => WithChildReplaced(trivia, node => node is PrecompilerInlineIfStatementNode, 0, replace);

    // the directives of a module, with the value of the #Const of a name replaced.
    private static ImmutableArray<SyntaxNode> WithConstantValue(ImmutableArray<SyntaxNode> trivia, string name, Func<ExpressionNode, ExpressionNode> replace)
        => WithChildReplaced(trivia, node => node is PrecompilerConstantDeclarationNode { Children: [PrecompilerNameExpressionNode declared, ..] } && declared.Name == name, 1, replace);

    // the directives of a module, with the expression at `index` of the one directive that is `target` replaced.
    private static ImmutableArray<SyntaxNode> WithChildReplaced(ImmutableArray<SyntaxNode> trivia, Func<SyntaxNode, bool> target, int index,
        Func<ExpressionNode, ExpressionNode> replace)
    {
        var replaced = 0;
        SyntaxNode Rewrite(SyntaxNode node)
        {
            if (target(node))
            {
                replaced++;
                return node with { Children = node.Children.SetItem(index, replace((ExpressionNode)node.Children[index])) };
            }

            return node is ExpressionNode || node.Children.IsDefaultOrEmpty ? node : node with { Children = [.. node.Children.Select(Rewrite)] };
        }

        var rewritten = trivia.Select(Rewrite).ToImmutableArray();
        Assert.AreEqual(1, replaced, "the directive the test replaces an expression of");
        return rewritten;
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
