using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Static;
using RDCore.SDK.Semantics.Static.Abstract;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.Tests.Semantics.Static;

/// <summary>
/// A call nested in the argument of a call: each argument is evaluated once, however deep the nesting, so the work of a nested call grows with its depth and
/// not as a power of it.
/// </summary>
[TestClass]
public sealed class NestedCallEvaluationTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SourceRange R = SourceRange.Empty;

    // the names an evaluation looks up, and how many times each.
    private sealed class CountingResolver(ISymbolResolver resolver) : ISymbolResolver
    {
        public Dictionary<string, int> Lookups { get; } = new(StringComparer.OrdinalIgnoreCase);

        public SymbolResolutionResult ResolveValue(string name, ScopeKind scope, Uri handle)
        {
            Lookups[name] = Lookups.GetValueOrDefault(name) + 1;
            return resolver.ResolveValue(name, scope, handle);
        }

        public SymbolResolutionResult ResolveType(string name, ScopeKind scope, Uri handle) => resolver.ResolveType(name, scope, handle);
        public SymbolResolutionResult ResolveQualifier(string name, ScopeKind scope, Uri handle) => resolver.ResolveQualifier(name, scope, handle);
        public SymbolResolutionResult ResolveProjectType(VBProjectSymbol project, string name) => resolver.ResolveProjectType(project, name);
        public SymbolResolutionResult ResolveConditionalConstant(string name, ScopeKind scope, Uri handle) => resolver.ResolveConditionalConstant(name, scope, handle);
        public SymbolResolutionResult ResolveMember(Symbol owner, string name, Uri handle) => resolver.ResolveMember(owner, name, handle);
        public IBindingHandle GetValue(Symbol symbol) => resolver.GetValue(symbol);
        public bool TryRead(MemoryAddress address, [NotNullWhen(true)][MaybeNullWhen(false)] out IBindingHandle? value) => resolver.TryRead(address, out value);
        public bool TryGetAddress(Symbol symbol, out MemoryAddress address) => resolver.TryGetAddress(symbol, out address);
        public bool TryAllocate(Symbol symbol, VBTypedValue value, out MemoryAddress address) => resolver.TryAllocate(symbol, value, out address);
    }

    private static SimpleNameExpressionNode NameOf(string identifier)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [1]), TestLocations.TestLocation, identifier);

    // F(F(...F(x)...)), F written depth times.
    private static ExpressionNode Nested(int depth)
    {
        ExpressionNode expression = NameOf("x");
        for (var level = 0; level < depth; level++)
        {
            expression = new IndexExpressionNode(new(TestUri.TestModuleUri().AbsolutePath, [2]), TestLocations.TestLocation, NameOf("F"), [expression]);
        }

        return expression;
    }

    // how many times the evaluation of the call nested depth deep looks up x: Function F(ByVal n As Long) As Long and a Long x of the module.
    private static int LookupsOfTheInnermostArgument(int depth)
    {
        var module = new VBStandardModuleSymbol(Root, Root, "Caller");
        var function = new VBFunctionMemberSymbol(Root, module.Uri, "F", ScopeKind.Module, SymbolKindExt.Function, VBLongType.TypeInfo, R, R, AccessModifier.Public);
        function = function with { Parameters = [new VBParameterSymbol(Root, function.Uri, "n", R, R, ParameterKind.ExplicitByVal, VBLongType.TypeInfo)] };
        var x = new VBModuleFieldVariableMemberSymbol(Root, module.Uri, "x", ScopeKind.Module, VBLongType.TypeInfo, R, R, AccessModifier.Implicit);
        var tree = ScopeTreeBuilder.Build([module with { Members = [function, x] }, function, x]);
        var resolver = new CountingResolver(new ScopeTreeSymbolResolver(tree));

        var result = ExpressionStaticSemanticsEvaluator.Evaluate(new StaticEvaluationContext(resolver, tree.ScopeFor(module.Uri)), Nested(depth));

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        Assert.AreEqual(VBLongType.TypeInfo, result.Result);
        return resolver.Lookups.GetValueOrDefault("x");
    }

    [TestMethod]
    [DataRow(2)]
    [DataRow(10)]
    public void TheInnermostArgumentOfANestedCall_IsLookedUpAsOftenAsTheArgumentOfOneCall(int depth)
        => Assert.AreEqual(LookupsOfTheInnermostArgument(1), LookupsOfTheInnermostArgument(depth), $"x is looked up as often in {depth} calls as in one");
}
