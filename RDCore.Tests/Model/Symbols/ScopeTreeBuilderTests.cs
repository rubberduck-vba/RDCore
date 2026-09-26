using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Tests.Model.Symbols;

[TestClass]
public sealed class ScopeTreeBuilderTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SourceRange R = SourceRange.Empty;

    private static VBStandardModuleSymbol Module(string name) => new(Root, Root, name);
    private static VBClassModuleSymbol ClassModule(string name) => new(Root, Root, name);

    private static VBModuleFieldVariableMemberSymbol Field(Uri moduleUri, string name, AccessModifier access = AccessModifier.Implicit)
        => new(Root, moduleUri, name, ScopeKind.Module, VBLongType.TypeInfo, R, R, access);

    private static VBProcedureMemberSymbol Procedure(Uri moduleUri, string name, AccessModifier access = AccessModifier.Implicit)
        => new(Root, moduleUri, name, ScopeKind.Module, SymbolKindExt.Procedure, VBVoidType.TypeInfo, R, R, access);

    private static VBParameterSymbol Parameter(Uri procedureUri, string name)
        => new(Root, procedureUri, name, R, R, ParameterKind.ImplicitByRef, VBLongType.TypeInfo);

    private static VBLocalVariableSymbol Local(Uri procedureUri, string name, LocalDeclarationKind declaredBy = LocalDeclarationKind.Dim)
        => new(Root, procedureUri, name, ScopeKind.Local, R, R, DeclaredBy: declaredBy);

    private static PrecompilerConstantSymbol PrecompilerConst(string name)
        => new(name, VBEmptyValue.Empty);

    [TestMethod]
    public void ModuleSymbolsAndPrecompilerConstants_LandInTheGlobalScope()
    {
        var tree = ScopeTreeBuilder.Build([Module("Mod1"), PrecompilerConst("RDDEBUG")]);

        Assert.ContainsSingle(tree.Global.DeclaredAs("Mod1"));
        Assert.ContainsSingle(tree.Global.DeclaredAs("RDDEBUG"));
        Assert.IsNull(tree.Global.Parent);
        Assert.AreEqual(LexicalScopeKind.Global, tree.Global.Kind);
    }

    [TestMethod]
    public void ModuleMembers_LandInTheirModuleScope_NotTheGlobalScope()
    {
        var module = Module("Mod1");
        var field = Field(module.Uri, "Total");

        var tree = ScopeTreeBuilder.Build([module, field]);
        var moduleScope = tree.ScopeFor(module.Uri);

        Assert.AreSame(field, moduleScope.DeclaredAs("Total").Single());
        Assert.IsEmpty(tree.Global.DeclaredAs("Total"));
        Assert.AreEqual(LexicalScopeKind.Module, moduleScope.Kind);
    }

    [TestMethod]
    public void ParametersAndLocals_LandInTheirProcedureScope()
    {
        var module = Module("Mod1");
        var declared = Procedure(module.Uri, "DoWork");
        var parameter = Parameter(declared.Uri, "value");
        var procedure = declared with { Parameters = [parameter] };
        var local = Local(procedure.Uri, "temp");

        var tree = ScopeTreeBuilder.Build([module, procedure, local]);
        var procedureScope = tree.ScopeFor(procedure.Uri);

        Assert.AreSame(parameter, procedureScope.DeclaredAs("value").Single());
        Assert.AreSame(local, procedureScope.DeclaredAs("temp").Single());
        Assert.AreEqual(LexicalScopeKind.Procedure, procedureScope.Kind);
    }

    [TestMethod]
    public void LocalsOnTheProcedureSymbol_AlsoLandInItsProcedureScope()
        // A procedure's own Locals property (Dim/Static/Const, mirroring how Parameters already "rides
        // on" the member symbol rather than needing a separate flat entry in the input set) resolves
        // the same way the test above proves for a flat-entry local.
    {
        var module = Module("Mod1");
        var declared = Procedure(module.Uri, "DoWork");
        var local = Local(declared.Uri, "temp");
        var procedure = declared with { Locals = [local] };

        var tree = ScopeTreeBuilder.Build([module, procedure]);
        var procedureScope = tree.ScopeFor(procedure.Uri);

        Assert.AreSame(local, procedureScope.DeclaredAs("temp").Single());
    }

    [TestMethod]
    public void ScopeFor_MapsAContainedSymbol_ToItsDeclaringScope()
    {
        var module = Module("Mod1");
        var procedure = Procedure(module.Uri, "DoWork");
        var local = Local(procedure.Uri, "temp");
        var field = Field(module.Uri, "Total");

        var tree = ScopeTreeBuilder.Build([module, procedure, local, field]);

        Assert.AreSame(tree.ScopeFor(procedure.Uri), tree.ScopeFor(local.Uri), "a local resolves from its procedure scope");
        Assert.AreSame(tree.ScopeFor(module.Uri), tree.ScopeFor(field.Uri), "a field resolves from its module scope");
    }

    [TestMethod]
    public void SelfAndAncestors_WalksProcedure_Module_Project_Global()
    {
        var module = Module("Mod1");
        var procedure = Procedure(module.Uri, "DoWork");

        var tree = ScopeTreeBuilder.Build([module, procedure]);
        var chain = tree.ScopeFor(procedure.Uri).SelfAndAncestors().ToArray();

        CollectionAssert.AreEqual(
            new[] { LexicalScopeKind.Procedure, LexicalScopeKind.Module, LexicalScopeKind.Project, LexicalScopeKind.Global },
            chain.Select(scope => scope.Kind).ToArray());
        Assert.AreSame(tree.Global, chain[^1]);
    }

    [TestMethod]
    public void DeclaredAs_MatchesNamesCaseInsensitively()
    {
        var module = Module("Mod1");
        var field = Field(module.Uri, "Total");

        var tree = ScopeTreeBuilder.Build([module, field]);

        Assert.AreSame(field, tree.ScopeFor(module.Uri).DeclaredAs("tOtAl").Single());
    }

    // this test used to assert that an enum's members are placed in no scope at all, on the grounds that
    // they are reached by member access. MS-VBAL §5.2.3.4 says otherwise: "the Enum type *and its Enum
    // members* are accessible within the enclosing project" (public) or "within the enclosing module"
    // (private) — which is why `vbSunday` is a name on its own in every VBA project. They parent to the
    // enum rather than to a scope, so the placement is resolved through it.
    [TestMethod]
    public void EnumMembers_ArePlacedInTheScopeThatDeclaresTheEnum()
    {
        var module = Module("Mod1");
        var (@enum, red) = Enum(module.Uri, "Colours", "Red");

        var tree = ScopeTreeBuilder.Build([module, @enum, red]);

        Assert.AreSame(@enum, tree.ScopeFor(module.Uri).DeclaredAs("Colours").Single(), "the Enum itself is a module member");
        Assert.AreSame(red, tree.ScopeFor(module.Uri).DeclaredAs("Red").Single());
    }

    [TestMethod]
    public void APublicEnumsMembers_ReachTheProjectScope()
    {
        var module = Module("Mod1");
        var (@enum, red) = Enum(module.Uri, "Colours", "Red", AccessModifier.Public);

        var tree = ScopeTreeBuilder.Build([module, @enum, red]);

        Assert.AreSame(red, tree.ScopeFor(module.Uri).Parent!.DeclaredAs("Red").Single());
    }

    [TestMethod]
    public void APrivateEnumsMembers_DoNotReachTheProjectScope()
    {
        var module = Module("Mod1");
        var (@enum, red) = Enum(module.Uri, "Colours", "Red", AccessModifier.Private);

        var tree = ScopeTreeBuilder.Build([module, @enum, red]);

        Assert.AreSame(red, tree.ScopeFor(module.Uri).DeclaredAs("Red").Single(), "accessible within the enclosing module");
        Assert.IsEmpty(tree.ScopeFor(module.Uri).Parent!.DeclaredAs("Red"));
    }

    [TestMethod]
    public void APublicEnumOfAClassModule_ReachesTheProjectScope_TypeAndMembersAlike()
    {
        // the class's other members need an instance to be reached through; a declared type needs none,
        // and §5.2.3.4 scopes a public Enum to the project wherever it is declared.
        var classModule = ClassModule("Class1");
        var (@enum, red) = Enum(classModule.Uri, "Colours", "Red", AccessModifier.Public);
        var field = Field(classModule.Uri, "Total", AccessModifier.Public);

        var project = ScopeTreeBuilder.Build([classModule, @enum, red, field]).ScopeFor(classModule.Uri).Parent!;

        Assert.AreSame(@enum, project.DeclaredAs("Colours").Single());
        Assert.AreSame(red, project.DeclaredAs("Red").Single());
        Assert.IsEmpty(project.DeclaredAs("Total"), "an instance field is reached through an instance");
    }

    [TestMethod]
    public void APublicUserDefinedTypeOfAClassModule_ReachesTheProjectScope()
    {
        // MS-VBAL §5.2.3.3, the same rule: "the UDT is accessible within the enclosing project".
        var classModule = ClassModule("Class1");
        var udt = new VBUserDefinedTypeMemberSymbol(Root, classModule.Uri, "Point", ScopeKind.Module, R, R, AccessModifier.Public);

        var project = ScopeTreeBuilder.Build([classModule, udt]).ScopeFor(classModule.Uri).Parent!;

        Assert.AreSame(udt, project.DeclaredAs("Point").Single());
    }

    [TestMethod]
    public void AnEnumConstantWhoseEnumIsAbsent_IsNotPlaced()
    {
        // any subset of symbols is valid input, and a constant parents to its enum — with the enum
        // missing there is no scope to resolve it through, and guessing the global one would make it
        // resolve from everywhere.
        var module = Module("Mod1");
        var (_, red) = Enum(module.Uri, "Colours", "Red", AccessModifier.Public);

        var tree = ScopeTreeBuilder.Build([module, red]);

        Assert.IsEmpty(tree.ScopeFor(module.Uri).DeclaredAs("Red"));
        Assert.IsEmpty(tree.Global.DeclaredAs("Red"));
    }

    [TestMethod]
    public void AnEnumDeclaredGlobally_PlacesItsMembersInTheGlobalScope()
    {
        // how the standard library's predefined enums (MS-VBAL §6.1.1) resolve: they belong to no module
        // of the project, so `vbSunday` comes off the global scope.
        var (@enum, sunday) = Enum(StaticSymbol.GlobalUri, "VbDayOfWeek", "vbSunday", AccessModifier.Public);

        var tree = ScopeTreeBuilder.Build([Module("Mod1"), @enum, sunday]);

        Assert.AreSame(sunday, tree.Global.DeclaredAs("vbSunday").Single());
    }

    private static (VBEnumMemberSymbol Enum, VBEnumConstMemberSymbol Constant) Enum(
        Uri ownerUri, string name, string constantName, AccessModifier access = AccessModifier.Implicit)
    {
        var @enum = new VBEnumMemberSymbol(
            Root, ownerUri, name, ScopeKind.Module, SymbolKindExt.Enum, VBUnknownType.TypeInfo, R, R, access);

        return (@enum, new VBEnumConstMemberSymbol(Root, @enum.Uri, constantName, ScopeKind.Module, SymbolKindExt.EnumMember, R, R));
    }

    [TestMethod]
    public void AProcedureWhoseModuleIsAbsent_IsParentedToTheGlobalScope()
    {
        var absentModuleUri = Module("Ghost").Uri;
        var procedure = Procedure(absentModuleUri, "Orphan");

        var tree = ScopeTreeBuilder.Build([procedure]);

        Assert.AreSame(tree.Global, tree.ScopeFor(procedure.Uri).Parent);
    }

    [TestMethod]
    public void AnEmptySymbolSet_YieldsAGlobalScopeOnly()
    {
        var tree = ScopeTreeBuilder.Build([]);

        Assert.IsNotNull(tree.Global);
        Assert.IsFalse(tree.TryGetScope(new Uri("file://rdcore-test#Nope"), out _));
        Assert.AreSame(tree.Global, tree.ScopeFor(new Uri("file://rdcore-test#Nope")));
    }

    [TestMethod]
    public void TheProjectScope_SitsBetweenTheModuleAndGlobalScopes()
    {
        var module = Module("Mod1");

        var tree = ScopeTreeBuilder.Build([module]);
        var project = tree.ScopeFor(module.Uri).Parent;

        Assert.AreEqual(LexicalScopeKind.Project, project!.Kind);
        Assert.AreSame(tree.Global, project.Parent);
    }

    [TestMethod]
    public void PublicStandardModuleMembers_AreVisibleInTheProjectScope()
    {
        var module = Module("Mod1");
        var api = Procedure(module.Uri, "DoWork", AccessModifier.Public);

        var tree = ScopeTreeBuilder.Build([module, api]);
        var projectScope = tree.ScopeFor(module.Uri).Parent!;

        Assert.AreSame(api, projectScope.DeclaredAs("DoWork").Single());
        Assert.AreSame(api, tree.ScopeFor(module.Uri).DeclaredAs("DoWork").Single(), "still declared in its own module too");
    }

    [TestMethod]
    public void PrivateStandardModuleMembers_StayModuleScoped()
    {
        var module = Module("Mod1");
        var secret = Field(module.Uri, "State", AccessModifier.Private);

        var tree = ScopeTreeBuilder.Build([module, secret]);

        Assert.AreSame(secret, tree.ScopeFor(module.Uri).DeclaredAs("State").Single());
        Assert.IsEmpty(tree.ScopeFor(module.Uri).Parent!.DeclaredAs("State"));
    }

    [TestMethod]
    public void ImplicitModuleFields_AreNotProjectVisible_ButImplicitProceduresAre()
    {
        var module = Module("Mod1");
        var field = Field(module.Uri, "Counter");           // implicit variable -> Private
        var procedure = Procedure(module.Uri, "Reset");     // implicit procedure -> Public

        var tree = ScopeTreeBuilder.Build([module, field, procedure]);
        var projectScope = tree.ScopeFor(module.Uri).Parent!;

        Assert.IsEmpty(projectScope.DeclaredAs("Counter"));
        Assert.AreSame(procedure, projectScope.DeclaredAs("Reset").Single());
    }

    [TestMethod]
    public void ClassModuleMembers_AreNotPlacedInTheProjectScope()
    {
        var @class = ClassModule("Widget");
        var api = Procedure(@class.Uri, "Refresh", AccessModifier.Public);

        var tree = ScopeTreeBuilder.Build([@class, api]);

        Assert.AreSame(api, tree.ScopeFor(@class.Uri).DeclaredAs("Refresh").Single(), "still an instance member");
        Assert.IsEmpty(tree.ScopeFor(@class.Uri).Parent!.DeclaredAs("Refresh"), "but not surfaced to sibling modules");
    }

    [TestMethod]
    public void TheModuleScope_CarriesTheModuleSymbolsDirectives()
    {
        var module = Module("Mod1") with { Directives = new ModuleDirectives(Explicit: true) };

        var tree = ScopeTreeBuilder.Build([module]);

        Assert.AreEqual(new ModuleDirectives(Explicit: true), tree.ScopeFor(module.Uri).Directives);
    }

    [TestMethod]
    public void EnclosingModuleDirectives_WalksUpFromAProcedureScope_ToItsModule()
    {
        var module = Module("Mod1") with { Directives = new ModuleDirectives(Explicit: true) };
        var procedure = Procedure(module.Uri, "DoWork");

        var tree = ScopeTreeBuilder.Build([module, procedure]);

        Assert.AreEqual(new ModuleDirectives(Explicit: true), tree.ScopeFor(procedure.Uri).EnclosingModuleDirectives());
    }

    [TestMethod]
    public void EnclosingModuleDirectives_IsNull_OutsideAnyModuleScope()
    {
        var tree = ScopeTreeBuilder.Build([]);

        Assert.IsNull(tree.Global.EnclosingModuleDirectives());
    }
}
