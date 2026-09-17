using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// The workspace resolver is composed over every parsed module, so a module's <c>As SomeType</c>
/// binds to a sibling module's <c>Type</c> / <c>Enum</c> — not just an intrinsic type name.
/// </summary>
[TestClass]
public sealed class WorkspaceSymbolResolverTests
{
    private static readonly Uri WorkspaceRoot = new("file://rdcore-test");

    private static Uri ModuleUri(string name) => new UriBuilder(WorkspaceRoot) { Fragment = name }.Uri;

    private static (Uri Uri, ModuleType ModuleType, ModuleParseResult Parse) Module(string name, string body)
        => (ModuleUri(name), ModuleType.StdModule, new ModuleParser().Parse(
            new Uri($"file:///c:/ws/{name}.bas"), $"Attribute VB_Name = \"{name}\"\r\n{body}"));

    private static (Uri Uri, ModuleType ModuleType, ModuleParseResult Parse) ClassModule(string name, string body)
        => (ModuleUri(name), ModuleType.ClassModule, new ModuleParser().Parse(
            new Uri($"file:///c:/ws/{name}.cls"), $"Attribute VB_Name = \"{name}\"\r\n{body}"));

    private static List<Symbol> Resolve(
        string moduleName, string moduleBody, params (Uri Uri, ModuleType ModuleType, ModuleParseResult Parse)[] siblings)
    {
        var target = Module(moduleName, moduleBody);
        var resolver = WorkspaceSymbolResolver.Compose(
            WorkspaceRoot, [.. siblings, target], new IntrinsicSymbolResolver());

        return [.. new SyntaxTreeSymbolProvider(WorkspaceRoot, target.Uri, target.ModuleType, target.Parse, resolver).ProvideSymbols()];
    }

    [TestMethod]
    public void ASiblingModulesUserDefinedType_BindsThroughTheWorkspaceResolver()
    {
        var types = Module("Types", "Public Type TPoint\r\n    X As Long\r\n    Y As Long\r\nEnd Type\r\n");

        var field = Resolve("Consumer", "Public Origin As TPoint\r\n", types)
            .OfType<VBModuleFieldVariableMemberSymbol>().Single();

        var udt = Assert.IsInstanceOfType<VBUserDefinedType>(field.ResolvedType);
        Assert.AreEqual("TPoint", udt.Name);
        Assert.HasCount(2, udt.Members);
    }

    [TestMethod]
    public void ASiblingModulesEnum_BindsThroughTheWorkspaceResolver()
    {
        var enums = Module("Enums", "Public Enum Colour\r\n    Red\r\n    Green\r\nEnd Enum\r\n");

        var field = Resolve("Consumer", "Public Selected As Colour\r\n", enums)
            .OfType<VBModuleFieldVariableMemberSymbol>().Single();

        Assert.AreEqual("Colour", Assert.IsInstanceOfType<VBEnumType>(field.ResolvedType).Name);
    }

    [TestMethod]
    public void AnIntrinsicTypeName_StillBinds_ThroughTheFallback()
    {
        var field = Resolve("Consumer", "Public Total As Long\r\n")
            .OfType<VBModuleFieldVariableMemberSymbol>().Single();

        Assert.AreEqual(VBTypeNames.VBLong, field.ResolvedType.Name);
    }

    [TestMethod]
    public void AnUnknownTypeName_StaysUnknown()
    {
        var field = Resolve("Consumer", "Public Widget As CWidget\r\n")
            .OfType<VBModuleFieldVariableMemberSymbol>().Single();

        Assert.AreEqual(VBTypeNames.VBUnknown, field.ResolvedType.Name);
    }

    [TestMethod]
    public void AClassModulesOwnFieldsAndProcedures_AreCarriedOnItsSynthesizedModuleSymbol()
        // a class's members can't ride on its module symbol the way a Type's fields ride on it (that
        // falls out of one AST node's own children) - they're separate top-level declarations, only
        // known once SyntaxTreeSymbolProvider has run. Proves Compose's second pass actually wires them
        // up for real parsed source, not just hand-constructed symbols.
    {
        var target = ClassModule("Widget", "Public Total As Long\r\nPrivate Sub DoWork()\r\nEnd Sub\r\n");
        var resolver = WorkspaceSymbolResolver.Compose(WorkspaceRoot, [target], new IntrinsicSymbolResolver());

        var module = Assert.IsInstanceOfType<VBClassModuleSymbol>(
            resolver.Resolve("Widget", ScopeKind.Global, target.Uri).Symbol);

        Assert.HasCount(2, module.Members);
        Assert.IsTrue(module.Members.Any(member => member.Name == "Total"));
        Assert.IsTrue(module.Members.Any(member => member.Name == "DoWork"));
    }

    [TestMethod]
    public void AClassModulesDefaultInterface_ExcludesOnlyPrivateMembers()
        // Members stays the full, unfiltered declaration surface (asserted above); DefaultInterfaceMembers
        // is the separate, precomputed default-interface view - Public/implicit/Friend, never Private -
        // that New/As-type/Me read directly instead of rebuilding at resolution time.
    {
        var target = ClassModule("Widget",
            "Public Sub PublicSub()\r\nEnd Sub\r\nFriend Sub FriendSub()\r\nEnd Sub\r\nPrivate Sub PrivateSub()\r\nEnd Sub\r\n");
        var resolver = WorkspaceSymbolResolver.Compose(WorkspaceRoot, [target], new IntrinsicSymbolResolver());

        var module = Assert.IsInstanceOfType<VBClassModuleSymbol>(
            resolver.Resolve("Widget", ScopeKind.Global, target.Uri).Symbol);

        Assert.HasCount(2, module.DefaultInterfaceMembers);
        Assert.IsTrue(module.DefaultInterfaceMembers.Any(member => member.Name == "PublicSub"));
        Assert.IsTrue(module.DefaultInterfaceMembers.Any(member => member.Name == "FriendSub"));
    }

    [TestMethod]
    public void AClassModulesMembers_ExcludeProcedureLocals()
        // Members is the class's own API surface - a procedure's Dim locals parent to the procedure,
        // not the module, and must not leak into it.
    {
        var target = ClassModule("Widget", "Private Sub DoWork()\r\n    Dim i As Long\r\nEnd Sub\r\n");
        var resolver = WorkspaceSymbolResolver.Compose(WorkspaceRoot, [target], new IntrinsicSymbolResolver());

        var module = Assert.IsInstanceOfType<VBClassModuleSymbol>(
            resolver.Resolve("Widget", ScopeKind.Global, target.Uri).Symbol);

        Assert.HasCount(1, module.Members);
        Assert.AreEqual("DoWork", module.Members[0].Name);
    }

    [TestMethod]
    public void AStandardModulesOwnFields_AreAlsoCarriedOnItsSynthesizedModuleSymbol()
        // Members lives on the shared VBModuleSymbol base - Compose's second pass treats every module
        // kind uniformly, so a standard module gets the same treatment as a class module.
    {
        var target = Module("Globals", "Public Total As Long\r\n");
        var resolver = WorkspaceSymbolResolver.Compose(WorkspaceRoot, [target], new IntrinsicSymbolResolver());

        var module = Assert.IsInstanceOfType<VBStandardModuleSymbol>(
            resolver.Resolve("Globals", ScopeKind.Global, target.Uri).Symbol);

        Assert.HasCount(1, module.Members);
        Assert.AreEqual("Total", module.Members[0].Name);
    }

    [TestMethod]
    public void AModuleDeclaringOptionExplicit_CarriesItOnTheSynthesizedModuleSymbol()
    {
        var target = Module("Strict", "Option Explicit\r\n");
        var resolver = WorkspaceSymbolResolver.Compose(WorkspaceRoot, [target], new IntrinsicSymbolResolver());

        var module = Assert.IsInstanceOfType<VBModuleSymbol>(
            resolver.Resolve("Strict", ScopeKind.Global, target.Uri).Symbol);

        Assert.IsTrue(module.Directives.Explicit);
    }

    [TestMethod]
    public void AModuleWithoutOptionExplicit_DoesNotCarryIt()
    {
        var target = Module("Loose", "Public Total As Long\r\n");
        var resolver = WorkspaceSymbolResolver.Compose(WorkspaceRoot, [target], new IntrinsicSymbolResolver());

        var module = Assert.IsInstanceOfType<VBModuleSymbol>(
            resolver.Resolve("Loose", ScopeKind.Global, target.Uri).Symbol);

        Assert.IsFalse(module.Directives.Explicit);
    }

    [TestMethod]
    public void AClassModuleWithoutVB_Creatable_DefaultsToCreatable()
        // VBE's own default: a class module that declares no Attribute VB_Creatable is creatable.
    {
        var target = ClassModule("Widget", "Public Total As Long\r\n");
        var resolver = WorkspaceSymbolResolver.Compose(WorkspaceRoot, [target], new IntrinsicSymbolResolver());

        var module = Assert.IsInstanceOfType<VBClassModuleSymbol>(
            resolver.Resolve("Widget", ScopeKind.Global, target.Uri).Symbol);

        Assert.IsTrue(module.GetProperty(SymbolProperties.Creatable));
    }

    [TestMethod]
    public void AClassModuleDeclaringVB_CreatableFalse_IsNotCreatable()
    {
        var target = ClassModule("Widget", "Attribute VB_Creatable = False\r\nPublic Total As Long\r\n");
        var resolver = WorkspaceSymbolResolver.Compose(WorkspaceRoot, [target], new IntrinsicSymbolResolver());

        var module = Assert.IsInstanceOfType<VBClassModuleSymbol>(
            resolver.Resolve("Widget", ScopeKind.Global, target.Uri).Symbol);

        Assert.IsFalse(module.GetProperty(SymbolProperties.Creatable));
    }

    [TestMethod]
    public void AProjectName_SynthesizesAResolvableVBProjectSymbol()
    {
        var target = Module("Globals", "Public Total As Long\r\n");
        var resolver = WorkspaceSymbolResolver.Compose(WorkspaceRoot, [target], new IntrinsicSymbolResolver(), projectName: "MyProject");

        var project = Assert.IsInstanceOfType<VBProjectSymbol>(
            resolver.Resolve("MyProject", ScopeKind.Global, target.Uri).Symbol);

        Assert.AreEqual("MyProject", project.Name);
    }
}
