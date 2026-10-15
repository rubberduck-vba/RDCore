using RDCore.Runtime.Execution;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Facts;
using RDCore.SDK.Semantics.Flags;
using RDCore.Tests.Semantics.Runtime;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// <strong>MS-VBAL §5.2.3.4</strong> a variable or parameter declared with an Enum type, end to end: "the meaning is the
/// same as if the enum name was replaced with the declared type Long" — assigned like a <c>Long</c>, read back as one, and
/// passed as one.
/// </summary>
/// <remarks>
/// Both halves are needed for a variable to round-trip: the provider coerces to a <c>Long</c> what is coerced to an Enum
/// type (<c>LetCoercionRuntimeSemanticsProvider</c>), and the type reads what its storage holds back as a <c>Long</c>
/// (<see cref="VBEnumType.CreateValue"/>).
/// </remarks>
[TestClass]
[TestCategory("RD-VBAL §5.2.3.4 Enum Declarations")]
public sealed class EnumTypedStorageTests
{
    private static readonly Uri Workspace = TestUri.WorkspaceRoot();

    // Enum Colour: Red, Green.
    private static VBEnumType Colour()
    {
        var symbol = new VBEnumMemberSymbol(Workspace, RuntimeSourceHarness.ModuleUri, "Colour", ScopeKind.Module, SymbolKindExt.Enum,
            VBUnknownType.TypeInfo, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);
        return new VBEnumType(symbol, new[] { "Red", "Green" }.Select(member => new VBEnumConstMemberSymbol(
            Workspace, symbol.Uri, member, ScopeKind.Module, SymbolKindExt.EnumMember, SourceRange.Empty, SourceRange.Empty)));
    }

    private static VBModuleFieldVariableMemberSymbol Variable(string name, VBType type)
        => new(Workspace, RuntimeSourceHarness.ModuleUri, name, ScopeKind.Module, type,
            SourceRange.Empty, SourceRange.Empty, AccessModifier.Implicit);

    [TestMethod]
    public void AnEnumTypedVariable_IsAssignableAndReadable()
    {
        var c = Variable("C", Colour());
        var l = Variable("L", VBLongType.TypeInfo);
        var output = new RuntimeOutputBuffer();

        var (session, outcome) = RuntimeSourceHarness.Run(
            fileSystem: null, [c, l], output, standardLibrary: false,
            "C = 1", "Debug.Print C", "L = C + 1", "C = \"5\"");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual("1", output.Lines.Single().Trim());
        Assert.AreEqual(2, session.Symbols.Resolver.GetValue(l).Value.BoxedValue);
        // a String is Let-coerced to a Long, as it would be to any variable declared As Long.
        Assert.AreEqual(5, session.Symbols.Resolver.GetValue(c).Value.BoxedValue);
    }

    [TestMethod]
    public void AnAssignmentToAnEnumTypedVariable_IsAnalyzedAsTheCoercionToLong()
    {
        // what an analysis states about the coercion is what it does: the Long's rounding, and not a failure for want of a
        // strategy keyed on the Enum type.
        var c = Variable("C", Colour());
        var observer = new RecordingAnalysisObserver();

        var (_, outcome) = RuntimeSourceHarness.Run(
            fileSystem: null, [c], output: null, standardLibrary: false, arrange: null,
            new RuntimeSourceHarness.Surroundings { Observer = observer }, "C = 1.7");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        var fact = observer.Conversions.Single(conversion => conversion.Site == ConversionSite.Assignment);
        Assert.IsFalse(fact.Flags.HasFlag(ConversionSemanticFlags.Failed), observer.Describe());
        Assert.IsTrue(fact.Flags.HasFlag(ConversionSemanticFlags.BankersRounding), observer.Describe());
    }

    [TestMethod]
    public void APassedLiteral_ReachesAnEnumParameter()
    {
        // StrComp's Compare is declared As VbCompareMethod: the 1 is Let-coerced to it on the way in, and arrives as a
        // Long. What StrComp itself does with it is not this test's business — it is not written yet.
        RuntimeSourceHarness.RecordingDispatcher? spy = null;

        RuntimeSourceHarness.Run(
            fileSystem: null, [], output: null, standardLibrary: true, arrange: null,
            new RuntimeSourceHarness.Surroundings { Dispatcher = inner => spy = new RuntimeSourceHarness.RecordingDispatcher(inner) },
            "Debug.Print StrComp(\"a\", \"A\", 1)");

        Assert.IsNotNull(spy);
        Assert.AreEqual(1, spy.Requests.Single().Arguments[2].BoxedValue);
    }
}
