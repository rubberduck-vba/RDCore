using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Tests.Model.Symbols;

/// <summary>
/// The <c>Get</c>, <c>Let</c> and <c>Set</c> accessors of one property share a name but each define a scope of their
/// own: their parameters and locals are theirs alone. The <c>Get</c> accessor keeps the property's own identity; the
/// <c>Let</c> and <c>Set</c> accessors are addressed by the reserved word after the name.
/// </summary>
[TestClass]
public sealed class PropertyAccessorScopeTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SourceRange R = SourceRange.Empty;

    private static Uri ModuleUri(string name) => new UriBuilder(Root) { Fragment = name }.Uri;

    [TestMethod]
    public void EachAccessorHasAnIdentityOfItsOwn_TheGetKeepingThePropertysOwn()
    {
        var module = new Uri("file://rdcore-test#Cls");
        var get = new VBPropertyGetMemberSymbol(Root, module, ScopeKind.Module, "Item", R, R, AccessModifier.Public);
        var let = new VBPropertyLetMemberSymbol(Root, module, "Item", ScopeKind.Module, SymbolKindExt.Property, VBVoidType.TypeInfo, R, R, AccessModifier.Public);
        var set = new VBPropertySetMemberSymbol(Root, module, "Item", ScopeKind.Module, SymbolKindExt.Property, VBVoidType.TypeInfo, R, R, AccessModifier.Public);

        Assert.AreEqual("Cls.Item", get.Uri.Fragment.TrimStart('#'));
        Assert.AreEqual("Cls.Item.Let", let.Uri.Fragment.TrimStart('#'));
        Assert.AreEqual("Cls.Item.Set", set.Uri.Fragment.TrimStart('#'));
        Assert.HasCount(3, new HashSet<SemanticId> { get.SemanticId, let.SemanticId, set.SemanticId });
        Assert.IsTrue(new[] { get.Name, let.Name, set.Name }.All(name => name == "Item"), "the accessors still share the property's name");
    }

    [TestMethod]
    public void ACopyOfAnAccessor_KeepsItsIdentity()
        => Assert.AreEqual("Cls.Item.Set", (new VBPropertySetMemberSymbol(
                Root, new Uri("file://rdcore-test#Cls"), "Item", ScopeKind.Module, SymbolKindExt.Property, VBVoidType.TypeInfo, R, R, AccessModifier.Public)
            with { AccessModifier = AccessModifier.Private }).Uri.Fragment.TrimStart('#'));

    private const string Property = """
        Attribute VB_Name = "Cls"
        Option Explicit

        Public Property Get Item(ByVal key As String) As Long
          Dim tmp As Long
        End Property

        Public Property Let Item(ByVal key As String, ByVal value As Long)
          Dim tmp As String
        End Property

        Public Property Set Item(ByVal key As String, ByVal value As Object)
          Dim tmp As Date
        End Property
        """;

    private static ISymbolResolver Composed()
        => WorkspaceSymbolResolver.Compose(
            Root, [(ModuleUri("Cls"), ModuleType.ClassModule, new ModuleParser().Parse(new Uri("file:///c:/ws/Cls.cls"), Property))],
            new IntrinsicSymbolResolver());

    private static VBType TypeOf(ISymbolResolver resolver, string name, string accessorScope)
        => Assert.IsInstanceOfType<ITypedSymbol>(resolver.ResolveValue(name, ScopeKind.Unallocated, ModuleUri(accessorScope)).Symbol, $"'{name}' from {accessorScope}").ResolvedType;

    [TestMethod]
    public void EachAccessorSeesItsOwnParameters()
    {
        var resolver = Composed();

        Assert.IsInstanceOfType<VBObjectType>(TypeOf(resolver, "value", "Cls.Item.Set"));
        Assert.AreEqual(VBLongType.TypeInfo, TypeOf(resolver, "value", "Cls.Item.Let"));
        Assert.IsTrue(resolver.ResolveValue("value", ScopeKind.Unallocated, ModuleUri("Cls.Item")).IsUnbound,
            "the Get has no `value`: another accessor's parameter is not in its scope");
    }

    [TestMethod]
    public void EachAccessorSeesItsOwnLocals()
    {
        var resolver = Composed();

        Assert.AreEqual(VBLongType.TypeInfo, TypeOf(resolver, "tmp", "Cls.Item"));
        Assert.AreEqual(VBStringType.TypeInfo, TypeOf(resolver, "tmp", "Cls.Item.Let"));
        Assert.AreEqual(VBDateType.TypeInfo, TypeOf(resolver, "tmp", "Cls.Item.Set"));
    }

    [TestMethod]
    public void TheProperty_StillResolvesByName_FromItsModule()
        // three accessors of one name at the module tier are one property, not a duplicate declaration.
    {
        var result = Composed().ResolveValue("Item", ScopeKind.Unallocated, ModuleUri("Cls"));

        Assert.IsTrue(result.IsResolved, result.ErrorId?.ToString());
        Assert.IsInstanceOfType<IVBPropertyMemberSymbol>(result.Symbol);
    }
}
