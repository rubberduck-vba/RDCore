using System.Collections.Immutable;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.Precompiler;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Instructions;
using RDCore.SDK.Semantics.Static;
using RDCore.SDK.Semantics.Static.Abstract;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Execution;

/// <summary>
/// Loads a parsed module into a session's <see cref="ProgramImage"/>: lowers each procedure it declares to the instructions
/// the interpreter runs, and keys it by the symbol it is the body of.
/// </summary>
/// <remarks>
/// The module's symbols are defined in the session already, which is what the bodies are keyed by and what a constant
/// expression is reduced against.
/// <para>
/// <strong>MS-VBAL §3.4.2</strong>: an excluded <c>#If</c> branch is logically removed before the rest of the language
/// ever sees it. The parser leaves both branches' statements in the body as siblings and records the directives apart, so
/// the only thing that relates a statement to its branch is its source position — which is what a dead range is, and what
/// lowering is given.
/// </para>
/// <para>
/// <strong>MS-VBAL §5.2.3.3 / §5.4.3.2</strong>: a <c>Const</c> statically evaluates to a value and is substituted at its
/// use sites, so the session gives it no storage; reducing its expression is done here, once, because a constant
/// expression yields the same value however many times it is written.
/// </para>
/// </remarks>
/// <param name="session">The session the module is loaded into.</param>
/// <param name="image">The code of the session.</param>
/// <param name="messages">Builds the verbose half of the errors the expression evaluator reports.</param>
public sealed class ModuleLoader(IRuntimeSession session, ProgramImage image, IVerboseMessageBuilder messages)
{
    /// <summary>
    /// Lowers the procedures of <paramref name="module"/> and loads them, replacing what was loaded for it before.
    /// </summary>
    /// <param name="module">The module's symbol, as the session has it.</param>
    /// <param name="parseResult">The parsed module.</param>
    /// <returns>
    /// The descriptions of the errors lowering found. A module that has any is not loaded at all, and what was loaded
    /// for it before stays: there is no telling which of its procedures the program would have run.
    /// </returns>
    public ImmutableArray<string> Load(Symbol module, ModuleParseResult parseResult)
    {
        if (parseResult.SyntaxTree is not { } syntaxTree)
        {
            return [];
        }

        // composed before anything is lowered, because lowering needs its expression evaluator to decide
        // which #If branches are live. The invoker looks a body up at call time, so the image it holds is this same one.
        var pipeline = RuntimeExecutionPipeline.Create(session, image, messages);

        // the scope is the global one because that is where the project's #Const is bound and resolved from
        // (RuntimeExpressionEvaluator.EvaluatePrecompilerConstant); the module's own are found in the trivia, and shadow them.
        var precompiled = PrecompilerLiveBranchEvaluator.Evaluate(
            session, pipeline.Expressions, new RuntimeEvaluationContext(StaticSymbol.GlobalUri), parseResult.PrecompilerTrivia);
        var deadRanges = precompiled.DeadRanges;

        var members = session.Symbols.MembersOf(module.Uri);
        pipeline.Expressions.FoldConstants(session, ConstantsOf(session, syntaxTree, module, members));

        SizeFixedSizeArrays(members);

        // the structure of the #If blocks, whatever they evaluated to: what says that a name declared in each branch of one is declared once.
        var blocks = ConditionalCompilationBlocks.Of(parseResult.PrecompilerTrivia);
        var options = new InstructionLoweringOptions(deadRanges, IsReleaseBuild: !session.IsDebugBuild(), Language: session.Environment.Language);
        var procedures = new List<KeyValuePair<SemanticId, InstructionList>>();
        var procedureModels = ImmutableArray.CreateBuilder<ProcedureSemanticModel>();
        foreach (var declaration in syntaxTree.Children.OfType<MemberDeclarationNode>())
        {
            if (FindMember(members, declaration) is not { } procedure)
            {
                continue;
            }

            var body = new StatementBlock([.. declaration.Children]);
            var lowering = InstructionListLowering.Lower(body, options, declaration.MemberKind);

            // the static pass is told what the session defines, so what is wrong with an expression is found out along with what is wrong with the
            // structure of the body.
            var scope = session.Symbols.ScopeOf(procedure.Uri);
            var model = StatementStaticSemanticsEvaluator.Analyze(
                procedure.SemanticId, body, new StaticSemanticsOptions(deadRanges, session.Environment.Language, blocks), declaration.MemberKind,
                scope is null ? null : new StaticEvaluationContext(session.Symbols.Resolver, scope));

            procedureModels.Add(model);
            procedures.Add(new(procedure.SemanticId, lowering.InstructionList));
        }

        // a module is valid when what it declares is, as well as every procedure of it.
        var declarationErrors = precompiled.Errors
            .AddRange(DeclarationStaticSemanticsEvaluator.CheckSyntax(syntaxTree, blocks))
            .AddRange(DeclarationStaticSemanticsEvaluator.Evaluate(module, members, session.Symbols.Resolver));
        var moduleModel = new ModuleSemanticModel(module.Uri, declarationErrors, procedureModels.ToImmutable())
        {
            // a language that has no such directive has no fact to state about it.
            OptionExplicit = session.Environment.Language is { HasOptionExplicit: false } ? null : module is VBModuleSymbol { Directives.Explicit: true },
            Declarations = DeclarationUsage.Of(DeclarationUsage.DeclaredBy(members), procedureModels),
        };

        // kept whether or not the module loads: what is wrong with a module is what is asked after.
        image.Semantics.Store(moduleModel);

        // what the error is, and the detail that says which of the module's statements it is about.
        var errors = moduleModel.CompileErrors.Select(error => string.IsNullOrEmpty(error.Verbose) || error.Verbose == error.Description
            ? error.Description
            : $"{error.Description}: {error.Verbose}").ToImmutableArray();

        if (errors.IsEmpty)
        {
            image.Load(module.Uri, procedures);
        }

        return errors;
    }

    // A module-level array is allocated when its symbol is defined, which is before anything could reduce the constant expressions
    // its bounds are: it has no dimensions then. Now it can, and an array that is still without them is given the ones it was
    // declared with. (A variable of a class is allocated with each object, when the code is running.)
    private void SizeFixedSizeArrays(IReadOnlyList<VBTypeMemberSymbol> members)
    {
        if (session.Symbols.Defaults is not { } defaults)
        {
            return;
        }

        foreach (var member in members)
        {
            if (member.ScopeKind is not ScopeKind.Module
                || member is not ITypedSymbol { ResolvedType: VBFixedSizeArrayType type }
                || !member.TryGetProperty(SymbolProperties.ArrayBounds, out _)
                || !session.Symbols.Resolver.TryGetAddress(member, out var address)
                || !session.Symbols.Resolver.TryRead(address, out var handle)
                || type.CreateValue(handle!) is not VBArrayValue { IsInitialized: false })
            {
                continue;
            }

            if (defaults.DefaultValueOf(member) is VBArrayValue { IsInitialized: true } sized)
            {
                handle!.SetValue(session.Symbols.Resolver, SymbolAddressTable.BoxedValue(sized));
            }
        }
    }

    // a name is not enough: the accessors of a property share one, and the declaration says which it is.
    private static VBTypeMemberSymbol? FindMember(IReadOnlyList<VBTypeMemberSymbol> members, MemberDeclarationNode declaration)
        => declaration.Name is { Length: > 0 } name
            ? members.FirstOrDefault(member => string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase) && KindOf(member) == declaration.MemberKind)
            : null;

    private static MemberKind? KindOf(VBTypeMemberSymbol member) => member switch
    {
        VBPropertyGetMemberSymbol => MemberKind.PropertyGet,
        VBPropertyLetMemberSymbol => MemberKind.PropertyLet,
        VBPropertySetMemberSymbol => MemberKind.PropertySet,
        VBFunctionMemberSymbol => MemberKind.Function,
        VBProcedureMemberSymbol => MemberKind.Procedure,
        _ => null,
    };

    // every Const this module declares, module-level and procedure-local, as the session knows them.
    // The AST names them; the session symbols are what carry the expression to reduce and the identity
    // the folded value is keyed by. A constant declared by another module is not here — it is folded on
    // first use instead, and still only once.
    private static IEnumerable<Symbol> ConstantsOf(
        IRuntimeSession session, ModuleNode syntaxTree, Symbol module, IReadOnlyList<VBTypeMemberSymbol> members)
    {
        foreach (var declaration in syntaxTree.Children.OfType<ConstantDeclarationNode>())
        {
            if (session.Symbols.TryResolveValue(declaration.Name, module, out var constant) && constant is not null)
            {
                yield return constant;
            }
        }

        foreach (var member in members)
        {
            var locals = member switch
            {
                VBReturningMemberSymbol returning => returning.Locals,
                VBProcedureMemberSymbol procedure => procedure.Locals,
                _ => [],
            };
            foreach (var local in locals.OfType<VBLocalConstantSymbol>())
            {
                yield return local;
            }
        }
    }
}
