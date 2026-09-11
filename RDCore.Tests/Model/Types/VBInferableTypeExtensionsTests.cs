using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;

namespace RDCore.Tests.Model.Types;

[TestClass]
public sealed class VBInferableTypeExtensionsTests
{
    private static VBDeferredModuleType Deferred() => new("Widget", new Uri("file:///c:/ws/#Widget"));

    [TestMethod]
    public void Resolve_Null_WhenThereAreNoCandidates()
        => Assert.IsNull(Deferred().Resolve());

    [TestMethod]
    public void Resolve_TheCandidate_WhenThereIsOnlyOne()
    {
        var inferable = Deferred().WithCandidateType(VBStringType.TypeInfo);

        Assert.AreEqual(VBStringType.TypeInfo, inferable.Resolve());
    }

    [TestMethod]
    [DataRow(typeof(VBByteType))]
    [DataRow(typeof(VBIntegerType))]
    [DataRow(typeof(VBLongType))]
    [DataRow(typeof(VBLongLongType))]
    public void WithCandidateType_WidensAnyIntegralCandidate_ToLong(Type integralType)
    {
        var candidate = (VBType)integralType.GetProperty("TypeInfo")!.GetValue(null)!;

        var inferable = Deferred().WithCandidateType(candidate);

        Assert.AreEqual(VBLongType.TypeInfo, inferable.Resolve());
    }

    [TestMethod]
    public void WithCandidateType_BucketsDifferentlySizedIntegralCandidates_Together()
    {
        // Byte then Integer then Long: all widen to Long, so there is still only one candidate.
        var inferable = Deferred()
            .WithCandidateType(VBByteType.TypeInfo)
            .WithCandidateType(VBIntegerType.TypeInfo)
            .WithCandidateType(VBLongType.TypeInfo);

        Assert.AreEqual(VBLongType.TypeInfo, inferable.Resolve());
    }

    [TestMethod]
    public void WithCandidateType_WidensAFloatingPointCandidate_ToDouble()
    {
        var inferable = Deferred().WithCandidateType(VBSingleType.TypeInfo);

        Assert.AreEqual(VBDoubleType.TypeInfo, inferable.Resolve());
    }

    [TestMethod]
    public void WithCandidateType_WidensAFixedPointCandidate_ToCurrency()
    {
        // Currency is both the fixed-point candidate and its own widened bucket.
        var inferable = Deferred().WithCandidateType(VBCurrencyType.TypeInfo);

        Assert.AreEqual(VBCurrencyType.TypeInfo, inferable.Resolve());
    }

    [TestMethod]
    public void Resolve_Variant_WhenMultiplePlainValueCandidatesConflict()
    {
        var inferable = Deferred()
            .WithCandidateType(VBLongType.TypeInfo)
            .WithCandidateType(VBStringType.TypeInfo);

        Assert.AreEqual(VBVariantType.TypeInfo, inferable.Resolve());
    }

    [TestMethod]
    public void Resolve_Null_WhenAnObjectCandidateConflictsWithAnother()
    {
        // an Object candidate never blends into a Variant guess with another candidate - what it
        // resolves to instead isn't decided yet (the algorithm's own note stops here).
        var inferable = Deferred()
            .WithCandidateType(VBLongType.TypeInfo)
            .WithCandidateType(VBObjectType.TypeInfo);

        Assert.IsNull(inferable.Resolve());
    }

    [TestMethod]
    public void Resolve_Null_WhenAMemberOwnerCandidateConflictsWithAnother()
    {
        var inferable = Deferred()
            .WithCandidateType(VBLongType.TypeInfo)
            .WithCandidateType(new VBStdModuleType("SomeModule", false));

        Assert.IsNull(inferable.Resolve());
    }

    [TestMethod]
    public void Resolve_TheSoleObjectCandidate_WhenItIsTheOnlyOne()
        => Assert.AreEqual(VBObjectType.TypeInfo, Deferred().WithCandidateType(VBObjectType.TypeInfo).Resolve());

    [TestMethod]
    public void WithCandidateType_MergesAnotherInferableTypesCandidates_InsteadOfAddingItOpaquely()
    {
        var other = Deferred().WithCandidateType(VBStringType.TypeInfo);

        var inferable = Deferred().WithCandidateType(VBLongType.TypeInfo).WithCandidateType((VBDeferredModuleType)other);

        Assert.AreEqual(VBVariantType.TypeInfo, inferable.Resolve());
    }
}
