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
/// <c>Set</c>-heavy body of <c>MyProc1</c> and the <c>Implements</c> overlap in <c>Class</c> need the
/// statement-node AST (parser §P) and are covered by the ignored case below.
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
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/MyModule.bas"), ModuleType.StdModule, MyModuleSource);

        var resolver = WorkspaceSymbolResolver.Compose(
            WorkspaceRoot, [(moduleUri, parse)], new IntrinsicSymbolResolver());

        var procUri = new UriBuilder(WorkspaceRoot) { Fragment = "MyModule.MyProc1" }.Uri;
        return (resolver, moduleUri, procUri);
    }

    private static SymbolResolutionResult Resolve(string name, Uri from)
        => Compose().Resolver.Resolve(name, ScopeKind.Unallocated, from);

    [TestMethod]
    public void FromMyProc1_MyProject_BindsTheLocal_NotTheModuleConstOrType()
    {
        var (resolver, _, procUri) = Compose();

        var result = resolver.Resolve("MyProject", ScopeKind.Unallocated, procUri);

        var local = Assert.IsInstanceOfType<VBLocalVariableSymbol>(result.Symbol);
        Assert.AreEqual(procUri, local.ParentUri);
    }

    [TestMethod]
    public void FromModuleScope_MyProject_IsADuplicateDeclaration_ConstAndType()
    {
        var (resolver, moduleUri, _) = Compose();

        var result = resolver.Resolve("MyProject", ScopeKind.Unallocated, moduleUri);

        Assert.AreEqual(VBCompileErrorId.DuplicateDeclaration, result.ErrorId);
        CollectionAssert.AreEquivalent(
            new[] { nameof(VBConstantMemberSymbol), nameof(VBUserDefinedTypeMemberSymbol) },
            result.Candidates.Select(c => c.GetType().Name).ToArray());
    }

    [TestMethod]
    public void FromModuleScope_MyProc_IsADuplicateDeclaration_FieldAndType()
        => Assert.AreEqual(VBCompileErrorId.DuplicateDeclaration,
            Resolve("MyProc", Compose().ModuleUri).ErrorId);

    [TestMethod]
    public void FromModuleScope_MyModule_IsADuplicateDeclaration_ConstAndType()
        => Assert.AreEqual(VBCompileErrorId.DuplicateDeclaration,
            Resolve("MyModule", Compose().ModuleUri).ErrorId);

    [TestMethod]
    public void FromModuleScope_MyConst_ResolvesUnambiguously()
        => Assert.IsInstanceOfType<VBConstantMemberSymbol>(
            Resolve("MyConst", Compose().ModuleUri).Symbol);

    [TestMethod]
    public void FromGlobalScope_MyModule_ResolvesToTheModuleSymbol()
        => Assert.IsInstanceOfType<VBStandardModuleSymbol>(
            Resolve("MyModule", GlobalSymbols.UnresolvedSymbol.Uri).Symbol);

    [TestMethod]
    [Ignore("Needs statement-node AST (parser §P): With-nesting, Set member access, Implements overlap.")]
    public void MyProc1_Body_And_InterfaceImplementation_ResolveCorrectly()
    {
    }
}
