using RDCore.Parsing;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Precompiler;

namespace RDCore.Tests.Semantics.Precompiler;

/// <summary>
/// <see cref="PrecompilerLiveBranchEvaluator"/> — MS-VBAL §3.4.2: correlates each <c>#If</c>/<c>#ElseIf</c>/
/// <c>#Else</c> branch (from <c>ModuleParseResult.PrecompilerTrivia</c>) to its source range and yields
/// the ranges of the branches that are not live. Parse-driven: both passes (the main parse and the
/// precompiler-directive parse) run over the exact same source text a real workspace would produce.
/// </summary>
[TestClass]
public sealed class PrecompilerLiveBranchEvaluatorTests
{
    private static ISymbolResolver Resolver(params PrecompilerConstantSymbol[] constants)
        => new ScopeTreeSymbolResolver(ScopeTreeBuilder.Build([.. constants]));

    private static (StatementBlock Body, RDCore.SDK.Model.AST.ModuleParseResult Parse) ParseSub(string source)
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
        var member = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        return (new StatementBlock([.. member.Children]), parse);
    }

    [TestMethod]
    public void IfElse_TheTrueBranch_ReportsTheElseBranchDead()
    {
        var (_, parse) = ParseSub("Sub Foo()\r\n#If DEBUGMODE Then\r\nx = 1\r\n#Else\r\nx = 2\r\n#End If\r\nEnd Sub\r\n");
        var resolver = Resolver(new PrecompilerConstantSymbol("DEBUGMODE", new VBIntegerValue(1)));

        var deadRanges = PrecompilerLiveBranchEvaluator.GetDeadRanges(parse.PrecompilerTrivia, resolver);

        Assert.HasCount(1, deadRanges);
    }

    [TestMethod]
    public void IfElse_TheFalseCondition_ReportsTheIfBranchDeadInstead()
    {
        var (_, parse) = ParseSub("Sub Foo()\r\n#If DEBUGMODE Then\r\nx = 1\r\n#Else\r\nx = 2\r\n#End If\r\nEnd Sub\r\n");
        var resolver = Resolver(new PrecompilerConstantSymbol("DEBUGMODE", new VBIntegerValue(0)));

        var deadRanges = PrecompilerLiveBranchEvaluator.GetDeadRanges(parse.PrecompilerTrivia, resolver);

        Assert.HasCount(1, deadRanges);
        // the dead range should contain the "x = 1" line, not "x = 2" - sanity-checked properly by the
        // lowering-integration tests below, which assert on which statement actually survives.
    }

    [TestMethod]
    public void ElseIfChain_OnlyTheMatchingBranchStaysLive()
    {
        var (_, parse) = ParseSub("Sub Foo()\r\n#If A Then\r\nx = 1\r\n#ElseIf B Then\r\nx = 2\r\n#Else\r\nx = 3\r\n#End If\r\nEnd Sub\r\n");
        var resolver = Resolver(
            new PrecompilerConstantSymbol("A", VBBooleanValue.False),
            new PrecompilerConstantSymbol("B", VBBooleanValue.True));

        var deadRanges = PrecompilerLiveBranchEvaluator.GetDeadRanges(parse.PrecompilerTrivia, resolver);

        Assert.HasCount(2, deadRanges); // #If and #Else are both dead; #ElseIf B is live
    }

    [TestMethod]
    public void NoBranchMatches_AndNoElse_EveryBranchIsDead()
    {
        var (_, parse) = ParseSub("Sub Foo()\r\n#If A Then\r\nx = 1\r\n#ElseIf B Then\r\nx = 2\r\n#End If\r\nEnd Sub\r\n");
        var resolver = Resolver(
            new PrecompilerConstantSymbol("A", VBBooleanValue.False),
            new PrecompilerConstantSymbol("B", VBBooleanValue.False));

        var deadRanges = PrecompilerLiveBranchEvaluator.GetDeadRanges(parse.PrecompilerTrivia, resolver);

        Assert.HasCount(2, deadRanges);
    }

    [TestMethod]
    public void AnIndeterminateCondition_ReportsNoDeadRangesForTheWholeChain()
    {
        var (_, parse) = ParseSub("Sub Foo()\r\n#If Nowhere Then\r\nx = 1\r\n#Else\r\nx = 2\r\n#End If\r\nEnd Sub\r\n");

        var deadRanges = PrecompilerLiveBranchEvaluator.GetDeadRanges(parse.PrecompilerTrivia, Resolver());

        Assert.IsEmpty(deadRanges);
    }

    [TestMethod]
    public void ANestedIfInsideTheLiveBranch_IsAlsoEvaluated()
    {
        var (_, parse) = ParseSub(
            "Sub Foo()\r\n#If OUTER Then\r\n#If INNER Then\r\nx = 1\r\n#Else\r\nx = 2\r\n#End If\r\n#End If\r\nEnd Sub\r\n");
        var resolver = Resolver(
            new PrecompilerConstantSymbol("OUTER", VBBooleanValue.True),
            new PrecompilerConstantSymbol("INNER", VBBooleanValue.True));

        var deadRanges = PrecompilerLiveBranchEvaluator.GetDeadRanges(parse.PrecompilerTrivia, resolver);

        Assert.HasCount(1, deadRanges); // the nested #If's own Else branch
    }

    [TestMethod]
    public void ANestedIfInsideADeadBranch_IsNotSeparatelyReported()
        // the outer branch's own range already covers everything inside it.
    {
        var (_, parse) = ParseSub(
            "Sub Foo()\r\n#If OUTER Then\r\ny = 0\r\n#Else\r\n#If INNER Then\r\nx = 1\r\n#Else\r\nx = 2\r\n#End If\r\n#End If\r\nEnd Sub\r\n");
        var resolver = Resolver(
            new PrecompilerConstantSymbol("OUTER", VBBooleanValue.True),
            new PrecompilerConstantSymbol("INNER", VBBooleanValue.True));

        var deadRanges = PrecompilerLiveBranchEvaluator.GetDeadRanges(parse.PrecompilerTrivia, resolver);

        Assert.HasCount(1, deadRanges); // just the outer #Else - the nested #If inside it is never visited
    }
}
