using System.Collections.Immutable;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.Precompiler;
using RDCore.Runtime.Semantics.Statements;
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
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Semantics.Flow;
using RDCore.SDK.Semantics.Instructions;
using RDCore.SDK.Semantics.Static;
using RDCore.SDK.Semantics.Static.Abstract;
using RDCore.SDK.Services.VerboseMessages;
using RDCore.SDK.Workspace.Forms;

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

        // a form is created from what its designer declares when the program first uses it, whether or not its code loads: Form1.Show shows a form with no code.
        image.DefineForm(module.Name, parseResult.Designer is { Kind: FormDesignerKind.MSForms } designer ? designer : null);

        // composed before anything is lowered, because lowering needs its expression evaluator to decide
        // which #If branches are live. The invoker looks a body up at call time, so the image it holds is this same one.
        var pipeline = RuntimeExecutionPipeline.Create(session, image, messages);

        // the scope is the global one because that is where the project's #Const is bound and resolved from
        // (RuntimeExpressionEvaluator.EvaluatePrecompilerConstant); the module's own are found in the trivia, and shadow them.
        var precompiled = PrecompilerLiveBranchEvaluator.Evaluate(
            session, pipeline.Expressions, new RuntimeEvaluationContext(StaticSymbol.GlobalUri), parseResult.PrecompilerTrivia);
        var deadRanges = precompiled.DeadRanges;

        var members = session.Symbols.MembersOf(module.Uri);
        // what a constant's value raises when it is evaluated statically is a compile error of the module (MS-VBA: Overflow, Division by zero).
        var constantErrors = pipeline.Expressions.FoldConstants(session, ConstantsOf(session, syntaxTree, module, members));

        SizeFixedSizeArrays(members);

        // the sizes the declarations are written with - a String * n's length, a bound that names a constant - are reduced now that the constants are.
        var sizeErrors = DeclaredSizeStaticSemantics.Check(session, new ArrayBoundEvaluator(pipeline.Expressions, pipeline.LetCoercion), module,
            syntaxTree.Children.Select(node => (node, node is MemberDeclarationNode declaration && FindMember(members, declaration) is { } member ? member.Uri : module.Uri)),
            members);

        // the structure of the #If blocks, whatever they evaluated to: what says that a name declared in each branch of one is declared once.
        var blocks = ConditionalCompilationBlocks.Of(parseResult.PrecompilerTrivia);
        var options = new InstructionLoweringOptions(deadRanges, IsReleaseBuild: !session.IsDebugBuild(), Language: session.Environment.Language);
        var procedures = new List<KeyValuePair<SemanticId, InstructionList>>();
        var procedureModels = ImmutableArray.CreateBuilder<ProcedureSemanticModel>();
        var sweepable = new List<(VBTypeMemberSymbol Procedure, InstructionList Body)>();
        var swept = new HashSet<SemanticId>();
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
                scope is null ? null : new StaticEvaluationContext(session.Symbols.Resolver, scope,
                    Environment: new StaticEnvironment(session.Environment.Is64Bit) { Libraries = session.Libraries }));

            // the code paths are those of the lowered body, which only the loader has next to what the static pass found out about it.
            procedureModels.Add(model with { ReturnValue = ReturnValueAnalysis.Of(declaration, model, lowering.InstructionList) });
            procedures.Add(new(procedure.SemanticId, lowering.InstructionList));
            // a declaration that repeats another resolves to the symbol of the first, whose code is the one the session has: the repeat is an error of
            // the declarations, and its code is not evaluated for it.
            if (model.IsValid && swept.Add(procedure.SemanticId))
            {
                sweepable.Add((procedure, lowering.InstructionList));
            }
        }

        // a module is valid when what it declares is, as well as every procedure of it. The types it declares are asked to be names that resolve: its code is
        // only sent once everything the workspace and its references declare is defined, which is what a name that is declared after the one that uses it needs.
        var declarationErrors = precompiled.Errors
            .AddRange(constantErrors)
            .AddRange(sizeErrors)
            .AddRange(DeclarationStaticSemanticsEvaluator.CheckSyntax(syntaxTree, blocks))
            .AddRange(DeclarationStaticSemanticsEvaluator.CheckDesigner(syntaxTree, parseResult.Designer))
            .AddRange(ReservedNameStaticSemantics.CheckSyntax(syntaxTree))
            .AddRange(DeclarationStaticSemanticsEvaluator.Evaluate(module, members, session.Symbols.Resolver, DeclarationRules.DeclaredTypes));
        var moduleModel = new ModuleSemanticModel(module.Uri, declarationErrors, procedureModels.ToImmutable())
        {
            // a language that has no such directive has no fact to state about it.
            OptionExplicit = session.Environment.Language is { HasOptionExplicit: false } ? null : module is VBModuleSymbol { Directives.Explicit: true },
            Declarations = DeclarationUsage.Of(
                DeclarationUsage.DeclaredBy(members), procedureModels, module is VBClassModuleSymbol classModule ? MemberRoles.Of(classModule, session.Symbols.Resolver) : null),
            Language = session.Environment.Language?.Id,
            Class = module is VBClassModuleSymbol declaredClass ? ClassFlagsOf(declaredClass, members) : null,
        };

        // kept whether or not the module loads: what is wrong with a module is what is asked after. What the language core states about the code is found out by
        // evaluating it, which only a procedure with nothing wrong with it can be, and takes longer than anything above: the model is stored now, and the
        // facts are added to it when they are ready. The evaluation is given what it needs of the session as it is at this moment, because it is not done here.
        var sweep = sweepable.Count == 0 ? null : ModuleSweep.Prepare(session, sweepable, messages);
        image.Semantics.Store(moduleModel, sweep is null ? null : sweep.Evaluate);

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
                || member is not ITypedSymbol { ResolvedType: var type }
                || !session.Symbols.Resolver.TryGetAddress(member, out var address)
                || !session.Symbols.Resolver.TryRead(address, out var handle))
            {
                continue;
            }

            switch (type)
            {
                case VBFixedSizeArrayType when member.TryGetProperty(SymbolProperties.ArrayBounds, out _)
                    && type.CreateValue(handle!) is VBArrayValue { IsInitialized: false }
                    && defaults.DefaultValueOf(member) is VBArrayValue { IsInitialized: true } sized:
                    handle!.SetValue(session.Symbols.Resolver, SymbolAddressTable.BoxedValue(sized));
                    break;

                // a record is given again the fields it starts with, when one of them is a fixed-size array: nothing has run that assigned it.
                case VBUserDefinedType record when HasFixedSizeArrayField(session.Symbols.Resolver, record, [])
                    && defaults.DefaultValueOf(member) is VBUserDefinedTypeValue sized:
                    handle!.SetValue(session.Symbols.Resolver, SymbolAddressTable.BoxedValue(sized));
                    break;
            }
        }
    }

    // MS-VBAL §5.2.4.1.2, §5.2.3.1: whether the class has a default instance, and whether its objects - the default instance among them - hold state: a variable
    // the class declares at module level, of which each object has a distinct one, or a Static local of one of its members, which MS-VBA gives each object
    // its own of too (Excel-verified, oracle\s19 N37), where MS-VBAL gives a static local module extent.
    private static ClassModuleSemanticFlags ClassFlagsOf(VBClassModuleSymbol classModule, IReadOnlyList<VBTypeMemberSymbol> members)
        => (classModule.GetProperty(SymbolProperties.PredeclaredId) ? ClassModuleSemanticFlags.DefaultInstance : ClassModuleSemanticFlags.None)
            | (members.Any(member => member is VBModuleFieldVariableMemberSymbol
                || RuntimeProcedureInvoker.GetLocals(member).Any(local => local is VBLocalVariableSymbol { IsStatic: true }))
                ? ClassModuleSemanticFlags.Stateful
                : ClassModuleSemanticFlags.None);

    // directly, or in a record that is a field of it. A type that holds itself is not one MS-VBA compiles, and is looked into once. The fields are the type's
    // as the session has it now (CurrentTypes).
    private static bool HasFixedSizeArrayField(ISymbolResolver resolver, VBUserDefinedType declared, HashSet<VBUserDefinedType> seen)
        => CurrentTypes.Of(resolver, declared) is var record && seen.Add(record) && record.Fields().Any(field => field.ResolvedType switch
        {
            VBFixedSizeArrayType => true,
            VBUserDefinedType nested => HasFixedSizeArrayField(resolver, nested, seen),
            _ => false,
        });

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
