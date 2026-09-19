using NSubstitute;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics.Operators.Relational;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// The comparison mode of a module (MS-VBAL 5.2.1.1) decides how the relational operators compare <c>String</c> values
/// (MS-VBAL 5.6.9.5): by the code of each character in binary mode, regardless of case in text mode. The mode is the one of the
/// module the executing procedure is declared in, which the session says; <c>Option Compare Database</c> is what the platform says.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.2.1.1 Option Compare Directive")]
public sealed class OptionCompareTests : LetCoercionRuntimeSemanticsTests
{
    private static readonly StaticSymbol Procedure = new("DoWork", SymbolKindExt.Procedure, VBVoidType.TypeInfo);

    private sealed class NoSymbols : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => [];
    }

    private static IRuntimeSession SessionIn(OptionCompare? declared, OptionCompare databaseCompare = OptionCompare.Text)
    {
        var session = RuntimeSessionComposer.Compose(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, SupportsOptionCompareDatabase: true, databaseCompare), new NoSymbols());

        if (declared is { } compare)
        {
            var frame = session.Symbols.CreateFrame(new SyntaxNodeId("file://rdcore-test/Mod1.bas", [1]), Procedure, new ModuleDirectives(Compare: compare));
            Assert.IsTrue(session.CallStack.TryPush(frame));
        }

        return session;
    }

    private static BinaryEqRelationalOperatorRuntimeSemantics Eq() => new(LetCoercionAnalysisHarness.BuildProvider(), Formatter());
    private static BinaryLtRelationalOperatorRuntimeSemantics Lt() => new(LetCoercionAnalysisHarness.BuildProvider(), Formatter());

    private static bool Equal(IRuntimeSession session, string left, string right)
        => (bool)((VBBooleanValue)Eq().Evaluate(session, new BinaryOperatorSemanticContext<ComparisonOperatorSemanticFlags>(),
            ThrowawayExpression, new VBStringValue(left), new VBStringValue(right)).Result!).Value!;

    private static bool LessThan(IRuntimeSession session, string left, string right)
        => (bool)((VBBooleanValue)Lt().Evaluate(session, new BinaryOperatorSemanticContext<ComparisonOperatorSemanticFlags>(),
            ThrowawayExpression, new VBStringValue(left), new VBStringValue(right)).Result!).Value!;

    private static ComparisonOperatorSemanticFlags Analyze(IRuntimeSession session, string left, string right)
    {
        var builder = new SemanticContextBuilder<BinaryOperatorSemanticContext<ComparisonOperatorSemanticFlags>, ComparisonOperatorSemanticFlags>(
            Substitute.For<ICoreDiagnosticsFactory>());
        Eq().Analyze(session, new ConversionOperationSemanticContext(), builder, ThrowawayExpression, new VBStringValue(left), new VBStringValue(right));
        return builder.Build().Flags;
    }

    #region the mode of the executing code

    [TestMethod]
    public void WithNothingExecuting_TheModeIsBinary()
        => Assert.AreEqual(OptionCompare.Binary, SessionIn(declared: null).CurrentCompareMode());

    [TestMethod]
    [DataRow(OptionCompare.Binary)]
    [DataRow(OptionCompare.Text)]
    public void TheModeIsTheOneOfTheModuleOfTheExecutingProcedure(OptionCompare declared)
        => Assert.AreEqual(declared, SessionIn(declared).CurrentCompareMode());

    [TestMethod]
    public void TheModeIsTheOneOfTheProcedureThatIsExecuting_NotOfItsCaller()
    {
        var session = SessionIn(OptionCompare.Text);
        var callee = session.Symbols.CreateFrame(new SyntaxNodeId("file://rdcore-test/Mod2.bas", [1]), Procedure, new ModuleDirectives(Compare: OptionCompare.Binary));
        session.CallStack.TryPush(callee);

        Assert.AreEqual(OptionCompare.Binary, session.CurrentCompareMode());

        session.CallStack.TryPop(out _);

        Assert.AreEqual(OptionCompare.Text, session.CurrentCompareMode());
    }

    [TestMethod]
    [DataRow(OptionCompare.Text)]
    [DataRow(OptionCompare.Binary)]
    public void OptionCompareDatabase_IsWhatThePlatformSays(OptionCompare platform)
        => Assert.AreEqual(platform, SessionIn(OptionCompare.Database, platform).CurrentCompareMode());

    [TestMethod]
    public void OptionCompareDatabase_IsNeverTheModeAnOperationRunsIn()
        => Assert.AreNotEqual(OptionCompare.Database, SessionIn(OptionCompare.Database, OptionCompare.Database).CurrentCompareMode());

    #endregion

    #region evaluating a comparison

    [TestMethod]
    public void InBinaryMode_StringsThatDifferInCase_Differ()
        => Assert.IsFalse(Equal(SessionIn(OptionCompare.Binary), "a", "A"));

    [TestMethod]
    public void InTextMode_StringsThatDifferInCase_AreEqual()
        => Assert.IsTrue(Equal(SessionIn(OptionCompare.Text), "a", "A"));

    [TestMethod]
    public void WithNoModule_StringsAreComparedInBinaryMode()
        => Assert.IsFalse(Equal(SessionIn(declared: null), "a", "A"));

    [TestMethod]
    public void InBinaryMode_StringsAreOrderedByTheirCharacterCodes()
        // "B" is U+0042 and "a" is U+0061: every upper-case letter is before every lower-case one.
        => Assert.IsTrue(LessThan(SessionIn(OptionCompare.Binary), "B", "a"));

    [TestMethod]
    public void InTextMode_StringsAreOrderedRegardlessOfCase()
        => Assert.IsTrue(LessThan(SessionIn(OptionCompare.Text), "a", "B"));

    [TestMethod]
    public void OptionCompareDatabase_ComparesAsThePlatformSays()
    {
        Assert.IsTrue(Equal(SessionIn(OptionCompare.Database, OptionCompare.Text), "a", "A"));
        Assert.IsFalse(Equal(SessionIn(OptionCompare.Database, OptionCompare.Binary), "a", "A"));
    }

    #endregion

    #region analyzing a comparison

    [TestMethod]
    public void AComparisonInBinaryMode_IsFlaggedBinary()
        => Assert.AreEqual(
            ComparisonOperatorSemanticFlags.StringEffectiveType | ComparisonOperatorSemanticFlags.StringComparisonBinary,
            Analyze(SessionIn(OptionCompare.Binary), "a", "b"));

    [TestMethod]
    public void AComparisonInTextMode_IsFlaggedText()
        => Assert.AreEqual(
            ComparisonOperatorSemanticFlags.StringEffectiveType | ComparisonOperatorSemanticFlags.StringComparisonText,
            Analyze(SessionIn(OptionCompare.Text), "a", "b"));

    [TestMethod]
    public void AComparisonUnderOptionCompareDatabase_IsFlaggedAsThePlatformSays()
    {
        Assert.IsTrue(Analyze(SessionIn(OptionCompare.Database, OptionCompare.Text), "a", "b").HasFlag(ComparisonOperatorSemanticFlags.StringComparisonText));
        Assert.IsTrue(Analyze(SessionIn(OptionCompare.Database, OptionCompare.Binary), "a", "b").HasFlag(ComparisonOperatorSemanticFlags.StringComparisonBinary));
    }

    [TestMethod]
    public void AComparisonOfNumbers_HasNoStringComparisonMode()
    {
        var builder = new SemanticContextBuilder<BinaryOperatorSemanticContext<ComparisonOperatorSemanticFlags>, ComparisonOperatorSemanticFlags>(
            Substitute.For<ICoreDiagnosticsFactory>());
        Eq().Analyze(SessionIn(OptionCompare.Text), new ConversionOperationSemanticContext(), builder, ThrowawayExpression, new VBLongValue(1), new VBLongValue(2));

        Assert.AreEqual((ComparisonOperatorSemanticFlags)0, builder.Build().Flags & (ComparisonOperatorSemanticFlags.StringComparisonBinary | ComparisonOperatorSemanticFlags.StringComparisonText));
    }

    #endregion
}
