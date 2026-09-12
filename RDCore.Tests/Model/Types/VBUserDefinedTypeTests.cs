using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;

namespace RDCore.Tests.Model.Types;

/// <summary>
/// Regression coverage for a stack-overflow bug in <c>VBUserDefinedType</c>'s record equality and
/// <c>ToString</c>: the compiler-generated implementations recurse forever between
/// <see cref="RDCore.SDK.Model.Types.Abstract.VBType.DefaultValue"/> and
/// <see cref="RDCore.SDK.Model.Values.Abstract.VBTypedValue.TypeInfo"/>, which reference each other
/// (any <c>VBType</c>'s default value carries that same <c>VBType</c> back as its own <c>TypeInfo</c>).
/// The fix overrides <c>PrintMembers</c> on both base types to stop printing the cyclic side, and fixes
/// a related bug uncovered along the way: <see cref="VBUserDefinedType"/>'s identity comparison used
/// <see cref="Uri"/>'s own <c>Equals</c>, which ignores <see cref="Uri.Fragment"/> — the very part of
/// these symbol URIs that encodes which module/member the symbol actually is.
/// </summary>
[TestClass]
public sealed class VBUserDefinedTypeTests
{
    private static VBUserDefinedType Udt(string name, string moduleUri = "file://rdcore-test")
    {
        var uri = new Uri($"{moduleUri}#{name}");
        var symbol = new VBUserDefinedTypeMemberSymbol(uri, uri, name, ScopeKind.Module, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);
        return new VBUserDefinedType(symbol, []);
    }

    [TestMethod]
    public void DistinctUdtsSharingAWorkspaceRoot_AreNotEqual()
    {
        var foo = Udt("Foo");
        var bar = Udt("Bar");

        Assert.IsFalse(foo == bar);
        Assert.IsFalse(foo.Equals(bar));
        Assert.AreNotEqual(foo.GetHashCode(), bar.GetHashCode());
    }

    [TestMethod]
    public void UdtsAtTheSameSymbolLocation_AreEqual()
    {
        var first = Udt("Foo");
        var second = Udt("Foo");

        Assert.IsTrue(first == second);
        Assert.IsTrue(first.Equals(second));
        Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
    }

    [TestMethod]
    public void ToString_DoesNotStackOverflow()
    {
        var udt = Udt("Foo");

        var text = udt.ToString();

        Assert.IsNotNull(text);
        StringAssert.Contains(text, "Foo");
    }

    [TestMethod]
    public void Equals_DoesNotStackOverflow_WhenComparingTwoDistinctInstances()
    {
        var foo = Udt("Foo");
        var bar = Udt("Bar");

        // the assertion itself is secondary here: merely reaching it without an
        // InsufficientExecutionStackException is the regression this guards against.
        _ = foo.Equals(bar);
    }

    [TestMethod]
    public void ToString_OnAnyVBType_DoesNotStackOverflow()
        // VBUserDefinedType is not special-cased: the cycle is between VBType.DefaultValue and
        // VBTypedValue.TypeInfo, shared by every VBType/VBTypedValue pair in the platform.
        => Assert.IsNotNull(VBLongType.TypeInfo.ToString());
}
