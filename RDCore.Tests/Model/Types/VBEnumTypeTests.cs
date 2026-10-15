using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.Tests.Model.Types;

/// <summary>
/// <see cref="VBEnumType"/> as a declared type: MS-VBAL §5.2.3.4 makes an enum name in an as-type clause mean the declared
/// type <c>Long</c>, so what a variable of an Enum type holds reads back as a <c>Long</c>.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §2.4 Static Types")]
public sealed class VBEnumTypeTests
{
    private static readonly Uri Root = TestUri.WorkspaceRoot();

    private static VBEnumType Colour()
    {
        var symbol = new VBEnumMemberSymbol(Root, TestUri.TestModuleUri(), "Colour", ScopeKind.Module, SymbolKindExt.Enum,
            VBUnknownType.TypeInfo, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);
        return new VBEnumType(symbol, [new VBEnumConstMemberSymbol(
            Root, symbol.Uri, "Red", ScopeKind.Module, SymbolKindExt.EnumMember, SourceRange.Empty, SourceRange.Empty)]);
    }

    [TestMethod]
    public void AsDeclaredType_AnEnum_IsLong()
        => Assert.AreEqual(VBLongType.TypeInfo, VBEnumType.AsDeclaredType(Colour()));

    [TestMethod]
    public void AsDeclaredType_AnyOtherType_IsItself()
    {
        Assert.AreEqual(VBLongType.TypeInfo, VBEnumType.AsDeclaredType(VBLongType.TypeInfo));
        Assert.AreEqual(VBIntegerType.TypeInfo, VBEnumType.AsDeclaredType(VBIntegerType.TypeInfo));
        Assert.AreEqual(VBVariantType.TypeInfo, VBEnumType.AsDeclaredType(VBVariantType.TypeInfo));
    }

    [TestMethod]
    public void CreateValue_BindingHandle_IsALongBoundToIt()
    {
        var type = Colour();
        var handle = new ValueBindingHandle(new VBRuntimeValue<int>(1));

        var value = type.CreateValue(handle);

        Assert.IsInstanceOfType<VBLongValue>(value);
        Assert.AreSame(handle, value.Handle);
    }
}
