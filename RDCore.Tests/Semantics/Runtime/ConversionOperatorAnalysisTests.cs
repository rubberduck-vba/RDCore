using NSubstitute;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// The analysis of the operators that are conversions (RD-VBAL 5.6.9.9, MS-VBAL 5.4.3.8): what an assignment and an explicit
/// let-coercion report of the coercion they perform. The coercion of an operand is implicit, unless it is the explicit
/// let-coercion operator that asks for it.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL 5.6.9.9 Let-coercion operator (analysis)")]
public sealed class ConversionOperatorAnalysisTests : LetCoercionRuntimeSemanticsTests
{
    private static readonly Uri Root = new("file://rdcore-test");

    private const ConversionSemanticFlags Coerced = ConversionSemanticFlags.LetCoerced | ConversionSemanticFlags.CTypeAvailable | ConversionSemanticFlags.Numeric;

    private sealed class Provider(Symbol[] symbols) : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => symbols;
    }

    private static (IRuntimeSession Session, VBModuleFieldVariableMemberSymbol Field) SessionWithField(VBType type)
    {
        var module = new VBStandardModuleSymbol(Root, Root, "Mod1");
        var field = new VBModuleFieldVariableMemberSymbol(Root, module.Uri, "Total", ScopeKind.Module, type, SourceRange.Empty, SourceRange.Empty, AccessModifier.Implicit);
        var session = RuntimeSessionComposer.Compose(new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), new Provider([module, field]));
        return (session, field);
    }

    private static SemanticContextBuilder<ConversionOperationSemanticContext, ConversionSemanticFlags> Builder()
        => new(Substitute.For<ICoreDiagnosticsFactory>());

    private static BinaryLetAssignmentOperatorRuntimeSemantics Assignment()
        => new(LetCoercionAnalysisHarness.BuildProvider(), Formatter());

    private static ConversionOperationSemanticContext Assign(IRuntimeSession session, Symbol target, VBTypedValue source)
    {
        var builder = Builder();
        Assignment().Analyze(session, new ConversionOperationSemanticContext(), builder, ThrowawayExpression, new VBSymbolDescValue(target), source);
        return builder.Build();
    }

    private static ConversionOperationSemanticContext Coerce(VBTypedValue source, VBType target)
    {
        var builder = Builder();
        new BinaryLetCoerceOperatorRuntimeSemantics(LetCoercionAnalysisHarness.BuildProvider(), Formatter())
            .Analyze(SessionWithField(VBLongType.TypeInfo).Session, new ConversionOperationSemanticContext(), builder, ThrowawayExpression, source, new VBTypeDescValue(target));
        return builder.Build();
    }

    #region let-assignment

    [TestMethod]
    public void AnAssignmentOfTheSameType_ConvertsNothing_AndReportsNothing()
    {
        var (session, field) = SessionWithField(VBLongType.TypeInfo);

        Assert.AreEqual((ConversionSemanticFlags)0, Assign(session, field, new VBLongValue(5)).Flags);
    }

    [TestMethod]
    public void AnAssignmentOfAnIntegral_ToAFloatingPoint_IsAnImplicitWideningConversion()
    {
        var (session, field) = SessionWithField(VBDoubleType.TypeInfo);

        Assert.AreEqual(
            Coerced | ConversionSemanticFlags.Implicit | ConversionSemanticFlags.Widening | ConversionSemanticFlags.BinaryRightOperand,
            Assign(session, field, new VBLongValue(5)).Flags);
    }

    [TestMethod]
    public void AnAssignmentOfAFraction_ToAnIntegral_IsAnImplicitNarrowingConversion_ThatRounds()
    {
        var (session, field) = SessionWithField(VBLongType.TypeInfo);

        Assert.AreEqual(
            Coerced | ConversionSemanticFlags.Implicit | ConversionSemanticFlags.BinaryRightOperand
                | ConversionSemanticFlags.Narrowing | ConversionSemanticFlags.Lossy | ConversionSemanticFlags.BankersRounding,
            Assign(session, field, new VBDoubleValue(2.67)).Flags);
    }

    [TestMethod]
    public void AnAssignmentOfAWiderIntegral_ToANarrowerOne_IsNarrowing()
    {
        var (session, field) = SessionWithField(VBIntegerType.TypeInfo);

        Assert.AreEqual(
            Coerced | ConversionSemanticFlags.Implicit | ConversionSemanticFlags.BinaryRightOperand | ConversionSemanticFlags.Narrowing,
            Assign(session, field, new VBLongValue(5)).Flags);
    }

    [TestMethod]
    public void AnAssignmentIsNeverExplicit()
    {
        var (session, field) = SessionWithField(VBDoubleType.TypeInfo);

        Assert.IsFalse(Assign(session, field, new VBLongValue(5)).Flags.HasFlag(ConversionSemanticFlags.Explicit));
    }

    [TestMethod]
    public void AnAssignmentOfANull_ToALong_IsFlaggedAsANullOperand_AndReportsTheError()
    {
        var (session, field) = SessionWithField(VBLongType.TypeInfo);

        var context = Assign(session, field, VBNullValue.Null);

        Assert.IsTrue(context.Flags.HasFlag(ConversionSemanticFlags.NullOperand | ConversionSemanticFlags.Implicit));
        Assert.HasCount(1, context.Errors);
    }

    [TestMethod]
    public void AnAssignmentOfAnObject_ToALong_IsFlaggedAsAnObjectOperand()
    {
        var (session, field) = SessionWithField(VBLongType.TypeInfo);

        Assert.IsTrue(Assign(session, field, new VBObjectValue(new VBRuntimeObjectId())).Flags.HasFlag(ConversionSemanticFlags.ObjectOperand));
    }

    [TestMethod]
    public void AnAssignmentOfAnEmpty_ToALong_IsFlaggedAsAnEmptyOperand()
    {
        var (session, field) = SessionWithField(VBLongType.TypeInfo);

        Assert.IsTrue(Assign(session, field, VBEmptyValue.Empty).Flags.HasFlag(ConversionSemanticFlags.EmptyOperand));
    }

    [TestMethod]
    public void AnalyzingAnAssignment_DoesNotPerformIt()
    {
        var (session, field) = SessionWithField(VBLongType.TypeInfo);
        var before = session.Symbols.Resolver.GetValue(field).Value.BoxedValue;

        Assign(session, field, new VBDoubleValue(2.67));

        Assert.AreEqual(before, session.Symbols.Resolver.GetValue(field).Value.BoxedValue);
    }

    [TestMethod]
    public void AnAssignmentThatFails_ReportsTheErrorEvaluatingItRaises()
    {
        var (session, field) = SessionWithField(VBIntegerType.TypeInfo);
        var source = new VBLongValue(100_000);

        var raised = Assignment().Evaluate(session, new ConversionOperationSemanticContext(), ThrowawayExpression, new VBSymbolDescValue(field), source).ErrorInfo?.ErrorId;
        var analyzed = Assign(session, field, source).Errors.SingleOrDefault()?.ErrorId;

        Assert.AreEqual((int)VBRuntimeErrorId.Overflow, raised);
        Assert.AreEqual(raised, analyzed);
    }

    #endregion

    #region explicit let-coercion

    [TestMethod]
    public void AnExplicitLetCoercion_IsExplicit_AndItsOperandIsCoercedExplicitly()
        => Assert.AreEqual(
            Coerced | ConversionSemanticFlags.Explicit | ConversionSemanticFlags.Widening | ConversionSemanticFlags.BinaryLeftOperand,
            Coerce(new VBLongValue(5), VBDoubleType.TypeInfo).Flags);

    [TestMethod]
    public void AnExplicitLetCoercion_IsNeverImplicit()
        => Assert.IsFalse(Coerce(new VBLongValue(5), VBDoubleType.TypeInfo).Flags.HasFlag(ConversionSemanticFlags.Implicit));

    [TestMethod]
    public void ARedundantExplicitLetCoercion_IsStillExplicit_AndConvertsNothing()
        => Assert.AreEqual(
            Coerced | ConversionSemanticFlags.Explicit | ConversionSemanticFlags.BinaryLeftOperand,
            Coerce(new VBLongValue(5), VBLongType.TypeInfo).Flags);

    [TestMethod]
    public void AnExplicitLetCoercionOfAFraction_ToAnIntegral_Rounds()
        => Assert.IsTrue(Coerce(new VBDoubleValue(2.67), VBLongType.TypeInfo).Flags
            .HasFlag(ConversionSemanticFlags.Explicit | ConversionSemanticFlags.Narrowing | ConversionSemanticFlags.BankersRounding));

    #endregion
}
