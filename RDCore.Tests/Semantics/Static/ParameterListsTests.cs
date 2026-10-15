using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Semantics.Static;

namespace RDCore.Tests.Semantics.Static;

/// <summary>
/// Whether two declared types are the same one where an event handler's parameters must be compatible with its event's (<strong>MS-VBAL §5.3.1.8</strong>)
/// and an implemented name declaration's must be its interface member's (<strong>§5.3.1.9</strong>): an Enum named in an <c>As</c> clause means the declared
/// type <c>Long</c> (<strong>§5.2.3.4</strong>).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.3.1.9 Implemented Name Declarations")]
public sealed class ParameterListsTests
{
    private static readonly Uri Root = TestUri.WorkspaceRoot();

    private static VBEnumType Enum(string name)
        => new(new VBEnumMemberSymbol(Root, TestUri.TestModuleUri(), name, ScopeKind.Module, SymbolKindExt.Enum,
            VBUnknownType.TypeInfo, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public), members: null);

    [TestMethod]
    public void AnEnumAndLong_AreTheSameType()
    {
        Assert.IsTrue(ParameterLists.SameType(Enum("Colour"), VBLongType.TypeInfo));
        Assert.IsTrue(ParameterLists.SameType(VBLongType.TypeInfo, Enum("Colour")));
    }

    [TestMethod]
    public void TwoEnums_AreTheSameType()
        => Assert.IsTrue(ParameterLists.SameType(Enum("Colour"), Enum("Kind")));

    [TestMethod]
    public void AnEnumAndInteger_AreNot()
        => Assert.IsFalse(ParameterLists.SameType(Enum("Colour"), VBIntegerType.TypeInfo));

    [TestMethod]
    public void IntegerAndLong_AreNot()
        => Assert.IsFalse(ParameterLists.SameType(VBIntegerType.TypeInfo, VBLongType.TypeInfo));
}
