using RDCore.Runtime.LanguageCore.Flow;
using RDCore.Runtime.LanguageCore.Instructions;
using RDCore.Runtime.LanguageCore.Static;
using RDCore.Runtime.LanguageCore.Static.Abstract;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Completion;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics;
using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace RDCore.LanguageServer.Services.Refactoring.ExtractMethod;

/// <summary>
/// A module as an extraction plans and verifies it: parsed, its names bound among the workspace's, and each of its procedures analyzed when it is asked for.
/// </summary>
/// <remarks>
/// What is wrong with the module is what the language server finds out on its own: its syntax errors, the errors of its declarations that its syntax
/// shows, and the compile errors of each procedure. The diagnostics of the extensions are theirs to give.
/// </remarks>
internal sealed class ExtractionModuleAnalysis
{
    private readonly ISymbolResolver _resolver;
    private readonly ConcurrentDictionary<string, ExtractionProcedureAnalysis?> _procedures = new(StringComparer.OrdinalIgnoreCase);

    private ExtractionModuleAnalysis(Uri module, string text, ModuleParseResult parse, ISymbolResolver resolver, ScopeTree scopes)
    {
        Module = module;
        Text = text;
        Parse = parse;
        _resolver = resolver;
        Scopes = scopes;
        Directives = ExtractionDirectives.Of(parse.PrecompilerTrivia, text);
        _blocks = ConditionalCompilationBlocks.Of(parse.PrecompilerTrivia);
        DeclarationErrors = parse.SyntaxTree is { } tree
            ? [.. DeclarationStaticSemanticsEvaluator.CheckSyntax(tree, _blocks), .. ReservedNameStaticSemantics.CheckSyntax(tree)]
            : [];
    }

    // the module's #If blocks: a body is lowered with every branch, which are alternatives on its code paths.
    private readonly ConditionalCompilationBlocks _blocks;

    /// <summary>
    /// The module, as its scope is addressed.
    /// </summary>
    public Uri Module { get; }

    /// <summary>
    /// Its text.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Its parse.
    /// </summary>
    public ModuleParseResult Parse { get; }

    /// <summary>
    /// The scopes of the workspace it is bound in.
    /// </summary>
    public ScopeTree Scopes { get; }

    /// <summary>
    /// Its <c>#If</c> blocks.
    /// </summary>
    public ExtractionDirectives Directives { get; }

    /// <summary>
    /// What its syntax shows is wrong with its declarations: those checked without evaluating anything.
    /// </summary>
    public ImmutableArray<VBCompileErrorInfo> DeclarationErrors { get; }

    /// <summary>
    /// A module whose names are bound in a composition of the workspace.
    /// </summary>
    /// <param name="module">The module, as its scope is addressed in <paramref name="scopes"/>.</param>
    /// <param name="text">Its text.</param>
    /// <param name="parse">Its parse.</param>
    /// <param name="resolver">The resolver of the composition.</param>
    /// <param name="scopes">The scopes of the composition.</param>
    public static ExtractionModuleAnalysis Of(Uri module, string text, ModuleParseResult parse, ISymbolResolver resolver, ScopeTree scopes)
        => new(module, text, parse, resolver, scopes);

    /// <summary>
    /// The procedure of the module that is named <paramref name="name"/>, of the kind <paramref name="kind"/>, analyzed; <see langword="null"/> when the module
    /// declares none, or its scope is not known.
    /// </summary>
    /// <param name="name">Its name.</param>
    /// <param name="kind">Its kind: the <c>Property Get</c>, <c>Let</c> and <c>Set</c> of a property share its name.</param>
    public ExtractionProcedureAnalysis? Procedure(string name, MemberKind kind) => _procedures.GetOrAdd($"{kind} {name}", _ => Analyze(name, kind));

    private ExtractionProcedureAnalysis? Analyze(string name, MemberKind kind)
    {
        // the procedure's scope is the one its declaration is in: a property's procedures are separate symbols with one name.
        if (Parse.SyntaxTree?.Children.OfType<MemberDeclarationNode>()
                .FirstOrDefault(member => member.MemberKind == kind && member.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is not { } declaration
            || !Scopes.TryGetScope(CompletionCandidates.OriginAt(Scopes, Module, declaration.SourceLocation.Range.Start), out var scope)
            || scope.Kind is not LexicalScopeKind.Procedure)
        {
            return null;
        }

        var block = new StatementBlock([.. declaration.Children]);
        var model = StatementStaticSemanticsEvaluator.Analyze(new SemanticId(scope.Uri), block, kind: declaration.MemberKind,
            context: new StaticEvaluationContext(_resolver, scope));
        var instructions = InstructionListLowering.Lower(block, default, declaration.MemberKind).InstructionList;
        var graph = ControlFlowGraph.Of(instructions, model, blocks: _blocks);
        return new ExtractionProcedureAnalysis(declaration, scope, model, instructions, graph, DefinitionsAndUses.Of(instructions, model, Scopes));
    }
}

/// <summary>
/// A procedure as an extraction plans and verifies it.
/// </summary>
/// <param name="Declaration">Its declaration.</param>
/// <param name="Scope">Its scope: its parameters and locals.</param>
/// <param name="Model">What the static pass found out about it, its compile errors included.</param>
/// <param name="Instructions">Its lowered body.</param>
/// <param name="Graph">Its code paths.</param>
/// <param name="Accesses">The definitions and uses of its variables.</param>
internal sealed record class ExtractionProcedureAnalysis(
    MemberDeclarationNode Declaration, LexicalScope Scope, ProcedureSemanticModel Model, InstructionList Instructions, ControlFlowGraph Graph, DefinitionsAndUses Accesses);
