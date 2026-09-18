using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// Legacy-Rubberduck issue #973, "Evil Test Case 2 for Parser/Resolver" — one identifier
/// (<c>MyProject</c> / <c>MyModule</c> / <c>MyProc</c>) reused at once as a module name, a
/// <c>Const</c>, a <c>Type</c>, a field, a procedure, a local, and an interface member. This pins the
/// <em>declaration-scope</em> behaviour the resolver can settle today; the <c>With</c>-nested,
/// <c>Set</c>-heavy body of <c>MyProc1</c> and the <c>Implements</c> overlap in <c>Class</c> are covered
/// by the ignored case below.
/// <para>
/// The collisions are legal, not duplicates: <strong>MS-VBAL §5.6.4</strong> binds a name in one of two
/// contexts. A simple name expression uses the default binding context, whose candidates
/// (<strong>§5.6.10</strong>) include no user-defined type; <c>As X</c> and <c>New X</c> use the type
/// binding context, whose candidates are only types, modules and the project. A <c>Const</c> and a
/// <c>Type</c> of one name therefore never compete for the same lookup.
/// </para>
/// </summary>
[TestClass]
public sealed class EvilTestCase2ResolverTests
{
    private static readonly Uri WorkspaceRoot = new("file://rdcore-test");

    // MyModule.bas — every module-scope name collides; MyProc1 shadows them with locals.
    private const string MyModuleSource = """
        Attribute VB_Name = "MyModule"
        Option Explicit

        Public Const MyProject As String = "String"
        Public Const MyModule As String = MyProject
        Public Const MyConst As String = MyModule

        Public MyProc As Interface

        Type MyProc
          MyModule As String
          MyProject As String
        End Type

        Type MyModule
          MyProject As MyProc
        End Type

        Type MyProject
          MyModule As MyModule
        End Type

        Sub MyProc1()
          Dim MyProject As Interface
          Dim MyModule
          Dim MyCircular
          Dim o
        End Sub
        """;

    private static (ISymbolResolver Resolver, Uri ModuleUri, Uri ProcUri) Compose()
    {
        var moduleUri = new UriBuilder(WorkspaceRoot) { Fragment = "MyModule" }.Uri;
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/MyModule.bas"), MyModuleSource);

        var resolver = WorkspaceSymbolResolver.Compose(
            WorkspaceRoot, [(moduleUri, ModuleType.StdModule, parse)], new IntrinsicSymbolResolver());

        var procUri = new UriBuilder(WorkspaceRoot) { Fragment = "MyModule.MyProc1" }.Uri;
        return (resolver, moduleUri, procUri);
    }

    private static SymbolResolutionResult ResolveValue(string name, Uri from)
        => Compose().Resolver.ResolveValue(name, ScopeKind.Unallocated, from);

    private static SymbolResolutionResult ResolveType(string name, Uri from)
        => Compose().Resolver.ResolveType(name, ScopeKind.Unallocated, from);

    [TestMethod]
    public void FromMyProc1_MyProject_BindsTheLocal_NotTheModuleConst()
    {
        var (resolver, _, procUri) = Compose();

        var result = resolver.ResolveValue("MyProject", ScopeKind.Unallocated, procUri);

        var local = Assert.IsInstanceOfType<VBLocalVariableSymbol>(result.Symbol);
        Assert.AreEqual(procUri, local.ParentUri);
    }

    [TestMethod]
    public void FromModuleScope_MyProject_BindsTheConst_TheTypeIsNotInTheDefaultContext()
        // MS-VBAL 5.6.10: a Const and a Type of one name do not compete - the Type is only a candidate in
        // the type binding context (see the ResolveType cases below).
    {
        var result = ResolveValue("MyProject", Compose().ModuleUri);

        Assert.IsTrue(result.IsResolved);
        Assert.IsInstanceOfType<VBConstantMemberSymbol>(result.Symbol);
    }

    [TestMethod]
    public void FromModuleScope_MyProc_BindsTheField_TheTypeIsNotInTheDefaultContext()
        => Assert.IsInstanceOfType<VBModuleFieldVariableMemberSymbol>(
            ResolveValue("MyProc", Compose().ModuleUri).Symbol);

    [TestMethod]
    public void FromModuleScope_MyModule_BindsTheConst_NotTheModuleNorTheType()
        // the module-level Const is the enclosing-module tier; the module of the same name is a later tier.
        => Assert.IsInstanceOfType<VBConstantMemberSymbol>(
            ResolveValue("MyModule", Compose().ModuleUri).Symbol);

    [TestMethod]
    public void FromModuleScope_MyConst_ResolvesUnambiguously()
        => Assert.IsInstanceOfType<VBConstantMemberSymbol>(
            ResolveValue("MyConst", Compose().ModuleUri).Symbol);

    [TestMethod]
    public void AsTypeContext_FromModuleScope_MyProject_BindsTheType_NotTheConst()
    {
        var result = ResolveType("MyProject", Compose().ModuleUri);

        Assert.IsTrue(result.IsResolved);
        Assert.IsInstanceOfType<VBUserDefinedTypeMemberSymbol>(result.Symbol);
    }

    [TestMethod]
    public void AsTypeContext_FromModuleScope_MyProc_BindsTheType_NotTheField()
        => Assert.IsInstanceOfType<VBUserDefinedTypeMemberSymbol>(
            ResolveType("MyProc", Compose().ModuleUri).Symbol);

    [TestMethod]
    public void AsTypeContext_FromModuleScope_MyModule_BindsTheModulesOwnType_NotTheModule()
        // the enclosing module's types are the first tier of the type binding context; the module named
        // MyModule is a later one.
        => Assert.IsInstanceOfType<VBUserDefinedTypeMemberSymbol>(
            ResolveType("MyModule", Compose().ModuleUri).Symbol);

    [TestMethod]
    public void AsTypeContext_FromMyProc1_MyProject_BindsTheType_NotTheLocal()
        // the local Dim MyProject As Interface is never a candidate in the type binding context.
        => Assert.IsInstanceOfType<VBUserDefinedTypeMemberSymbol>(
            ResolveType("MyProject", Compose().ProcUri).Symbol);

    [TestMethod]
    public void AsTypeContext_AConstant_IsNotAType()
        => Assert.IsTrue(ResolveType("MyConst", Compose().ModuleUri).IsUnbound);

    [TestMethod]
    public void FromGlobalScope_MyModule_ResolvesToTheModuleSymbol()
        => Assert.IsInstanceOfType<VBStandardModuleSymbol>(
            ResolveValue("MyModule", GlobalSymbols.UnresolvedSymbol.Uri).Symbol);

    [TestMethod]
    [Ignore("Needs statement-node AST (parser §P): With-nesting, Set member access, Implements overlap.")]
    public void MyProc1_Body_And_InterfaceImplementation_ResolveCorrectly()
    {
    }
}
