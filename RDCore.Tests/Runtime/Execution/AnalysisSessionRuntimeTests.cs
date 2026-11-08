using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.External;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Facts;
using RDCore.Tests.Semantics.Runtime;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// A session composed to analyze code: it shares the declared symbols of another, holds the run-time state of its own, and starts a variable
/// that outlives an activation as a value that is not known (<see cref="RuntimeSessionComposer.ComposeForAnalysis"/>).
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.0.3 Semantic Analysis")]
public sealed class AnalysisSessionRuntimeTests
{
    private static readonly VBModuleFieldVariableMemberSymbol M = Variable("M", VBLongType.TypeInfo);
    private static readonly VBModuleFieldVariableMemberSymbol Y = Variable("Y", VBLongType.TypeInfo);
    private static readonly VBModuleFieldVariableMemberSymbol G = Variable("G", new VBFixedSizeArrayType(VBLongType.TypeInfo));

    private static VBModuleFieldVariableMemberSymbol Variable(string name, VBType type)
        => new(TestUri.WorkspaceRoot(), RuntimeSourceHarness.ModuleUri, name, ScopeKind.Module, type, SourceRange.Empty, SourceRange.Empty, AccessModifier.Implicit);

    private sealed class Provider(params Symbol[] symbols) : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => symbols;
    }

    private static IRuntimeSession Declared(params Symbol[] symbols)
        => RuntimeSessionComposer.Compose(new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), [], [new Provider(symbols)]);

    // what each assignment was told of the value it assigns, in source order.
    private static (RuntimeExecutionOutcome Outcome, bool[] Assigned) Analyze(params string[] body)
    {
        var observer = new RecordingAnalysisObserver();
        var (_, outcome) = RuntimeSourceHarness.Run(
            null, [M, Y, G], new RuntimeOutputBuffer(), standardLibrary: false, null, ModuleDirectives.None, observer, analyze: true, body);
        return (outcome, [.. observer.Conversions.Where(conversion => conversion.Site == ConversionSite.Assignment).Select(conversion => conversion.IsValueKnown)]);
    }

    [TestMethod]
    public void AModuleVariable_IsNotKnown_OnEntry()
        => CollectionAssert.AreEqual(new[] { false }, Analyze("Y = M").Assigned);

    [TestMethod]
    public void ALocalVariable_IsItsDeclaredDefault_AndKnown()
        => CollectionAssert.AreEqual(new[] { true }, Analyze("Dim n As Long", "Y = n").Assigned);

    [TestMethod]
    public void AStaticLocalVariable_IsNotKnown_OnEntry_ForItKeepsItsValueBetweenCalls()
        => CollectionAssert.AreEqual(new[] { false }, Analyze("Static s As Long", "Y = s").Assigned);

    [TestMethod]
    public void ALocalArray_IsItsDeclaredDefault_ItsElementsKnownZeros_AModuleArrayIsNot()
    {
        CollectionAssert.AreEqual(new[] { true }, Analyze("Dim L(1 To 3) As Long", "Y = L(2)").Assigned);
        CollectionAssert.AreEqual(new[] { false }, Analyze("Y = G(2)").Assigned);
    }

    [TestMethod]
    public void AValueTheCodeAssigns_IsKnown_ForTheRestOfTheStraightLine()
    {
        var run = Analyze("M = 0", "Y = 1 / M");

        Assert.AreEqual((int)RDCore.Runtime.LanguageCore.Errors.VBRuntimeErrorId.DivisionByZero, run.Outcome.ErrorInfo?.ErrorId, "M is 0 here, as the code made it");
    }

    [TestMethod]
    public void TheDeclaredSymbols_AreThoseOfTheMomentItWasComposed_AndALaterDefinitionIsNotSeen()
    {
        var declared = Declared(M);
        var analysis = RuntimeSessionComposer.ComposeForAnalysis(declared);

        Assert.AreNotSame(declared, analysis);
        Assert.IsTrue(analysis.Symbols.TryResolveValue("M", GlobalSymbols.UnresolvedSymbol, out var found) && found is not null, "what was declared is declared");

        Assert.IsTrue(declared.Symbols.TryDefine(Y, Y.ScopeKind));
        Assert.IsTrue(declared.Symbols.TryResolveValue("Y", GlobalSymbols.UnresolvedSymbol, out _), "the session it was composed over has it");
        Assert.IsFalse(analysis.Symbols.TryResolveValue("Y", GlobalSymbols.UnresolvedSymbol, out _), "the analysis does not: it can run while the session changes");
    }

    [TestMethod]
    public void WhatAnAnalysisDoes_NeverReachesTheSessionItIsComposedOver()
    {
        var declared = Declared(M);
        var analysis = RuntimeSessionComposer.ComposeForAnalysis(declared);
        _ = RuntimeExecutionPipeline.CreateForAnalysis(analysis, new Dictionary<SemanticId, RDCore.SDK.Semantics.Instructions.InstructionList>(),
            NSubstitute.Substitute.For<RDCore.SDK.Services.VerboseMessages.IVerboseMessageBuilder>(), new RecordingAnalysisObserver());

        var inAnalysis = analysis.Symbols.Resolver.GetValue(M);
        inAnalysis.Store(analysis.Symbols.Resolver, new VBRuntimeValue<int>(7), isKnown: true);

        var inDeclared = (IKnowledgeBinding)declared.Symbols.Resolver.GetValue(M);
        Assert.AreEqual(0, inDeclared.Value.BoxedValue, "the session over which the analysis was composed holds what it held");
        Assert.AreNotSame(inAnalysis, inDeclared);
    }

    [TestMethod]
    public void TheSessionAnAnalysisIsComposedOver_MustBeOneTheComposerComposed()
        => Assert.ThrowsExactly<ArgumentException>(() => RuntimeSessionComposer.ComposeForAnalysis(NSubstitute.Substitute.For<IRuntimeSession>()));

    [TestMethod]
    public void AFileStatement_ActsOnAFileSystemInMemory_NeverOnTheRealOne()
    {
        var path = $"rdcore-analysis-{Guid.NewGuid():N}.txt";

        var run = Analyze($"Open \"{path}\" For Output As #1", "Print #1, \"probe\"", "Close #1");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, run.Outcome.Kind, run.Outcome.ErrorInfo?.ToString());
        Assert.IsFalse(File.Exists(path), "nothing was written to disk");
        Assert.IsFalse(File.Exists(Path.Combine(Path.GetPathRoot(Environment.CurrentDirectory)!, path)), "nor to the root of it");
    }

    [TestMethod]
    public void ACallToTheOutsideWorld_IsNotMade_ItsResultIsNotKnown_AndWhatItWasPassedByReferenceIsNotKnownAfterwards()
    {
        var declared = Declared(M);
        var analysis = RuntimeSessionComposer.ComposeForAnalysis(declared);
        _ = RuntimeExecutionPipeline.CreateForAnalysis(analysis, new Dictionary<SemanticId, RDCore.SDK.Semantics.Instructions.InstructionList>(),
            NSubstitute.Substitute.For<RDCore.SDK.Services.VerboseMessages.IVerboseMessageBuilder>(), new RecordingAnalysisObserver());

        var cell = (IKnowledgeBinding)analysis.Symbols.Resolver.GetValue(M);
        cell.Store(new VBRuntimeValue<int>(7), isKnown: true);
        Assert.IsTrue(analysis.Symbols.Resolver.TryGetAddress(M, out var address));

        var declare = new VBExternalFunctionMemberSymbol(
            TestUri.WorkspaceRoot(), RuntimeSourceHarness.ModuleUri, "GetTickCount", ScopeKind.Module, SymbolKindExt.Function, VBLongType.TypeInfo,
            SourceRange.Empty, SourceRange.Empty, AccessModifier.Implicit, IsPtrSafe: true, "kernel32", null);
        var result = new OutsideWorldCallProvider().Dispatch(new ExternalCallRequest(declare, [new VBRuntimeReference(address)]), analysis.Symbols.Resolver);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.Result!.IsIndeterminate);
        Assert.IsInstanceOfType<VBLongValue>(result.Result);
        Assert.IsFalse(cell.IsKnown, "the callee could have written to it");
    }
}
