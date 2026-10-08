using RDCore.Runtime.Execution;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.StdLib;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// A name resolves as it was written. <c>Hex$</c> is a member of the library that answers a <c>String</c>, and <c>Hex</c> another that answers a <c>Variant</c>:
/// resolving the first to the second would give everything that analyses the call the wrong type.
/// </summary>
[TestClass]
public sealed class TypeHintedNameResolutionTests
{
    private static readonly Uri Workspace = TestUri.WorkspaceRoot();

    private sealed class Provider(params Symbol[] symbols) : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => symbols;
    }

    private static IRuntimeSession Compose(params Symbol[] symbols)
        => RuntimeSessionComposer.Compose(new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), [], [new StdLibSymbolProvider(Workspace), new Provider(symbols)]);

    private static SimpleNameExpressionNode Name(string name, string? hint)
        => new(new RDCore.SDK.Model.AST.Abstract.SyntaxNodeId("test", [1]), TestLocations.TestLocation, name, hint);

    private static Symbol Resolve(IRuntimeSession session, SimpleNameExpressionNode name, Uri scope)
        => session.Symbols.Resolver.ResolveValue(name, ScopeKind.Local, scope).Symbol!;

    [TestMethod]
    public void AStringMemberOfTheLibrary_IsFoundByTheNameItIsWrittenWith()
    {
        var module = new VBStandardModuleSymbol(Workspace, Workspace, "Program");
        var session = Compose(module);

        var hinted = (VBFunctionMemberSymbol)Resolve(session, Name("Hex", "$"), module.Uri);
        var plain = (VBFunctionMemberSymbol)Resolve(session, Name("Hex", null), module.Uri);

        Assert.AreEqual("Hex$", hinted.Name);
        Assert.AreEqual(VBStringType.TypeInfo, hinted.ResolvedType);
        Assert.AreEqual("Hex", plain.Name);
        Assert.AreEqual(VBVariantType.TypeInfo, plain.ResolvedType);
    }

    [TestMethod]
    public void AVariableDeclaredWithAHint_IsTheDeclarationOfTheIdentifier()
    {
        // Dim n%: the symbol is n, and % is how its type was said.
        var module = new VBStandardModuleSymbol(Workspace, Workspace, "Program");
        var n = new VBModuleFieldVariableMemberSymbol(
            Workspace, module.Uri, "n", ScopeKind.Module, VBIntegerType.TypeInfo, SourceRange.Empty, SourceRange.Empty, AccessModifier.Implicit);
        var session = Compose(module, n);

        var resolved = Resolve(session, Name("n", "%"), module.Uri);

        Assert.AreEqual("n", resolved.Name);
    }

    [TestMethod]
    public void AHintedNameNothingDeclares_IsUnbound_NotTheMemberWithoutTheHint()
    {
        var module = new VBStandardModuleSymbol(Workspace, Workspace, "Program");
        var session = Compose(module);

        var result = session.Symbols.Resolver.ResolveValue(Name("Nonesuch", "$"), ScopeKind.Local, module.Uri);

        Assert.IsTrue(result.IsUnbound);
    }
}
