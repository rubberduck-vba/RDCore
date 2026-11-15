using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Facts;
using System.Text;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// A golden master of let-coercion: every value of a broad set let-coerced to every type of another, the way an assignment asks for it - what each coercion
/// yields or raises, and the fact an analysis is told of it - compared line for line with what was recorded before the coercion plans were extracted from
/// the let-coercion provider. A line that differs is a change in behaviour.
/// </summary>
/// <remarks>
/// <para>
/// The coercions of the operands of an operator are recorded by the golden masters of the operators; these are the coercions of an assignment, whose
/// destination is any declared type: each intrinsic type, a fixed-length string, arrays, an Enum, a user-defined type and a class.
/// </para>
/// <para>
/// The object among the sources is a reference the session has no object for: what its default member would make of it is an internal error here.
/// </para>
/// <para>
/// The snapshot is <c>GoldenMasters/let-coercions.txt.gz</c>, next to this file, recorded and compared as <see cref="GoldenMaster"/> says.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.5.1.2 Let-coercion")]
public sealed class LetCoercionGoldenMasterTests : LetCoercionRuntimeSemanticsTests
{
    private sealed class NoSymbols : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => [];
    }

    private static readonly IRuntimeSession Session = RuntimeSessionComposer.Compose(
        new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, SupportsOptionCompareDatabase: true, OptionCompare.Text), new NoSymbols());

    private static readonly Uri Root = TestUri.WorkspaceRoot();

    private static readonly VBUserDefinedType Point = UserDefinedType("Point");

    private static readonly VBEnumType Colour = new(
        new VBEnumMemberSymbol(Root, TestUri.TestModuleUri(), "Colour", ScopeKind.Module, SymbolKindExt.Enum, VBUnknownType.TypeInfo,
            SourceRange.Empty, SourceRange.Empty, AccessModifier.Public), members: []);

    private static readonly VBClassType Widget = new(new VBClassModuleSymbol(Root, Root, "Widget"), []);

    // one object, which is not one of the session's.
    private static readonly VBObjectValue AnObject = new(new VBRuntimeObjectId());

    // each value type's default and another of its values, values that overflow or round, strings that read as a number, a Boolean or a date, the operands
    // only a Variant or an analysis has, an object, arrays and a user-defined type.
    private static VBTypedValue[] Sources() =>
    [
        new VBByteValue(0), new VBByteValue(255),
        new VBBooleanValue(false), new VBBooleanValue(true),
        new VBIntegerValue(0), new VBIntegerValue(-1), new VBIntegerValue(32767),
        new VBLongValue(7), new VBLongValue(70000),
        new VBLongLongValue(7), new VBLongLongValue(10000000000),
        new VBLongPtrValue(7L),
        new VBSingleValue(2.5f),
        new VBDoubleValue(0), new VBDoubleValue(2.5), new VBDoubleValue(3.5), new VBDoubleValue(-1), new VBDoubleValue(1e10), new VBDoubleValue(double.NaN),
        new VBCurrencyValue(2.5m),
        new VBDecimalValue(2.5m),
        new VBDateValue(0), new VBDateValue(36527.5),
        new VBStringValue(string.Empty), new VBStringValue("7"), new VBStringValue(" 2.5 "), new VBStringValue("1e3"), new VBStringValue("&H10"),
        new VBStringValue("True"), new VBStringValue("2000-01-02"), new VBStringValue("abcdef"), VBStringValue.FromBytes([0x41, 0x00, 0x42]),
        VBEmptyValue.Empty,
        VBNullValue.Null,
        new VBErrorValue(5),
        VBObjectValue.Nothing, AnObject,
        ByteArray(), new VBResizableArrayValue([(0, 1)], VBLongType.TypeInfo), new VBResizableArrayValue([(0, 1)]), new VBFixedSizeArrayValue([(0, 1)], VBLongType.TypeInfo),
        new VBUserDefinedTypeValue(Point),
        new VBVariantValue(new VBLongValue(7)), new VBVariantValue(new VBStringValue("7")), new VBVariantValue(VBEmptyValue.Empty), new VBVariantValue(VBNullValue.Null),
        new VBVariantValue(new VBErrorValue(5)), new VBVariantValue(AnObject), new VBVariantValue(ByteArray()), new VBVariantValue(new VBVariantValue(new VBLongValue(7))),
        VBLongType.TypeInfo.CreateIndeterminateValue(), VBStringType.TypeInfo.CreateIndeterminateValue(), VBVariantType.TypeInfo.CreateIndeterminateValue(),
    ];

    private static VBType[] Destinations() =>
    [
        VBByteType.TypeInfo, VBBooleanType.TypeInfo, VBIntegerType.TypeInfo, VBLongType.TypeInfo, VBLongLongType.TypeInfo, VBLongPtrType_x64.TypeInfo,
        VBSingleType.TypeInfo, VBDoubleType.TypeInfo, VBCurrencyType.TypeInfo, VBDecimalType.TypeInfo, VBDateType.TypeInfo,
        VBStringType.TypeInfo, new VBFixedStringType(3),
        VBVariantType.TypeInfo, VBObjectType.TypeInfo, VBErrorType.TypeInfo,
        VBResizableByteArrayType.TypeInfo, new VBResizableArrayType(VBLongType.TypeInfo), new VBResizableArrayType(VBVariantType.TypeInfo),
        new VBFixedSizeArrayType(VBLongType.TypeInfo),
        Colour, Point, Widget,
    ];

    // "A" (0x41 0x00), which a Byte() is the data of.
    private static VBResizableByteArrayValue ByteArray()
    {
        var array = new VBResizableByteArrayValue([(0, 1)]);
        array.TrySetElement(new VBByteValue(0x41).Handle, 0);
        return array;
    }

    private static VBUserDefinedType UserDefinedType(string name)
    {
        var uri = new Uri($"{Root}#{name}");
        return new VBUserDefinedType(new VBUserDefinedTypeMemberSymbol(uri, uri, name, ScopeKind.Module, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public), []);
    }

    [TestMethod]
    public void LetCoercion_DoesWhatItWasRecordedToDo() => GoldenMaster.AssertMatches(Record(), "let-coercions.txt.gz");

    private static string Record()
    {
        var text = new StringBuilder();
        foreach (var destination in Destinations())
        {
            foreach (var source in Sources())
            {
                // a provider owns its coercion stack: one per coercion. Observed, so that what an analysis is told of the coercion is recorded with it.
                var observer = new RecordingAnalysisObserver();
                var provider = LetCoercionAnalysisHarness.BuildProvider(new AnalysisObservation(observer));
                var expression = new LiteralExpressionNode(NodeId, TestLocations.TestLocation, new VBLongValue(1));

                var outcome = GoldenMaster.Outcome(() => provider.EvaluateLetCoercionSemantics(Session.Symbols.Resolver, expression,
                    new LetCoercionStackFrame(NodeId, InputIndex.CoercionSourceValue, source, new VBTypeDescValue(destination), ConversionSite.Assignment)));
                text.Append($"{GoldenMaster.Describe(source)} -> {GoldenMaster.Describe(destination)} => {outcome}{GoldenMaster.Describe(observer)}\n");
            }
        }

        return text.ToString().TrimEnd('\n');
    }
}
