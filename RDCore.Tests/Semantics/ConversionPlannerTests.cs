using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.Runtime.LanguageCore.Plans;

namespace RDCore.Tests.Semantics;

/// <summary>
/// The plan of a let-coercion: the rule of MS-VBAL 5.5.1.2 that applies, decided from the type of the value and the type it is let-coerced
/// to, once. What each coercion yields is pinned by <c>LetCoercionGoldenMasterTests</c>; this is what a plan is.
/// </summary>
[TestClass]
public sealed class ConversionPlannerTests
{
    private static readonly Uri Root = TestUri.WorkspaceRoot();

    private static VBEnumType Colour() => new(
        new VBEnumMemberSymbol(Root, TestUri.TestModuleUri(), "Colour", ScopeKind.Module, SymbolKindExt.Enum, VBUnknownType.TypeInfo,
            SourceRange.Empty, SourceRange.Empty, AccessModifier.Public), members: []);

    [TestMethod]
    public void APlan_IsMadeOnce_ForEachPairOfTypes()
    {
        var first = ConversionPlanner.ForValue(VBStringType.TypeInfo, VBLongType.TypeInfo);
        Assert.AreSame(first, ConversionPlanner.ForValue(VBStringType.TypeInfo, VBLongType.TypeInfo));
        Assert.AreSame(first, ConversionPlanner.ForValue(VBStringType.TypeInfo, new VBLongType()), "an equal type in another instance is the same type");
    }

    [TestMethod]
    public void AValue_IsLetCoercedByTheRuleOfItsDestination()
    {
        Assert.AreEqual(ConversionRule.Numeric, ConversionPlanner.ForValue(VBStringType.TypeInfo, VBCurrencyType.TypeInfo).Rule);
        Assert.AreEqual(ConversionRule.Boolean, ConversionPlanner.ForValue(VBLongType.TypeInfo, VBBooleanType.TypeInfo).Rule);
        Assert.AreEqual(ConversionRule.Date, ConversionPlanner.ForValue(VBDoubleType.TypeInfo, VBDateType.TypeInfo).Rule);
        Assert.AreEqual(ConversionRule.String, ConversionPlanner.ForValue(VBDateType.TypeInfo, VBStringType.TypeInfo).Rule);
        Assert.AreEqual(ConversionRule.FixedString, ConversionPlanner.ForValue(VBLongType.TypeInfo, new VBFixedStringType(3)).Rule);
        Assert.AreEqual(ConversionRule.ByteArray, ConversionPlanner.ForValue(VBStringType.TypeInfo, VBResizableByteArrayType.TypeInfo).Rule);
        Assert.AreEqual(ConversionRule.Array, ConversionPlanner.ForValue(VBStringType.TypeInfo, new VBResizableArrayType(VBLongType.TypeInfo)).Rule);
        Assert.AreEqual(ConversionRule.Variant, ConversionPlanner.ForValue(VBLongType.TypeInfo, VBVariantType.TypeInfo).Rule);
        Assert.AreEqual(ConversionRule.Error, ConversionPlanner.ForValue(VBLongType.TypeInfo, VBErrorType.TypeInfo).Rule);
        Assert.IsFalse(ConversionPlanner.ForValue(VBLongType.TypeInfo, VBErrorType.TypeInfo).IsBySource);
    }

    [TestMethod]
    public void AnObject_Null_Empty_AndAnError_AreLetCoercedByRulesOfTheirOwn()
    {
        Assert.AreEqual(ConversionRule.FromObject, ConversionPlanner.ForValue(VBObjectType.TypeInfo, VBLongType.TypeInfo).Rule);
        Assert.AreEqual(ConversionRule.FromObject, ConversionPlanner.ForValue(VBObjectType.TypeInfo, VBVariantType.TypeInfo).Rule, "the default member of the object");
        Assert.AreEqual(ConversionRule.FromNull, ConversionPlanner.ForValue(VBNullType.TypeInfo, VBStringType.TypeInfo).Rule);
        Assert.AreEqual(ConversionRule.FromEmpty, ConversionPlanner.ForValue(VBEmptyType.TypeInfo, VBDateType.TypeInfo).Rule);
        Assert.AreEqual(ConversionRule.FromError, ConversionPlanner.ForValue(VBErrorType.TypeInfo, VBLongType.TypeInfo).Rule);
        Assert.IsTrue(ConversionPlanner.ForValue(VBErrorType.TypeInfo, VBLongType.TypeInfo).IsBySource);
    }

    [TestMethod]
    public void AVariant_AndAFixedSizeArray_TakeNull_Empty_AndAnError_ByTheirOwnRule()
    {
        // a Variant holds them as they are; a fixed-size array has no rule at all.
        Assert.AreEqual(ConversionRule.Variant, ConversionPlanner.ForValue(VBNullType.TypeInfo, VBVariantType.TypeInfo).Rule);
        Assert.IsTrue(ConversionPlanner.ForValue(VBEmptyType.TypeInfo, new VBFixedSizeArrayType(VBLongType.TypeInfo)).IsTypeMismatch);
    }

    [TestMethod]
    public void ADestinationNoRuleIsFor_IsATypeMismatch()
    {
        var root = new Uri("file://rdcore-test");
        Assert.IsTrue(ConversionPlanner.ForValue(VBLongType.TypeInfo, new VBFixedSizeArrayType(VBLongType.TypeInfo)).IsTypeMismatch);
        Assert.IsTrue(ConversionPlanner.ForValue(VBLongType.TypeInfo, new VBClassType(new VBClassModuleSymbol(root, root, "Widget"), [])).IsTypeMismatch);
    }

    [TestMethod]
    public void AnArray_AndAUserDefinedType_AreATypeMismatch_ToWhatTheirRulesHaveNoRowFor()
    {
        var longs = new VBResizableArrayType(VBLongType.TypeInfo);
        var point = new VBUserDefinedType(new VBUserDefinedTypeMemberSymbol(Root, Root, "Point", ScopeKind.Module, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public), []);

        // a Byte() is the data of a String, and nothing else's.
        Assert.AreEqual(ConversionRule.String, ConversionPlanner.ForValue(VBResizableByteArrayType.TypeInfo, VBStringType.TypeInfo).Rule);
        Assert.IsTrue(ConversionPlanner.ForValue(VBResizableByteArrayType.TypeInfo, VBLongType.TypeInfo).IsTypeMismatch);

        // an array of anything else is not even that; an array of the same elements, or a Variant, is the rule's to say.
        Assert.IsTrue(ConversionPlanner.ForValue(longs, VBStringType.TypeInfo).IsTypeMismatch);
        Assert.IsTrue(ConversionPlanner.ForValue(longs, VBResizableByteArrayType.TypeInfo).IsTypeMismatch);
        Assert.AreEqual(ConversionRule.Array, ConversionPlanner.ForValue(longs, longs).Rule);
        Assert.AreEqual(ConversionRule.Variant, ConversionPlanner.ForValue(longs, VBVariantType.TypeInfo).Rule);

        Assert.IsTrue(ConversionPlanner.ForValue(point, longs).IsTypeMismatch);
        Assert.AreEqual(ConversionRule.UserDefinedType, ConversionPlanner.ForValue(point, point).Rule);
    }

    [TestMethod]
    public void AnEnum_IsLetCoercedToAsALong_AndALongPtr_AsWhatItIsAnAliasOf()
    {
        var plan = ConversionPlanner.ForValue(VBStringType.TypeInfo, Colour());
        Assert.AreEqual(VBLongType.TypeInfo, plan.Destination);
        Assert.AreEqual(ConversionRule.Numeric, plan.Rule);

        Assert.AreEqual(VBLongLongType.TypeInfo, ConversionPlanner.ForValue(VBLongType.TypeInfo, VBLongPtrType_x64.TypeInfo).Destination);
        Assert.AreEqual(VBLongLongType.TypeInfo, ConversionPlanner.ForValue(VBLongPtrType_x64.TypeInfo, VBStringType.TypeInfo).Source);
    }

    [TestMethod]
    public void ThePlan_OfASourceDeclaredVariant_IsDeferred()
    {
        var plan = ConversionPlanner.ForDeclared(VBVariantType.TypeInfo, Colour());
        Assert.IsTrue(plan.IsDeferred);
        Assert.IsFalse(plan.IsTypeMismatch, "a deferred plan is not a mismatch: the value decides");
        Assert.AreEqual(VBLongType.TypeInfo, plan.Destination);

        Assert.IsTrue(ConversionPlanner.ForDeclared(VBUnknownType.TypeInfo, VBStringType.TypeInfo).IsDeferred, "a late-bound source is known at run time");
    }

    [TestMethod]
    public void ThePlan_OfOtherDeclaredTypes_IsThePlanOfTheirValues()
    {
        var root = new Uri("file://rdcore-test");
        VBType widget = new VBClassType(new VBClassModuleSymbol(root, root, "Widget"), []);

        Assert.AreSame(ConversionPlanner.ForValue(VBStringType.TypeInfo, VBLongType.TypeInfo), ConversionPlanner.ForDeclared(new VBFixedStringType(4), VBLongType.TypeInfo));
        Assert.AreSame(ConversionPlanner.ForValue(VBLongType.TypeInfo, VBStringType.TypeInfo), ConversionPlanner.ForDeclared(Colour(), VBStringType.TypeInfo));
        Assert.AreEqual(ConversionRule.FromObject, ConversionPlanner.ForDeclared(widget, VBStringType.TypeInfo).Rule);
    }
}
