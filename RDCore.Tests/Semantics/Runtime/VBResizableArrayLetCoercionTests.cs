using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Characterization matrix for let-coercion to a resizable array (MS-VBAL §5.5.1.2.7): an array of the destination's
/// element type is copied — bounds and rank preserved, elements owned by the copy — and any other source is a Type
/// mismatch. The Byte() rows (§5.5.1.2.6) are another strategy's.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.5.1.2.7 Let-coercion to and from a resizable array")]
public sealed class VBResizableArrayLetCoercionTests : LetCoercionRuntimeSemanticsTests
{
    private static readonly Uri Root = new("file://rdcore-test");

    private static VBResizableArrayLetCoercionRuntimeSemantics Sut() => new(FakeProvider(), Formatter());

    private static VBResizableArrayType LongArray => new(VBLongType.TypeInfo);

    private static VBResizableArrayValue LongsOf(int lower, params int[] values)
    {
        var array = new VBResizableArrayValue([(lower, lower + values.Length - 1)], VBLongType.TypeInfo);
        for (var i = 0; i < values.Length; i++)
        {
            array.TrySetElement(new ValueBindingHandle(new VBRuntimeValue<int>(values[i])), lower + i);
        }
        return array;
    }

    private static int[] LongsIn(LetCoercionResult result, int lower, int count)
        => [.. Enumerable.Range(lower, count).Select(i => ((VBLongValue)((VBArrayValue)result.Result!)[i]!).Value)];

    [TestMethod]
    [DataRow(5, DisplayName = "numeric")]
    [DataRow("text", DisplayName = "String")]
    [DataRow(true, DisplayName = "Boolean")]
    public void ScalarSource_IntoAnArrayTarget_IsTypeMismatch(object scalar)
        => AssertError(Coerce(Sut(), scalar switch
        {
            int i => new VBLongValue(i),
            string s => new VBStringValue(s),
            _ => VBBooleanValue.True,
        }, LongArray), VBRuntimeErrorId.TypeMismatch);

    [TestMethod]
    public void NumericSource_IntoArrayTarget_IsTypeMismatch()
        => AssertError(Coerce(Sut(), new VBLongValue(5), VBResizableArrayType.TypeInfo), VBRuntimeErrorId.TypeMismatch);

    [TestMethod]
    public void DateSource_IntoAnArrayTarget_IsTypeMismatch()
        => AssertError(Coerce(Sut(), new VBDateValue(2), LongArray), VBRuntimeErrorId.TypeMismatch);

    [TestMethod]
    public void ArraySource_OfTheSameElementType_IsCopied_BoundsPreserved()
    {
        var result = Coerce(Sut(), LongsOf(5, 10, 20, 30), LongArray);

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        var copy = Assert.IsInstanceOfType<VBResizableArrayValue>(result.Result);
        Assert.AreEqual((5, 7), (copy.Dimensions[0].LowerBound, copy.Dimensions[0].UpperBound));
        CollectionAssert.AreEqual(new[] { 10, 20, 30 }, LongsIn(result, 5, 3));
    }

    [TestMethod]
    public void TheCopy_OwnsItsElements()
        // a Let-assigned element is a value: writing the source afterwards never writes the copy.
    {
        var source = LongsOf(0, 1, 2);

        var result = Coerce(Sut(), source, LongArray);
        source.TrySetElement(new ValueBindingHandle(new VBRuntimeValue<int>(99)), 0);

        CollectionAssert.AreEqual(new[] { 1, 2 }, LongsIn(result, 0, 2));
    }

    [TestMethod]
    public void AMultiDimensionalSource_KeepsItsRankAndBounds_AndEveryElement()
    {
        var source = new VBResizableArrayValue([(1, 2), (0, 1)], VBLongType.TypeInfo);
        source.TrySetElement(new ValueBindingHandle(new VBRuntimeValue<int>(11)), 1, 0);
        source.TrySetElement(new ValueBindingHandle(new VBRuntimeValue<int>(12)), 2, 0);
        source.TrySetElement(new ValueBindingHandle(new VBRuntimeValue<int>(21)), 1, 1);
        source.TrySetElement(new ValueBindingHandle(new VBRuntimeValue<int>(22)), 2, 1);

        var copy = Assert.IsInstanceOfType<VBResizableArrayValue>(Coerce(Sut(), source, LongArray).Result);

        Assert.AreEqual(2, copy.Rank);
        Assert.AreEqual(((1, 2), (0, 1)), (
            (copy.Dimensions[0].LowerBound, copy.Dimensions[0].UpperBound), (copy.Dimensions[1].LowerBound, copy.Dimensions[1].UpperBound)));
        Assert.AreEqual(11, ((VBLongValue)copy[1, 0]!).Value);
        Assert.AreEqual(12, ((VBLongValue)copy[2, 0]!).Value);
        Assert.AreEqual(21, ((VBLongValue)copy[1, 1]!).Value);
        Assert.AreEqual(22, ((VBLongValue)copy[2, 1]!).Value);
    }

    [TestMethod]
    public void AFixedSizeSource_IsCopiedToTheResizableDestination()
    {
        var source = new VBFixedSizeArrayValue([(0, 1)], VBLongType.TypeInfo);
        source.TrySetElement(new ValueBindingHandle(new VBRuntimeValue<int>(7)), 0);
        source.TrySetElement(new ValueBindingHandle(new VBRuntimeValue<int>(8)), 1);

        var result = Coerce(Sut(), source, LongArray);

        Assert.IsInstanceOfType<VBResizableArrayValue>(result.Result);
        CollectionAssert.AreEqual(new[] { 7, 8 }, LongsIn(result, 0, 2));
    }

    [TestMethod]
    public void AnUninitializedSource_IsAnUninitializedCopy()
    {
        var result = Coerce(Sut(), new VBResizableArrayValue([], VBLongType.TypeInfo), LongArray);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(((VBArrayValue)result.Result!).IsInitialized);
    }

    [TestMethod]
    public void ElementsNeverAssigned_CopyWithoutBeingRead()
        // a Variant cell that was never assigned has no readable binding; it stays inert in the copy.
        => Assert.IsTrue(Coerce(Sut(), new VBResizableArrayValue([(0, 2)], VBVariantType.TypeInfo), VBResizableArrayType.TypeInfo).IsSuccess);

    [TestMethod]
    public void ArraySource_OfAnotherElementType_IsTypeMismatch()
        => AssertError(Coerce(Sut(), new VBResizableArrayValue([(0, 0)], VBIntegerType.TypeInfo), LongArray), VBRuntimeErrorId.TypeMismatch);

    [TestMethod]
    public void ArraySource_OfLongs_IntoAVariantArray_IsTypeMismatch()
        // "Array with same element type as source": a Variant() is not a Long().
        => AssertError(Coerce(Sut(), LongsOf(0, 1), VBResizableArrayType.TypeInfo), VBRuntimeErrorId.TypeMismatch);

    [TestMethod]
    public void AByteArraySource_IntoANonByteArray_IsTypeMismatch()
        => AssertError(Coerce(Sut(), new VBResizableByteArrayValue([(0, 1)]), LongArray), VBRuntimeErrorId.TypeMismatch);

    [TestMethod]
    public void FixedLengthStrings_OfDifferentLengths_AreDifferentElementTypes()
    {
        var source = new VBResizableArrayValue([(0, 0)], new VBFixedStringType(5));

        Assert.IsTrue(Coerce(Sut(), source, new VBResizableArrayType(new VBFixedStringType(5))).IsSuccess);
        AssertError(Coerce(Sut(), source, new VBResizableArrayType(new VBFixedStringType(6))), VBRuntimeErrorId.TypeMismatch);
    }

    private static VBClassType ClassNamed(string name)
        => new(new VBClassModuleSymbol(Root, Root, name), []);

    [TestMethod]
    public void ClassElements_AreSetAssigned_TheCopyHoldsTheSameReferenceInABindingOfItsOwn()
        // MS-VBAL 5.5.1.2.7: an element whose value type is a class is Set-assigned - the reference is copied, the
        // object is not - and rebinding the copy's element never rebinds the source's.
    {
        var widget = ClassNamed("Widget");
        var instance = new VBRuntimeValue<string>("the-object");
        var source = new VBResizableArrayValue([(0, 0)], widget);
        var sourceBinding = new ValueBindingHandle(instance);
        source.TrySetElement(sourceBinding, 0);

        var copy = Assert.IsInstanceOfType<VBArrayValue>(Coerce(Sut(), source, new VBResizableArrayType(widget)).Result);
        var copyBinding = copy.GetElementHandle(0)!;

        Assert.AreEqual(instance, copyBinding.Value, "the same referenced object");
        Assert.AreNotSame(sourceBinding, copyBinding, "a binding of its own");
    }

    [TestMethod]
    public void ClassElementTypes_AreTheSameWhenTheyAreTheSameClass()
    {
        var source = new VBResizableArrayValue([(0, 0)], ClassNamed("Widget"));

        Assert.IsTrue(Coerce(Sut(), source, new VBResizableArrayType(ClassNamed("Widget"))).IsSuccess);
        AssertError(Coerce(Sut(), source, new VBResizableArrayType(ClassNamed("Gadget"))), VBRuntimeErrorId.TypeMismatch);
    }

    [TestMethod]
    public void UserDefinedElementTypes_OfOneModule_AreToldApartByTheirDeclaration()
        // both symbols share the module's uri and differ only in the fragment, which Uri equality ignores.
    {
        var module = new Uri("file://rdcore-test#Mod");
        VBUserDefinedType Udt(string name)
            => new(new VBUserDefinedTypeMemberSymbol(Root, module, name, ScopeKind.Module, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public), []);
        var source = new VBResizableArrayValue([(0, 0)], Udt("Point"));

        Assert.IsTrue(Coerce(Sut(), source, new VBResizableArrayType(Udt("Point"))).IsSuccess);
        AssertError(Coerce(Sut(), source, new VBResizableArrayType(Udt("Size"))), VBRuntimeErrorId.TypeMismatch);
    }

    [TestMethod]
    public void ASourceThisStrategyDoesNotHandle_IsNotApplicable()
        => Assert.IsFalse(Coerce(Sut(), new VBNullValue(), LongArray).IsApplicable);

    [TestMethod]
    public void ADestinationThatIsNotAResizableArray_IsNotApplicable()
        => Assert.IsFalse(Coerce(Sut(), LongsOf(0, 1), VBLongType.TypeInfo).IsApplicable);

    [TestMethod]
    public void Analyze_AddsArrayTargetFlag()
    {
        var frame = new LetCoercionStackFrame(NodeId, InputIndex.CoercionSourceValue, new VBLongValue(5), new VBTypeDescValue(VBResizableArrayType.TypeInfo));
        var builder = new LetCoercionSemanticContextFlagsBuilder();

        Sut().Analyze(builder, null!, ThrowawayExpression, frame, LetCoercionResult.NotApplicable(frame));

        Assert.IsTrue(builder.Flags.HasFlag(ConversionSemanticFlags.ArrayTarget));
    }
}
