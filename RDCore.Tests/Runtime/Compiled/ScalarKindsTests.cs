using RDCore.Runtime.Compiled;
using RDCore.Runtime.Execution.Frames;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;

namespace RDCore.Tests.Runtime.Compiled;

/// <summary>
/// <see cref="ScalarKinds.Of"/>, which every payload is given its kind by when it is made, and the payload a <c>Variant</c> holds of one
/// (<see cref="ScalarPayload.HeldByAVariant"/>): a <c>LongPtr</c> is the kind of the type it is an alias of (MS-VBAL §2.2), and a Variant holds it as
/// that type; a payload of any other type is held as it is.
/// </summary>
[TestClass]
public sealed class ScalarKindsTests
{
    // the types that are no alias of another.
    private static IEnumerable<object[]> OwnTypes =>
    [
        [VBByteType.TypeInfo, ScalarKind.Byte],
        [VBIntegerType.TypeInfo, ScalarKind.Integer],
        [VBLongType.TypeInfo, ScalarKind.Long],
        [VBLongLongType.TypeInfo, ScalarKind.LongLong],
        [VBBooleanType.TypeInfo, ScalarKind.Boolean],
        [VBCurrencyType.TypeInfo, ScalarKind.Currency],
        [VBSingleType.TypeInfo, ScalarKind.Single],
        [VBDoubleType.TypeInfo, ScalarKind.Double],
        [VBDateType.TypeInfo, ScalarKind.Date],
    ];

    private static IEnumerable<object[]> Types =>
    [
        .. OwnTypes,
        [VBLongPtrType_x64.TypeInfo, ScalarKind.LongLong],
        [VBLongPtrType_x86.TypeInfo, ScalarKind.Long],
    ];

    [TestMethod]
    [DynamicData(nameof(Types))]
    public void AScalarType_IsTheKindItsValuesAreOf(VBType type, ScalarKind kind)
    {
        Assert.AreEqual(kind, ScalarKinds.Of(type));
        Assert.AreEqual(kind, new ScalarPayload(type, 1, 0).Kind);
    }

    [TestMethod]
    public void ATypeThatIsNoScalarType_IsNoKind()
    {
        Assert.IsNull(ScalarKinds.Of(VBStringType.TypeInfo));
        Assert.IsNull(ScalarKinds.Of(VBVariantType.TypeInfo));
        Assert.IsNull(ScalarKinds.Of(null));
    }

    [TestMethod]
    public void APayloadOfALongPtr_IsHeldByAVariant_AsTheTypeItIsAnAliasOf()
    {
        var wide = new ScalarPayload(VBLongPtrType_x64.TypeInfo, 7, 0).HeldByAVariant();
        var narrow = new ScalarPayload(VBLongPtrType_x86.TypeInfo, 7, 0).HeldByAVariant();

        Assert.AreSame(VBLongLongType.TypeInfo, wide.Type);
        Assert.AreEqual(ScalarKind.LongLong, wide.Kind);
        Assert.AreEqual(7L, wide.Integral);
        Assert.AreSame(VBLongType.TypeInfo, narrow.Type);
        Assert.AreEqual(ScalarKind.Long, narrow.Kind);
        Assert.AreEqual(7L, narrow.Integral);
    }

    [TestMethod]
    [DynamicData(nameof(OwnTypes))]
    public void APayloadOfAnyOtherScalarType_IsHeldByAVariant_AsItIs(VBType type, ScalarKind kind)
    {
        var payload = new ScalarPayload(type, 1, 0.5);
        var held = payload.HeldByAVariant();

        Assert.AreSame(type, held.Type);
        Assert.AreEqual(kind, held.Kind);
        Assert.AreEqual(payload, held);
    }
}
