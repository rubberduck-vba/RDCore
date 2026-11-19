using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.Runtime.LanguageCore.Binding;
using RDCore.SDK.Semantics.Flags;
using RDCore.Runtime.LanguageCore.Static;
using RDCore.Runtime.LanguageCore.Static.Abstract;
using RDCore.Symbols;
using System.Diagnostics.CodeAnalysis;
using RDCore.Runtime.LanguageCore.Services;

namespace RDCore.Tests.Semantics.Static;

/// <summary>
/// A call nested in the argument of a call: each argument is evaluated once, however deep the nesting, so the work of a nested call grows with its depth and
/// not as a power of it.
/// </summary>
[TestClass]
public sealed class NestedCallEvaluationTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly Uri Main = new UriBuilder(Root) { Fragment = "Main" }.Uri;
    private static readonly SourceRange R = SourceRange.Empty;

    // the names an evaluation looks up, and how many times each.
    private sealed class CountingResolver(ISymbolResolver resolver) : ISymbolResolver
    {
        public Dictionary<string, int> Lookups { get; } = new(StringComparer.OrdinalIgnoreCase);

        public SymbolResolutionResult ResolveValue(string name, ScopeKind scope, Uri handle)
        {
            Lookups[name] = Lookups.GetValueOrDefault(name) + 1;            return resolver.ResolveValue(name, scope, handle);
        }

        public SymbolResolutionResult ResolveType(string name, ScopeKind scope, Uri handle) => resolver.ResolveType(name, scope, handle);
        public SymbolResolutionResult ResolveQualifier(string name, ScopeKind scope, Uri handle) => resolver.ResolveQualifier(name, scope, handle);
        public SymbolResolutionResult ResolveProjectType(VBProjectSymbol project, string name) => resolver.ResolveProjectType(project, name);
        public SymbolResolutionResult ResolveConditionalConstant(string name, ScopeKind scope, Uri handle) => resolver.ResolveConditionalConstant(name, scope, handle);
        public SymbolResolutionResult ResolveMember(Symbol owner, string name, Uri handle) => resolver.ResolveMember(owner, name, handle);
    }

    // a node of its own identity: the n-th made for one tree.
    private static SyntaxNodeId Identity(int n) => new(TestUri.TestModuleUri().AbsolutePath, [n]);

    // F(F(...F(x)...)), F written depth times.
    private static ExpressionNode Nested(int depth)
    {
        var next = 0;
        ExpressionNode expression = new SimpleNameExpressionNode(Identity(next++), TestLocations.TestLocation, "x");
        for (var level = 0; level < depth; level++)
        {
            var callee = new SimpleNameExpressionNode(Identity(next++), TestLocations.TestLocation, "F");
            expression = new IndexExpressionNode(Identity(next++), TestLocations.TestLocation, callee, [expression]);
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

    // how many times the static pass of `x = Abs(Abs(...Abs(n)...)) + 1`, Abs written depth times, looks up n, recording the facts and the bindings of the
    // expressions: the pass looks at Abs's argument again for the type of the call.
    private static int RecordedLookupsOfTheInnermostArgument(int depth)
    {
        var expression = string.Concat(Enumerable.Repeat("Abs(", depth)) + "n" + new string(')', depth) + " + 1";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Main.bas"),
            $"Attribute VB_Name = \"Main\"\r\nPublic Sub Run()\r\n    Dim n As Long, x As Long\r\n    x = {expression}\r\nEnd Sub\r\n");
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        var composition = WorkspaceSymbolResolver.ComposeWithScopes(Root, [(Main, ModuleType.StdModule, parse)], new IntrinsicSymbolResolver());
        var symbols = new SyntaxTreeSymbolProvider(Root, Main, ModuleType.StdModule, parse, composition.Resolver, withImplicitDeclarations: true).ProvideSymbols();
        var run = symbols.OfType<VBProcedureMemberSymbol>().Single(member => member.Name == "Run");
        var declaration = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var resolver = new CountingResolver(composition.Resolver);
        var bindings = new ExpressionBindings();

        var model = StatementStaticSemanticsEvaluator.Analyze(run.SemanticId, new StatementBlock([.. declaration.Children]), kind: declaration.MemberKind,
            context: new StaticEvaluationContext(resolver, composition.ScopeTree.ScopeFor(run.Uri), Bindings: bindings));

        Assert.IsTrue(model.CompileErrors.IsEmpty, string.Join("; ", model.CompileErrors.Select(error => error.Description)));
        Assert.IsTrue(model.Expressions.Count > depth, "the facts of the expressions are recorded");
        Assert.IsTrue(bindings.Bound.Count > depth, "the expressions are bound");
        return resolver.Lookups.GetValueOrDefault("n");
    }

    [TestMethod]
    [DataRow(2)]
    [DataRow(10)]
    public void TheInnermostArgumentOfANestedCall_IsLookedUpAsOftenAsTheArgumentOfOneCall_WhereItsFactsAndBindingsAreRecorded(int depth)
    {
        var (one, nested) = (RecordedLookupsOfTheInnermostArgument(1), RecordedLookupsOfTheInnermostArgument(depth));

        Assert.AreEqual(one, nested, $"n is looked up as often in {depth} calls ({nested}) as in one ({one})");
    }
}
