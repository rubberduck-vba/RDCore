using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Model.Symbols;

/// <summary>
/// <see cref="ISymbolResolver.ResolveMember"/>: the right-hand side of a member access on a project or a procedural
/// module (<strong>MS-VBAL §5.6.12</strong>) — <c>Util.Helper</c>, <c>VBA.Strings</c>, <c>VBA.LenB</c>.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.12 Member Access Expressions")]
public sealed class ScopeTreeResolveMemberTests
{
    private const string Library = "VBA";

    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SourceRange R = SourceRange.Empty;

    private static VBFunctionMemberSymbol Function(Uri moduleUri, string name, AccessModifier access = AccessModifier.Public)
        => new(Root, moduleUri, name, ScopeKind.Module, SymbolKindExt.Function, VBLongType.TypeInfo, R, R, access);

    private static Symbol InLibrary(Symbol symbol) => symbol.With(SymbolProperties.Library, Library);

    // a project of the enclosing workspace, the library, and three modules: Strings (the library's), Util and Other.
    private sealed class World
    {
        public VBProjectSymbol Enclosing { get; } = new(Root, "Project1");
        public VBProjectSymbol LibraryProject { get; } = (VBProjectSymbol)InLibrary(new VBProjectSymbol(Root, Library));
        public VBStandardModuleSymbol Strings { get; } = (VBStandardModuleSymbol)InLibrary(new VBStandardModuleSymbol(Root, Root, "Strings"));
        public VBStandardModuleSymbol Util { get; } = new(Root, Root, "Util");
        public VBStandardModuleSymbol Other { get; } = new(Root, Root, "Other");
        public Symbol LenB { get; }
        public Symbol Helper { get; }
        public Symbol Secret { get; }
        public Symbol OtherHelper { get; }
        public ISymbolResolver Resolver { get; }

        public World()
        {
            LenB = InLibrary(Function(Strings.Uri, "LenB"));
            Helper = Function(Util.Uri, "Helper");
            Secret = Function(Util.Uri, "Secret", AccessModifier.Private);
            OtherHelper = Function(Other.Uri, "Helper");
            Resolver = new ScopeTreeSymbolResolver(ScopeTreeBuilder.Build(
                [Enclosing, LibraryProject, Strings, Util, Other, LenB, Helper, Secret, OtherHelper]));
        }
    }

    private static readonly World W = new();

    #region a procedural module's members

    [TestMethod]
    public void AModulesPublicMember_Resolves()
        => Assert.AreEqual(W.Helper.Uri, W.Resolver.ResolveMember(W.Util, "Helper", W.Other.Uri).Symbol?.Uri);

    [TestMethod]
    public void AModulesPrivateMember_IsNotAccessibleFromAnotherModule()
        => Assert.IsTrue(W.Resolver.ResolveMember(W.Util, "Secret", W.Other.Uri).IsUnbound);

    [TestMethod]
    public void AModulesPrivateMember_IsAccessibleFromWithinTheModule()
        => Assert.IsTrue(W.Resolver.ResolveMember(W.Util, "Secret", W.Util.Uri).IsResolved);

    [TestMethod]
    public void ANameTheModuleDoesNotDeclare_IsUnbound()
        => Assert.IsTrue(W.Resolver.ResolveMember(W.Util, "Nope", W.Other.Uri).IsUnbound);

    [TestMethod]
    public void ANameAnotherModuleDeclares_IsNotAMemberOfThisOne()
        => Assert.IsTrue(W.Resolver.ResolveMember(W.Strings, "Helper", W.Util.Uri).IsUnbound);

    #endregion

    #region a project's members, in the order MS-VBAL §5.6.12 gives them

    [TestMethod]
    public void TheLibrary_NamesItsProceduralModule()
        => Assert.AreEqual(W.Strings.Uri, W.Resolver.ResolveMember(W.LibraryProject, "Strings", W.Util.Uri).Symbol?.Uri);

    [TestMethod]
    public void TheLibrary_HasTheMemberExactlyOneOfItsModulesHas()
        // `VBA.LenB`: no module of the library is called LenB, and exactly one has a member that is.
        => Assert.AreEqual(W.LenB.Uri, W.Resolver.ResolveMember(W.LibraryProject, "LenB", W.Util.Uri).Symbol?.Uri);

    [TestMethod]
    public void TheLibrary_DoesNotHaveTheWorkspacesMembers()
        => Assert.IsTrue(W.Resolver.ResolveMember(W.LibraryProject, "Helper", W.Util.Uri).IsUnbound);

    [TestMethod]
    public void TheLibrary_DoesNotHaveTheWorkspacesModules()
        => Assert.IsTrue(W.Resolver.ResolveMember(W.LibraryProject, "Util", W.Util.Uri).IsUnbound);

    [TestMethod]
    public void TheEnclosingProject_NamesItsProceduralModule()
        => Assert.AreEqual(W.Util.Uri, W.Resolver.ResolveMember(W.Enclosing, "Util", W.Other.Uri).Symbol?.Uri);

    [TestMethod]
    public void TheEnclosingProject_DoesNotHaveTheLibrarysModulesOrMembers()
    {
        Assert.IsTrue(W.Resolver.ResolveMember(W.Enclosing, "Strings", W.Util.Uri).IsUnbound);
        Assert.IsTrue(W.Resolver.ResolveMember(W.Enclosing, "LenB", W.Util.Uri).IsUnbound);
    }

    [TestMethod]
    public void TheEnclosingProject_NamesTheProjectsItCanSee()
        // `Project1.VBA.LenB`.
        => Assert.AreEqual(W.LibraryProject.Uri, W.Resolver.ResolveMember(W.Enclosing, Library, W.Util.Uri).Symbol?.Uri);

    [TestMethod]
    public void TheLibrary_DoesNotNameProjects()
        => Assert.IsTrue(W.Resolver.ResolveMember(W.LibraryProject, "Project1", W.Util.Uri).IsUnbound);

    [TestMethod]
    public void AMemberTwoModulesDeclare_IsAmbiguous()
    {
        // "exactly one of the procedural modules" - two of them have a Helper, and the reference has to say which.
        var result = W.Resolver.ResolveMember(W.Enclosing, "Helper", W.Strings.Uri);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.AmbiguousName, result.ErrorId);
    }

    [TestMethod]
    public void AModuleOfTheSameNameAsAMember_ComesFirst()
    {
        // the order is the specification's: a procedural module named so is what the project's member is.
        var shadow = new VBStandardModuleSymbol(Root, Root, "Helper");
        var resolver = new ScopeTreeSymbolResolver(ScopeTreeBuilder.Build([W.Enclosing, shadow, W.Util, W.Helper]));

        Assert.AreEqual(shadow.Uri, resolver.ResolveMember(W.Enclosing, "Helper", W.Util.Uri).Symbol?.Uri);
    }

    #endregion

    [TestMethod]
    public void AClassModule_IsNotAnOwner()
    {
        // a class's members are reached through an instance, which is a member access on a value.
        var widget = new VBClassModuleSymbol(Root, Root, "Widget");
        var resolver = new ScopeTreeSymbolResolver(ScopeTreeBuilder.Build([widget, Function(widget.Uri, "Size")]));

        Assert.IsTrue(resolver.ResolveMember(widget, "Size", widget.Uri).IsUnbound);
    }
}
