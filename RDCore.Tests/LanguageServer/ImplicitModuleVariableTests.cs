using NSubstitute;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// <see cref="ImplicitDeclarationScope"/>: where the variable a reference to an undeclared name declares lives.
/// <strong>MS-VBAL §5.6.10</strong> makes it a local of the procedure; an environment that works the way a BASIC does
/// has it declared once, at module level, so that the name is the same variable in every procedure and outlives
/// them all.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.10 Simple Name Expressions")]
public sealed class ImplicitModuleVariableTests
{
    private static readonly Uri WorkspaceRoot = TestUri.WorkspaceRoot();
    private static readonly Uri ModuleUri = TestUri.TestModuleUri();

    private static List<Symbol> Provide(string source, ImplicitDeclarationScope scope, ISymbolResolver? resolver = null)
    {
        var parseResult = new ModuleParser().Parse(TestUri.TestModuleUri(), source);
        return [.. new SyntaxTreeSymbolProvider(
            WorkspaceRoot, ModuleUri, ModuleType.StdModule, parseResult, resolver ?? Substitute.For<ISymbolResolver>(),
            withImplicitDeclarations: true, scope).ProvideSymbols()];
    }

    [TestMethod]
    public void ByDefault_AnUndeclaredName_IsALocalOfItsProcedure()
    {
        var symbols = Provide("Sub Foo()\r\nA = 42\r\nEnd Sub\r\n", ImplicitDeclarationScope.Procedure);

        var local = symbols.OfType<VBLocalVariableSymbol>().Single(symbol => symbol.Name == "A");
        Assert.AreEqual(LocalDeclarationKind.Implicit, local.DeclaredBy);
        Assert.IsEmpty(symbols.OfType<VBModuleFieldVariableMemberSymbol>());
    }

    [TestMethod]
    public void AtModuleScope_AnUndeclaredName_IsAVariantOfTheModule()
    {
        var symbols = Provide("Sub Foo()\r\nA = 42\r\nEnd Sub\r\n", ImplicitDeclarationScope.Module);

        var variable = symbols.OfType<VBModuleFieldVariableMemberSymbol>().Single(symbol => symbol.Name == "A");
        Assert.AreEqual(VBVariantType.TypeInfo, variable.ResolvedType);
        Assert.AreEqual(ModuleUri.AbsoluteUri, variable.ParentUri.AbsoluteUri, "a member of the module, not of the procedure");
        Assert.IsTrue(variable.GetProperty(SymbolProperties.ImplicitlyDeclared));
        Assert.IsEmpty(symbols.OfType<VBLocalVariableSymbol>().Where(symbol => symbol.Name == "A"), "and not a local as well");
    }

    [TestMethod]
    public void AtModuleScope_TwoProceduresThatMentionTheName_ShareOneVariable()
    {
        var symbols = Provide(
            "Sub First()\r\nA = 1\r\nEnd Sub\r\nSub Second()\r\nDebug.Print A\r\nEnd Sub\r\n", ImplicitDeclarationScope.Module);

        var variable = symbols.OfType<VBModuleFieldVariableMemberSymbol>().Single(symbol => symbol.Name == "A");
        Assert.HasCount(2, variable.Definitions, "each procedure's reference is a site of the one declaration");
    }

    [TestMethod]
    public void AtModuleScope_ANameTheModuleDeclares_IsNotDeclaredAgain()
    {
        var symbols = Provide(
            "Public A As Long\r\nSub Foo()\r\nA = 42\r\nEnd Sub\r\n", ImplicitDeclarationScope.Module, new IntrinsicSymbolResolver());

        var variable = symbols.OfType<VBModuleFieldVariableMemberSymbol>().Single(symbol => symbol.Name == "A");
        Assert.AreEqual(VBLongType.TypeInfo, variable.ResolvedType, "the declaration the source wrote");
        Assert.IsFalse(variable.GetProperty(SymbolProperties.ImplicitlyDeclared));
    }

    [TestMethod]
    public void AtModuleScope_ANameAProcedureDeclares_StaysItsLocal()
    {
        var symbols = Provide("Sub Foo()\r\nDim A As Long\r\nA = 42\r\nEnd Sub\r\n", ImplicitDeclarationScope.Module);

        Assert.IsEmpty(symbols.OfType<VBModuleFieldVariableMemberSymbol>());
        Assert.AreEqual(LocalDeclarationKind.Dim, symbols.OfType<VBLocalVariableSymbol>().Single(symbol => symbol.Name == "A").DeclaredBy);
    }

    [TestMethod]
    public void UnderOptionExplicit_NothingIsDeclaredAtEitherScope()
    {
        // MS-VBAL 5.2.1.3: there is no implicit declaration mode at all, so there is nowhere for it to put a variable.
        const string Source = "Option Explicit\r\nSub Foo()\r\nA = 42\r\nEnd Sub\r\n";

        foreach (var scope in Enum.GetValues<ImplicitDeclarationScope>())
        {
            var symbols = Provide(Source, scope);
            Assert.IsEmpty(symbols.OfType<VBModuleFieldVariableMemberSymbol>(), $"{scope}");
            Assert.IsEmpty(symbols.OfType<VBLocalVariableSymbol>(), $"{scope}");
        }
    }

    [TestMethod]
    public void AtModuleScope_AStandardLibraryNameOrAMember_IsStillNotADeclaration()
    {
        // the rule is the same rule: a member name is resolved against its owner, so only the owner is a name.
        var symbols = Provide("Sub Foo()\r\nWidget.Frobnicate\r\nEnd Sub\r\n", ImplicitDeclarationScope.Module);

        CollectionAssert.AreEqual(
            new[] { "Widget" }, symbols.OfType<VBModuleFieldVariableMemberSymbol>().Select(symbol => symbol.Name).ToArray());
    }
}
