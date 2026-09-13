using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Tests.Model.Values;

[TestClass]
public sealed class VBUserDefinedTypeValueTests
{
    private static VBUserDefinedType Udt(string name, params VBTypeMemberSymbol[] fields)
    {
        var uri = TestUri.TestModuleUserDefinedTypeUri(name);
        var symbol = new VBUserDefinedTypeMemberSymbol(uri, uri, name, ScopeKind.Module, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);
        return new VBUserDefinedType(symbol, [.. fields]);
    }

    private static VBUserDefinedTypeFieldSymbol Field(string name, VBType type)
    {
        var uri = TestUri.TestUserDefinedTypeMemberUri(name);
        return new VBUserDefinedTypeFieldSymbol(uri, uri, name, type, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);
    }

    [TestMethod]
    public void Size_SumsTheDeclaredSizeOfEachField()
    {
        var udt = Udt("Point", Field("X", VBLongType.TypeInfo), Field("Y", VBLongType.TypeInfo));

        var size = new VBUserDefinedTypeValue(udt).Size;

        Assert.AreEqual(VBLongType.TypeInfo.DefaultValue.Size * 2, size);
    }

    [TestMethod]
    public void Size_MixedFieldTypes_SumsEachFieldsOwnSize()
    {
        var udt = Udt("Mixed", Field("B", VBByteType.TypeInfo), Field("L", VBLongType.TypeInfo));

        var size = new VBUserDefinedTypeValue(udt).Size;

        Assert.AreEqual(VBByteType.TypeInfo.DefaultValue.Size + VBLongType.TypeInfo.DefaultValue.Size, size);
    }

    [TestMethod]
    public void Size_OfAnEmptyType_IsZero()
    {
        var udt = Udt("Empty");

        Assert.AreEqual(0, new VBUserDefinedTypeValue(udt).Size);
    }
}
