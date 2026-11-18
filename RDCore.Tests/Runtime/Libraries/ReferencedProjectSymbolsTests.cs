using RDCore.Runtime.LanguageCore.Symbols;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Platform.Components;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.Runtime.Libraries;

/// <summary>
/// A project that references another one of the workspace names its public entities: the modules it can access, and their members it can access
/// (<strong>MS-VBAL §5.2.1.4</strong>, <strong>MS-VBAL §5.2.3</strong>, <strong>MS-VBAL §5.2.4.1.1</strong>).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.2.3 Module Declarations")]
public sealed class ReferencedProjectSymbolsTests
{
    private static readonly Uri Root = new("file:///c:/ws/NavalLib/");
    private static readonly Uri ReferencingRoot = new("file:///c:/ws/NavalConsole/");
    private static readonly SourceRange R = SourceRange.Empty;

    private static VBStandardModuleSymbol Module(string name, bool isPrivate = false)
        => new(Root, Root, name) { Directives = new ModuleDirectives(PrivateModule: isPrivate) };

    private static VBClassModuleSymbol Class(string name, bool exposed)
        => new(Root, Root, name) { Directives = new ModuleDirectives(Exposed: exposed) };

    private static VBModuleFieldVariableMemberSymbol Field(VBModuleSymbol module, AccessModifier access)
        => new(Root, module.Uri, "Score", ScopeKind.Module, VBLongType.TypeInfo, R, R, access);

    private static VBConstantMemberSymbol Constant(VBModuleSymbol module, AccessModifier access)
        => new(Root, module.Uri, "GridSize", ScopeKind.Module, VBLongType.TypeInfo, R, R, access);

    private static VBProcedureMemberSymbol Procedure(VBModuleSymbol module, AccessModifier access)
        => new(Root, module.Uri, "PlaceShip", ScopeKind.Module, SymbolKindExt.Procedure, VBVoidType.TypeInfo, R, R, access);

    private static VBUserDefinedTypeMemberSymbol Record(VBModuleSymbol module, AccessModifier access)
        => new(Root, module.Uri, "GridCoord", ScopeKind.Module, R, R, access);

    private static VBEnumMemberSymbol Enum(VBModuleSymbol module, AccessModifier access)
        => new(Root, module.Uri, "ShipKind", ScopeKind.Module, SymbolKindExt.Enum, VBUnknownType.TypeInfo, R, R, access);

    [TestMethod]
    [DataRow(false, true, DisplayName = "a procedural module")]
    [DataRow(true, false, DisplayName = "a private module")]
    public void AProceduralModule_IsAccessible_UnlessItIsAPrivateModule(bool isPrivate, bool accessible)
    {
        Assert.AreEqual(accessible, ReferencedProjectSymbols.IsAccessible(Module("GameRules", isPrivate)));
    }

    [TestMethod]
    [DataRow(true, true, DisplayName = "an exposed class")]
    [DataRow(false, false, DisplayName = "a private class")]
    public void AClassModule_IsAccessible_WhenItIsExposed(bool exposed, bool accessible)
    {
        Assert.AreEqual(accessible, ReferencedProjectSymbols.IsAccessible(Class("Grid", exposed)));
    }

    // what each kind of member declared each way is to a referencing project, in a procedural module, a private module and a class module.
    [TestMethod]
    [DataRow("field", AccessModifier.Public, true, false, true, DisplayName = "a Public variable")]
    [DataRow("field", AccessModifier.Global, true, false, true, DisplayName = "a Global variable")]
    [DataRow("field", AccessModifier.Implicit, false, false, false, DisplayName = "a variable declared with Dim")]
    [DataRow("field", AccessModifier.Private, false, false, false, DisplayName = "a Private variable")]
    [DataRow("constant", AccessModifier.Public, true, false, false, DisplayName = "a Public constant")]
    [DataRow("constant", AccessModifier.Implicit, false, false, false, DisplayName = "a constant without a modifier")]
    [DataRow("procedure", AccessModifier.Public, true, false, true, DisplayName = "a Public procedure")]
    [DataRow("procedure", AccessModifier.Implicit, true, false, true, DisplayName = "a procedure without a modifier")]
    [DataRow("procedure", AccessModifier.Friend, false, false, false, DisplayName = "a Friend procedure")]
    [DataRow("procedure", AccessModifier.Private, false, false, false, DisplayName = "a Private procedure")]
    [DataRow("type", AccessModifier.Public, true, false, false, DisplayName = "a Public user-defined type")]
    [DataRow("type", AccessModifier.Implicit, true, false, false, DisplayName = "a user-defined type without a modifier")]
    [DataRow("enum", AccessModifier.Public, true, false, true, DisplayName = "a Public Enum")]
    [DataRow("enum", AccessModifier.Implicit, true, false, true, DisplayName = "an Enum without a modifier")]
    [DataRow("enum", AccessModifier.Private, false, false, false, DisplayName = "a Private Enum")]
    public void AMember_IsAccessible_AsItsDeclarationAndItsModuleSay(string kind, AccessModifier access, bool inModule, bool inPrivateModule, bool inClass)
    {
        Symbol MemberOf(VBModuleSymbol module) => kind switch
        {
            "field" => Field(module, access),
            "constant" => Constant(module, access),
            "procedure" => Procedure(module, access),
            "type" => Record(module, access),
            _ => Enum(module, access),
        };

        VBModuleSymbol[] modules = [Module("GameRules"), Module("GameInternals", isPrivate: true), Class("Grid", exposed: true)];
        bool[] expected = [inModule, inPrivateModule, inClass];

        for (var index = 0; index < modules.Length; index++)
        {
            Assert.AreEqual(expected[index], ReferencedProjectSymbols.IsAccessible(MemberOf(modules[index]), modules[index]), modules[index].Name);
        }
    }

    // a public member of a class that is not exposed is still reached through an object of the class, which a referencing project can hold as an Object.
    [TestMethod]
    public void APublicMemberOfAPrivateClass_IsAccessible_ThroughAnObject()
    {
        var grid = Class("Grid", exposed: false);

        Assert.IsTrue(ReferencedProjectSymbols.IsAccessible(Procedure(grid, AccessModifier.Public), grid));
    }

    [TestMethod]
    public void TheNamesOfAProject_AreItsAccessibleModulesAndMembers_EachANameOfTheProjectAtItsPrecedence()
    {
        var rules = Module("GameRules");
        var internals = Module("GameInternals", isPrivate: true);
        var grid = Class("Grid", exposed: true);
        var cell = Class("Cell", exposed: false);
        Symbol[] symbols =
        [
            rules, Procedure(rules, AccessModifier.Public), Field(rules, AccessModifier.Private),
            internals, Procedure(internals, AccessModifier.Public),
            grid, Procedure(grid, AccessModifier.Friend),
            cell,
            new VBPredeclaredInstanceSymbol(grid),
            new VBProjectSymbol(Root, "NavalLib"),
        ];

        var names = ReferencedProjectSymbols.Of(ReferencingRoot, "NavalLib", symbols, priority: 2);

        CollectionAssert.AreEqual(
            new[] { "NavalLib", "GameRules", "Grid", "PlaceShip" },
            names.Select(name => name.Name).ToArray(),
            string.Join(", ", names.Select(name => name.Name)));
        Assert.IsTrue(names.All(name => name.GetProperty(SymbolProperties.Library) == "NavalLib"));
        Assert.IsTrue(names.All(name => name.GetProperty(SymbolProperties.LibraryPriority) == 2));
        Assert.AreEqual(ReferencingRoot, names[0].WorkspaceRoot, "the project is addressed under the referencing project's root, as a library is");
        Assert.AreEqual(rules.Uri, names[1].Uri, "a module is the referenced project's own");
    }

    [TestMethod]
    public void TheMembersOfAnAccessibleEnum_AreNamesOfTheProject_AndAPrivateEnumsAreNot()
    {
        var rules = Module("GameRules");
        var shipKind = Enum(rules, AccessModifier.Public);
        var hidden = new VBEnumMemberSymbol(
            Root, rules.Uri, "CellState", ScopeKind.Module, SymbolKindExt.Enum, VBUnknownType.TypeInfo, R, R, AccessModifier.Private);
        Symbol[] symbols =
        [
            rules, shipKind, hidden,
            new VBEnumConstMemberSymbol(Root, shipKind.Uri, "skCarrier", ScopeKind.Module, SymbolKindExt.EnumMember, R, R),
            new VBEnumConstMemberSymbol(Root, hidden.Uri, "csHit", ScopeKind.Module, SymbolKindExt.EnumMember, R, R),
        ];

        var names = ReferencedProjectSymbols.Of(ReferencingRoot, "NavalLib", symbols, priority: 1).Select(name => name.Name).ToArray();

        CollectionAssert.AreEqual(new[] { "NavalLib", "GameRules", "ShipKind", "skCarrier" }, names, string.Join(", ", names));
    }

    // the referenced project's composition holds the symbols of what it references itself: they are names of that project, and not of the one referencing it.
    [TestMethod]
    public void TheSymbolsOfTheLibrariesTheProjectReferences_AreLeftOut()
    {
        var rules = Module("GameRules");
        var library = new VBStandardModuleSymbol(Root, Root, "Strings").With(SymbolProperties.Library, "VBA");
        Symbol[] symbols =
        [
            rules, library,
            new VBProcedureMemberSymbol(
                Root, library.Uri, "Left", ScopeKind.Module, SymbolKindExt.Function, VBVoidType.TypeInfo, R, R, AccessModifier.Public),
        ];

        var names = ReferencedProjectSymbols.Of(ReferencingRoot, "NavalLib", symbols, priority: 1).Select(name => name.Name).ToArray();

        CollectionAssert.AreEqual(new[] { "NavalLib", "GameRules" }, names, string.Join(", ", names));
    }

    // what the environment host defines of a module of a referenced project: the module and its members as the reader makes them, and what of them the
    // project gives.
    [TestMethod]
    public void AnExposedClassOfAReferencedProject_AndItsPublicMembers_AreNamesOfThatProject()
    {
        var request = new DefineSymbolsParams
        {
            WorkspaceRoot = ReferencingRoot,
            ModuleUri = SymbolUri.Member(Root, "Module1"),
            ModuleName = "Module1",
            Library = "NavalLib",
            LibraryPriority = 1,
            ModuleType = ModuleType.ClassModule,
            Directives = new ModuleDirectives(Exposed: true),
            Symbols =
            [
                new SymbolDescriptor
                {
                    Name = "PlaceShip", Kind = SymbolDescriptorKind.Procedure, Scope = ScopeKind.Module, AccessModifier = AccessModifier.Public,
                },
            ],
        };
        var module = SymbolDescriptorReader.LibraryModuleOf(request)!;
        var members = SymbolDescriptorReader.Read(request, name => IntrinsicVBTypes.TryResolve(name, out var type) ? type : null);

        var names = ReferencedProjectSymbols.Of(ReferencingRoot, "NavalLib", [module, .. members], 1);

        CollectionAssert.AreEqual(new[] { "NavalLib", "Module1", "PlaceShip" }, names.Select(name => name.Name).ToArray());
        Assert.IsTrue(names.All(name => name.GetProperty(SymbolProperties.Library) == "NavalLib"));
    }
}
