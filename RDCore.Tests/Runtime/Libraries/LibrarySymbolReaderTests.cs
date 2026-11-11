using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Runtime.StdLib;
using System.Collections.Immutable;
using static RDCore.Tests.Runtime.Libraries.LibraryFixtures;

namespace RDCore.Tests.Runtime.Libraries;

/// <summary>
/// The symbols a library declares, read off its description: a project of its own, named for the library, whose classes and enumerations are its own and no
/// one else's.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §2.3.1.2 References")]
public sealed class LibrarySymbolReaderTests
{
    private static readonly Uri Root = new("file:///c:/ws/");

    private static ImmutableArray<Symbol> Read(params LibraryDescription[] libraries) => new LibrarySymbolReader(Root).Read(libraries);

    private static VBClassModuleSymbol Class(ImmutableArray<Symbol> symbols, string name) => symbols.OfType<VBClassModuleSymbol>().Single(symbol => symbol.Name == name);

    private static VBTypeMemberSymbol Member(VBClassModuleSymbol owner, string name, Type? kind = null)
        => owner.Members.Single(member => member.Name == name && (kind is null || member.GetType() == kind));

    [TestMethod]
    public void ALibrary_IsAProjectOfItsOwn_AndEverythingItDeclaresSaysWhichLibraryItIsOf()
    {
        var symbols = Read(Widgets);

        var project = symbols.OfType<VBProjectSymbol>().Single();
        Assert.AreEqual("Widgets", project.Name);
        Assert.AreEqual("Widgets", project.GetProperty(SymbolProperties.Library));
        Assert.IsTrue(symbols.All(symbol => symbol.GetProperty(SymbolProperties.Library) == "Widgets"), "the scope tree keeps every project's modules at one tier");
    }

    [TestMethod]
    public void AClassIsAClassModule_ThatIsCreatableWhenTheLibrarySaysSo_AndHasTheNameItIsCreatedBy()
    {
        var symbols = Read(Widgets);

        var gadget = Class(symbols, "Gadget");
        Assert.IsTrue(gadget.GetProperty(SymbolProperties.Creatable));
        Assert.AreEqual("Widgets.Gadget", gadget.GetProperty(SymbolProperties.ProgId));
        Assert.IsTrue(gadget.GetProperty(SymbolProperties.Extensible), "a host adds members to its objects that the library does not declare");
        Assert.IsFalse(Class(symbols, "Part").GetProperty(SymbolProperties.Creatable));
    }

    [TestMethod]
    public void TheMembersOfAClass_AreOfTheKindsTheyAre_AndEachCarriesWhatItIsDispatchedBy()
    {
        var gadget = Class(Read(Widgets), "Gadget");

        Assert.IsInstanceOfType<VBPropertyGetMemberSymbol>(Member(gadget, "Mode", typeof(VBPropertyGetMemberSymbol)));
        Assert.IsInstanceOfType<VBPropertyLetMemberSymbol>(Member(gadget, "Mode", typeof(VBPropertyLetMemberSymbol)));
        Assert.IsInstanceOfType<VBFunctionMemberSymbol>(Member(gadget, "Find"));
        Assert.IsInstanceOfType<VBProcedureMemberSymbol>(Member(gadget, "Reset"));

        Assert.AreEqual("Widgets.Gadget.Find/method", Member(gadget, "Find").GetProperty(SymbolProperties.ExternalTarget));
        Assert.AreEqual("Widgets.Gadget.Mode/get", Member(gadget, "Mode", typeof(VBPropertyGetMemberSymbol)).GetProperty(SymbolProperties.ExternalTarget));
        Assert.AreEqual("Widgets.Gadget.Mode/let", Member(gadget, "Mode", typeof(VBPropertyLetMemberSymbol)).GetProperty(SymbolProperties.ExternalTarget),
            "the accessors of a property share a name, and the key tells them apart");
    }

    [TestMethod]
    public void TheDefaultMemberOfAClass_IsFoundByTheIdItCarries_AndAHiddenMemberIsHidden()
    {
        var gadget = Class(Read(Widgets), "Gadget");

        Assert.IsTrue(Member(gadget, "Parts").TryGetProperty(SymbolProperties.UserMemId, out var id));
        Assert.AreEqual(0, id);
        Assert.IsFalse(Member(gadget, "Find").TryGetProperty(SymbolProperties.UserMemId, out _), "a member with an ordinary id is not the default member");
        Assert.AreNotEqual(0, Member(gadget, "Secret").GetProperty(SymbolProperties.MemberFlags) & SymbolProperties.HiddenMemberFlag);
        Assert.AreEqual(0, Member(gadget, "Find").GetProperty(SymbolProperties.MemberFlags) & SymbolProperties.HiddenMemberFlag);
    }

    [TestMethod]
    public void AnInstanceMember_HasTheObjectItIsCalledOnAsItsFirstParameter_AsEveryMemberOfAClassDoes()
    {
        var find = (VBFunctionMemberSymbol)Member(Class(Read(Widgets), "Gadget"), "Find");

        CollectionAssert.AreEqual(new[] { "Me", "Name", "Limit", "Exact" }, find.Parameters.Select(parameter => parameter.Name).ToArray());
        Assert.AreEqual(ParameterKind.ExplicitByVal, find.Parameters[1].ParameterKind);
        Assert.AreEqual(ParameterKind.ExplicitByRef, find.Parameters[2].ParameterKind, "a parameter is by reference unless the library says otherwise");
    }

    [TestMethod]
    public void AnOptionalParameter_HasItsDefault_AsALiteral()
    {
        var find = (VBFunctionMemberSymbol)Member(Class(Read(Widgets), "Gadget"), "Find");

        // Me, Name, Limit, Exact
        Assert.IsFalse(find.Parameters[1].IsOptional);
        Assert.IsNull(find.Parameters[1].DefaultValue);
        Assert.IsTrue(find.Parameters[2].IsOptional);
        Assert.AreEqual(10, ((VBLongValue)Assert.IsInstanceOfType<LiteralExpressionNode>(find.Parameters[2].DefaultValue).StaticValue).Value);
        Assert.AreEqual(VBBooleanValue.True, Assert.IsInstanceOfType<LiteralExpressionNode>(find.Parameters[3].DefaultValue).StaticValue);
    }

    [TestMethod]
    public void AParamArray_IsOne_AndAnArrayOfAType_IsAnArrayOfIt()
    {
        var gadget = Class(Read(Widgets), "Gadget");

        var reset = (VBProcedureMemberSymbol)Member(gadget, "Reset");
        Assert.IsInstanceOfType<ParamArrayParameterSymbol>(reset.Parameters[1]);

        var names = (VBFunctionMemberSymbol)Member(gadget, "Names");
        Assert.IsInstanceOfType<VBResizableArrayType>(names.ResolvedType);
        Assert.AreEqual(VBStringType.TypeInfo, ((VBResizableArrayType)names.ResolvedType).ItemType);
    }

    [TestMethod]
    public void AnEnumeration_IsAType_AndEachMemberOfItIsAConstantOfTheProject()
    {
        var symbols = Read(Widgets);

        var mode = symbols.OfType<VBEnumMemberSymbol>().Single();
        Assert.AreEqual("WidgetMode", mode.Name);
        var type = (VBEnumType)mode.ResolvedType;
        CollectionAssert.AreEqual(new[] { "wgOff", "wgOn" }, type.Members.Select(member => member.Name).ToArray());
        Assert.AreEqual(2, symbols.OfType<VBEnumConstMemberSymbol>().Count());

        var getter = (VBPropertyGetMemberSymbol)Member(Class(symbols, "Gadget"), "Mode", typeof(VBPropertyGetMemberSymbol));
        Assert.IsInstanceOfType<VBEnumType>(getter.ResolvedType, "a member declared as the enumeration is of it");
    }

    [TestMethod]
    public void AnEventOfAClass_IsAMemberOfIt_WithTheParametersAHandlerReceives()
    {
        var changed = (VBEventMemberSymbol)Member(Class(Read(Widgets), "Gadget"), "Changed");

        CollectionAssert.AreEqual(new[] { "Part" }, changed.Parameters.Select(parameter => parameter.Name).ToArray(), "an event has no object it is called on");
    }

    [TestMethod]
    public void TwoClassesThatNameEachOther_AreBothRead_TheSecondNamingTheFirstByItsName()
    {
        var symbols = Read(Widgets);

        var parts = (VBPropertyGetMemberSymbol)Member(Class(symbols, "Gadget"), "Parts");
        Assert.IsInstanceOfType<VBClassType>(parts.ResolvedType, "the class that is read second finds the first one's type");

        var owner = (VBPropertyGetMemberSymbol)Member(Class(symbols, "Part"), "Owner");
        var unresolved = Assert.IsInstanceOfType<VBUnresolvedType>(owner.ResolvedType, "and the first, which was being read, is named by its name until it is used");
        Assert.AreEqual("Widgets.Gadget", unresolved.DeclaredName);
    }

    [TestMethod]
    public void ATypeOfALibraryItDependsOn_IsThatLibrarysType_AndATypeOfNoLibraryOfTheseIsNotKnown()
    {
        var symbols = Read(Widgets, Panels);

        var panel = Class(symbols, "Panel");
        var gadget = (VBPropertyGetMemberSymbol)Member(panel, "Gadget");
        Assert.IsInstanceOfType<VBClassType>(gadget.ResolvedType);
        Assert.AreEqual("Gadget", gadget.ResolvedType.Name);

        var beyond = (VBPropertyGetMemberSymbol)Member(panel, "Beyond");
        Assert.AreEqual("Elsewhere.Thing", Assert.IsInstanceOfType<VBUnresolvedType>(beyond.ResolvedType).DeclaredName);
    }

    [TestMethod]
    public void AClassWithNothingToItButAName_IsAClass()
    {
        var symbols = Read(new LibraryDescription { Name = "Bare", Classes = [new ClassDescription { Name = "Lone" }] });

        Assert.IsEmpty(Class(symbols, "Lone").Members);
    }

    [TestMethod]
    public void ThePlatformsStandardLibrary_IsNotRead_ItIsTheirs()
    {
        var libraries = ReferencedLibraries.Load(new InMemoryLibrarySource(), ["VBA"], [StandardLibrary.LibraryName]);

        Assert.IsEmpty(new LibrarySymbolProvider(Root, libraries).ProvideSymbols().ToArray());
    }

    // an application object, Global, of a library known by the GUID it is given.
    private static LibraryDescription WithGlobal(string name, string guid) => new()
    {
        Name = name,
        Origin = new LibraryOrigin { Guid = guid, Version = "6.0", ExportedBy = "tests" },
        Classes =
        [
            new ClassDescription
            {
                Name = "Global",
                IsApplication = true,
                IsCreatable = true,
                DefaultInterface = "VBGlobal",
                Members =
                [
                    new MemberDescription { Name = "Forms", Kind = MemberKind.PropertyGet, Type = "Object" },
                    new MemberDescription { Name = "Unload", Kind = MemberKind.Method, Parameters = [new ParameterDescription { Name = "Object", Type = "Object" }] },
                ],
            },
        ],
    };

    [TestMethod]
    public void TheVisualBasicLibrarysGlobal_IsAModuleOfTheLibrary_WhoseMembersAreNamesOfTheProject()
    {
        var symbols = Read(WithGlobal("VB", "{FCFB3D2E-A0FA-1068-A738-08002B3371B5}"));

        var module = symbols.OfType<VBStandardModuleSymbol>().Single(symbol => symbol.Name == "Global");
        Assert.AreEqual(symbols.OfType<VBProjectSymbol>().Single().Uri, module.ParentUri, "the library project's module, as its class would be");
        Assert.IsFalse(symbols.OfType<VBClassModuleSymbol>().Any(), "no type: neither Global nor its interface");

        var members = symbols.OfType<VBTypeMemberSymbol>().Where(member => member.ParentUri == module.Uri).ToArray();
        CollectionAssert.AreEquivalent(new[] { "Forms", "Unload" }, members.Select(member => member.Name).ToArray());
        Assert.IsTrue(members.All(member => member.ScopeKind == ScopeKind.Module && member.GetProperty(SymbolProperties.LibraryPriority) == 1));
        Assert.AreEqual("VB.Global.Forms/get", members.Single(member => member.Name == "Forms").GetProperty(SymbolProperties.ExternalTarget));
        var unload = (VBProcedureMemberSymbol)members.Single(member => member.Name == "Unload");
        CollectionAssert.AreEqual(new[] { "Object" }, unload.Parameters.Select(parameter => parameter.Name).ToArray(), "a procedure of a module has no Me");
    }

    [TestMethod]
    public void AnotherLibrarysApplicationObject_IsAClass_WhatsoeverItsName()
    {
        var symbols = Read(WithGlobal("VB", "{00000000-0000-0000-0000-000000000002}"));

        Assert.IsTrue(Class(symbols, "Global").Members.All(member => member.ScopeKind == ScopeKind.Instance));
        Assert.IsFalse(symbols.OfType<VBStandardModuleSymbol>().Any(symbol => symbol.Name == "Global"));
    }
}
