using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// MS-VBAL §5.2.3.1.1 and §2.5.1: a variable declared with an <c>As New</c> clause (an <c>&lt;as-auto-object&gt;</c>)
/// is an <em>automatic instantiation variable</em>, and so is the default instance variable of a predeclared class
/// (§5.2.4.1.2 declares it "as if" <c>As New</c>). Parse-driven: real source through <c>WorkspaceSymbolResolver</c>.
/// </summary>
[TestClass]
public sealed class AutoInstantiatedVariableTests
{
    private static readonly Uri Root = new("file://rdcore-test");

    private static Uri ModuleUri(string name) => new UriBuilder(Root) { Fragment = name }.Uri;

    private static (Uri Uri, ModuleType ModuleType, ModuleParseResult Parse) Widget(string attributes = "")
        => (ModuleUri("Widget"), ModuleType.ClassModule, new ModuleParser().Parse(
            new Uri("file:///c:/ws/Widget.cls"), $"Attribute VB_Name = \"Widget\"\r\n{attributes}Public Size As Long\r\n"));

    private static ISymbolResolver ComposeMain(string body)
    {
        var main = (ModuleUri("Main"), ModuleType.StdModule, new ModuleParser().Parse(
            new Uri("file:///c:/ws/Main.bas"), $"Attribute VB_Name = \"Main\"\r\n{body}"));
        return WorkspaceSymbolResolver.Compose(Root, [Widget(), main], new IntrinsicSymbolResolver());
    }

    private static bool IsAutoInstantiated(ISymbolResolver resolver, string name, string scope)
    {
        var result = resolver.ResolveValue(name, ScopeKind.Unallocated, ModuleUri(scope));
        return Assert.IsInstanceOfType<Symbol>(result.Symbol, $"'{name}' should resolve from {scope}").GetProperty(SymbolProperties.AutoInstantiated);
    }

    [TestMethod]
    public void AModuleFieldDeclaredAsNew_IsAnAutoInstantiationVariable()
    {
        var resolver = ComposeMain("Public Auto As New Widget\r\nPublic Plain As Widget\r\n");

        Assert.IsTrue(IsAutoInstantiated(resolver, "Auto", "Main"));
        Assert.IsFalse(IsAutoInstantiated(resolver, "Plain", "Main"));
    }

    [TestMethod]
    public void ALocalDeclaredAsNew_IsAnAutoInstantiationVariable()
    {
        var resolver = ComposeMain("Sub Run()\r\nDim Auto As New Widget\r\nDim Plain As Widget\r\nEnd Sub\r\n");

        Assert.IsTrue(IsAutoInstantiated(resolver, "Auto", "Main.Run"));
        Assert.IsFalse(IsAutoInstantiated(resolver, "Plain", "Main.Run"));
    }

    [TestMethod]
    public void AnArrayLocalDeclaredAsNew_IsFlagged_ItsDependentVariablesBeingTheAutoInstantiationOnes()
        // 5.2.3.1.1: "each dependent variable of the defined array variable is an automatic instantiation variable".
    {
        var resolver = ComposeMain("Sub Run()\r\nDim Auto(1 To 3) As New Widget\r\nEnd Sub\r\n");

        Assert.IsTrue(IsAutoInstantiated(resolver, "Auto", "Main.Run"));
    }

    [TestMethod]
    public void AUserDefinedTypeFieldDeclaredAsNew_IsAnAutoInstantiationVariable()
        // 5.2.3.3: the dependent variable of any variable of the UDT's type is one.
    {
        var resolver = ComposeMain("Public Type Holder\r\n    Auto As New Widget\r\n    Plain As Widget\r\nEnd Type\r\n");

        var holder = Assert.IsInstanceOfType<VBUserDefinedTypeMemberSymbol>(resolver.ResolveType("Holder", ScopeKind.Global, ModuleUri("Main")).Symbol);

        Assert.IsTrue(holder.Members.Single(member => member.Name == "Auto").GetProperty(SymbolProperties.AutoInstantiated));
        Assert.IsFalse(holder.Members.Single(member => member.Name == "Plain").GetProperty(SymbolProperties.AutoInstantiated));
    }

    [TestMethod]
    public void ThePredeclaredInstance_IsAnAutoInstantiationVariable()
        // 5.2.4.1.2: created "as if declared in a <module-variable-declaration> containing an <as-autoobject> element".
    {
        var main = (ModuleUri("Main"), ModuleType.StdModule, new ModuleParser().Parse(new Uri("file:///c:/ws/Main.bas"), "Attribute VB_Name = \"Main\"\r\n"));
        var resolver = WorkspaceSymbolResolver.Compose(Root, [Widget("Attribute VB_PredeclaredId = True\r\n"), main], new IntrinsicSymbolResolver());

        var instance = Assert.IsInstanceOfType<VBPredeclaredInstanceSymbol>(resolver.ResolveValue("Widget", ScopeKind.Unallocated, ModuleUri("Main")).Symbol);

        Assert.IsTrue(instance.GetProperty(SymbolProperties.AutoInstantiated));
    }
}
