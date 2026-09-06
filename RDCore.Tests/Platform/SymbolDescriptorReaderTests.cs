using RDCore.SDK.Model;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.Platform;

[TestClass]
public sealed class SymbolDescriptorReaderTests
{
    private static readonly Uri WorkspaceRoot = new("file:///c:/ws/");

    private static VBType? Intrinsic(string name) => IntrinsicVBTypes.TryResolve(name, out var type) ? type : null;

    private static Symbol[] Read(params SymbolDescriptor[] symbols) => [.. SymbolDescriptorReader.Read(
        new DefineSymbolsParams { WorkspaceRoot = WorkspaceRoot, ModuleName = "Mod1", Symbols = [.. symbols] },
        Intrinsic)];

    [TestMethod]
    public void Function_BindsIntrinsicReturnType_AndParameters()
    {
        var symbols = Read(new SymbolDescriptor
        {
            Name = "Add",
            Kind = SymbolDescriptorKind.Function,
            AccessModifier = AccessModifier.Public,
            DeclaredTypeName = "Long",
            Parameters =
            [
                new ParameterDescriptor { Name = "a", DeclaredTypeName = "Long" },
                new ParameterDescriptor { Name = "b", DeclaredTypeName = "Long" },
            ],
        });

        var function = (VBFunctionMemberSymbol)symbols.Single();
        Assert.AreEqual(VBTypeNames.VBLong, function.ResolvedType.Name);
        Assert.AreEqual(2, function.Parameters.Length);
        Assert.AreEqual(VBTypeNames.VBLong, function.Parameters[0].ResolvedType.Name);
        Assert.AreEqual(function.Uri, function.Parameters[0].ParentUri);
    }

    [TestMethod]
    public void UnresolvedDeclaredType_StaysUnknown()
    {
        var field = (VBModuleFieldVariableMemberSymbol)Read(new SymbolDescriptor
        {
            Name = "Widget",
            Kind = SymbolDescriptorKind.ModuleField,
            DeclaredTypeName = "CWidget",
        }).Single();

        Assert.IsInstanceOfType<VBUnknownType>(field.ResolvedType);
    }

    [TestMethod]
    public void Enum_YieldsEnumSymbolThenMembersParentedToIt()
    {
        var symbols = Read(new SymbolDescriptor
        {
            Name = "Direction",
            Kind = SymbolDescriptorKind.Enum,
            Members =
            [
                new SymbolDescriptor { Name = "North", Kind = SymbolDescriptorKind.EnumMember },
                new SymbolDescriptor { Name = "South", Kind = SymbolDescriptorKind.EnumMember },
            ],
        });

        Assert.AreEqual(3, symbols.Length);
        var enumSymbol = (VBEnumMemberSymbol)symbols[0];
        var members = symbols.OfType<VBEnumConstMemberSymbol>().ToArray();
        Assert.AreEqual(2, members.Length);
        Assert.IsTrue(members.All(m => m.ParentUri == enumSymbol.Uri));
    }

    [TestMethod]
    public void UserDefinedType_YieldsTypeThenFieldsParentedToIt()
    {
        var symbols = Read(new SymbolDescriptor
        {
            Name = "TPoint",
            Kind = SymbolDescriptorKind.UserDefinedType,
            Members =
            [
                new SymbolDescriptor { Name = "X", Kind = SymbolDescriptorKind.UserDefinedTypeField, DeclaredTypeName = "Long" },
                new SymbolDescriptor { Name = "Y", Kind = SymbolDescriptorKind.UserDefinedTypeField, DeclaredTypeName = "Long" },
            ],
        });

        Assert.AreEqual(3, symbols.Length);
        var udt = (VBUserDefinedTypeMemberSymbol)symbols[0];
        var fields = symbols.OfType<VBUserDefinedTypeFieldSymbol>().ToArray();
        Assert.AreEqual(2, fields.Length);
        Assert.IsTrue(fields.All(f => f.ParentUri == udt.Uri));
        Assert.IsTrue(fields.All(f => f.ResolvedType.Name == VBTypeNames.VBLong));
    }

    [TestMethod]
    public void Event_ReconstructsWithParametersParentedToTheEvent()
    {
        var evt = (VBEventMemberSymbol)Read(new SymbolDescriptor
        {
            Name = "Changed",
            Kind = SymbolDescriptorKind.Event,
            Parameters = [new ParameterDescriptor { Name = "NewValue", DeclaredTypeName = "Long" }],
        }).Single();

        Assert.AreEqual(1, evt.Parameters.Length);
        Assert.AreEqual("NewValue", evt.Parameters[0].Name);
        Assert.AreEqual(evt.Uri, evt.Parameters[0].ParentUri);
        Assert.AreEqual(VBTypeNames.VBLong, evt.Parameters[0].ResolvedType.Name);
    }

    [TestMethod]
    public void ExternalFunction_CarriesDeclareMetadata()
    {
        var external = (VBExternalFunctionMemberSymbol)Read(new SymbolDescriptor
        {
            Name = "GetTickCount",
            Kind = SymbolDescriptorKind.ExternalFunction,
            Scope = ScopeKind.External,
            DeclaredTypeName = "Long",
            External = new ExternalDescriptor { IsPtrSafe = true, Library = "kernel32", Alias = "GetTickCount64" },
        }).Single();

        Assert.IsTrue(external.IsPtrSafe);
        Assert.AreEqual("kernel32", external.Lib);
        Assert.AreEqual("GetTickCount64", external.Alias);
        Assert.AreEqual(VBTypeNames.VBLong, external.ResolvedType.Name);
    }

    [TestMethod]
    public void MissingWorkspaceRoot_Throws()
        => Assert.ThrowsExactly<ArgumentException>(
            () => SymbolDescriptorReader.Read(new DefineSymbolsParams { ModuleName = "X" }, Intrinsic).ToArray());
}
