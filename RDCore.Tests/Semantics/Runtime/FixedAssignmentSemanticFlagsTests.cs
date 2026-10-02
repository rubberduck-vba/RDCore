using NSubstitute;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.Runtime.Semantics.Operators;
using RDCore.Runtime.Semantics.Statements;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// <strong>MS-VBAL §5.4.3.6-7</strong> the semantic flags of <c>LSet</c> and <c>RSet</c>, which <c>Analyze</c> sets and an analyzer reads: the
/// facts about a statement that is not wrong, and that a program written against MS-VBA should be told about.
/// </summary>
/// <remarks>
/// MS-VBA's <c>LSet</c> over a record that has a variable-length <c>String</c> member copies the member's pointer, and leaves two records owning one
/// allocation. RD-VBA copies the value, which corrupts nothing, and says so with a flag.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.6 LSet")]
public sealed class FixedAssignmentSemanticFlagsTests
{
    private static readonly SourceRange R = SourceRange.Empty;

    private static VBUserDefinedType Udt(string name, params (string Name, VBType Type)[] fields)
    {
        var uri = TestUri.TestModuleUserDefinedTypeUri(name);
        var symbol = new VBUserDefinedTypeMemberSymbol(uri, uri, name, ScopeKind.Module, R, R, AccessModifier.Public);
        return new VBUserDefinedType(symbol, [.. fields.Select(field =>
        {
            var fieldUri = TestUri.TestUserDefinedTypeMemberUri(field.Name, name);
            return (VBTypeMemberSymbol)new VBUserDefinedTypeFieldSymbol(fieldUri, fieldUri, field.Name, field.Type, R, R, AccessModifier.Public);
        })]);
    }

    private static FixedAssignmentRuntimeSemantics Semantics()
    {
        var formatter = Substitute.For<IVerboseMessageBuilder>();
        var strings = new VBStringLetCoercionRuntimeSemantics(formatter);
        var letCoercion = new LetCoercionRuntimeSemanticsProvider([strings], formatter);
        var expressions = new RuntimeExpressionEvaluator(new OperatorRuntimeSemanticsProvider(letCoercion, formatter));
        return new FixedAssignmentRuntimeSemantics(expressions, strings, new LetAssignmentEvaluator(letCoercion, formatter, expressions));
    }

    private static FixedAssignmentSemanticFlags Analyze(AssignmentKind kind, VBTypedValue target, VBTypedValue source)
    {
        var node = new AssignmentStatementNode(
            new(TestUri.TestModuleUri().AbsolutePath, [1]), TestLocations.TestLocation, kind,
            new SimpleNameExpressionNode(new(TestUri.TestModuleUri().AbsolutePath, [2]), TestLocations.TestLocation, "T"),
            new SimpleNameExpressionNode(new(TestUri.TestModuleUri().AbsolutePath, [3]), TestLocations.TestLocation, "S"));

        var builder = new SemanticContextFlagsBuilder<FixedAssignmentSemanticContext, FixedAssignmentSemanticFlags>();
        Semantics().Analyze(Substitute.For<IRuntimeSession>(), new ConversionOperationSemanticContext(), builder, node, target, source);
        return builder.Build().Flags;
    }

    private static VBTypedValue Record(VBUserDefinedType type) => type.DefaultValue;

    private static readonly VBUserDefinedType Plain = Udt("Plain", ("X", VBLongType.TypeInfo), ("Y", VBLongType.TypeInfo));
    private static readonly VBUserDefinedType Named = Udt("Named", ("Id", VBLongType.TypeInfo), ("Name", VBStringType.TypeInfo));
    private static readonly VBUserDefinedType FixedNamed = Udt("FixedNamed", ("Id", VBLongType.TypeInfo), ("Code", new VBFixedStringType(4)));

    [TestMethod]
    public void ARecordCopy_BetweenRecordsWithNoVariableLengthString_IsOnlyACopy()
        => Assert.AreEqual(FixedAssignmentSemanticFlags.UserDefinedTypeCopy, Analyze(AssignmentKind.LSet, Record(Plain), Record(Plain)));

    [TestMethod]
    public void ARecordCopy_FromARecordThatHoldsAVariableLengthString_FlagsTheSource()
        => Assert.AreEqual(
            FixedAssignmentSemanticFlags.UserDefinedTypeCopy | FixedAssignmentSemanticFlags.SourceHoldsVariableLengthString,
            Analyze(AssignmentKind.LSet, Record(Plain), Record(Named)));

    [TestMethod]
    public void ARecordCopy_IntoARecordThatHoldsAVariableLengthString_FlagsTheDestination()
        => Assert.AreEqual(
            FixedAssignmentSemanticFlags.UserDefinedTypeCopy | FixedAssignmentSemanticFlags.DestinationHoldsVariableLengthString,
            Analyze(AssignmentKind.LSet, Record(Named), Record(Plain)));

    [TestMethod]
    public void ARecordCopy_BetweenTwoRecordsThatHoldOne_FlagsBothSides()
        => Assert.AreEqual(
            FixedAssignmentSemanticFlags.UserDefinedTypeCopy
                | FixedAssignmentSemanticFlags.SourceHoldsVariableLengthString
                | FixedAssignmentSemanticFlags.DestinationHoldsVariableLengthString,
            Analyze(AssignmentKind.LSet, Record(Named), Record(Named)));

    [TestMethod]
    public void AVariableLengthString_InARecordInARecord_IsStillHeld()
    {
        var outer = Udt("Outer", ("Header", Plain), ("Body", Named));

        Assert.AreEqual(
            FixedAssignmentSemanticFlags.UserDefinedTypeCopy | FixedAssignmentSemanticFlags.SourceHoldsVariableLengthString,
            Analyze(AssignmentKind.LSet, Record(Plain), Record(outer)));
    }

    [TestMethod]
    public void AFixedLengthString_IsNotAVariableLengthOne()
        => Assert.AreEqual(FixedAssignmentSemanticFlags.UserDefinedTypeCopy, Analyze(AssignmentKind.LSet, Record(FixedNamed), Record(FixedNamed)));

    [TestMethod]
    [DataRow(AssignmentKind.LSet)]
    [DataRow(AssignmentKind.RSet)]
    public void AStringTarget_IsFlaggedAsTheStringForm(AssignmentKind kind)
        => Assert.AreEqual(FixedAssignmentSemanticFlags.StringTarget, Analyze(kind, new VBFixedStringValue(8), new VBStringValue("x")));

    [TestMethod]
    public void AVariantHoldingARecord_IsTheRecordItHolds()
        => Assert.AreEqual(
            FixedAssignmentSemanticFlags.UserDefinedTypeCopy | FixedAssignmentSemanticFlags.SourceHoldsVariableLengthString,
            Analyze(AssignmentKind.LSet, new VBVariantValue(Record(Plain)), new VBVariantValue(Record(Named))));

    [TestMethod]
    public void ATargetThatIsNeitherAStringNorARecord_IsFailed()
        => Assert.AreEqual(FixedAssignmentSemanticFlags.Failed, Analyze(AssignmentKind.LSet, new VBLongValue(1), new VBLongValue(2)));

    [TestMethod]
    public void RSet_OverRecords_IsFailed_BecauseOnlyLSetHasTheByteCopy()
        => Assert.AreEqual(FixedAssignmentSemanticFlags.Failed, Analyze(AssignmentKind.RSet, Record(Plain), Record(Plain)));
}
