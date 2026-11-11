using RDCore.Runtime.LanguageCore.Flow;
using RDCore.Runtime.LanguageCore.Instructions;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Semantics;
using ExpressionClassification = RDCore.SDK.Semantics.ExpressionClassification;

namespace RDCore.LanguageServer.Services.Refactoring.ExtractMethod;

/// <summary>
/// Plans the extraction of a selection: which of the source procedure's variables the new procedure is given, which it gives back, and which it declares as its
/// own. The plan follows the definitions and uses on the code paths, not where the names are written.
/// </summary>
/// <remarks>
/// <para>
/// A variable the selection reads, on a path where its value comes from outside the selection, is an <em>input</em>; one the selection writes, whose value is read
/// after it on some path (a later iteration of the loop the selection is in included), is an <em>output</em>; one that is both is passed <c>ByRef</c>. So a
/// variable that the loop around the selection reads again is passed <c>ByRef</c>.
/// </para>
/// <para>
/// The variables are the source procedure's own: its locals and parameters, and its result. A field or a global is the same variable in the new procedure, and is
/// not passed.
/// </para>
/// </remarks>
internal static class ExtractionAnalysis
{
    /// <summary>
    /// Plans the extraction of a selection.
    /// </summary>
    /// <param name="selection">The statements to extract.</param>
    /// <param name="graph">The code paths of the source procedure.</param>
    /// <param name="accesses">The definitions and uses of its variables.</param>
    /// <param name="model">What the static pass found out about the source procedure: its own identity, what its result is bound to, and its expressions.</param>
    /// <param name="scopes">The scopes of the workspace, which say what each variable is declared as.</param>
    public static ExtractionPlan Plan(ExtractionSelection selection, ControlFlowGraph graph, DefinitionsAndUses accesses, ProcedureSemanticModel model, ScopeTree scopes)
    {
        var region = selection.Region;
        var procedure = model.Procedure;
        var states = ErrorHandlingStates.Of(graph);
        var live = LiveVariables.Of(graph, accesses, states);
        var reaching = ReachingDefinitions.Of(graph, accesses, states);
        var liveAfter = live.LiveAfter(region);

        var uses = accesses.Uses.Where(use => region.Contains(use.Offset)).ToArray();
        var definitions = accesses.Definitions.Where(definition => region.Contains(definition.Offset)).ToArray();
        var touched = uses.Select(use => (use.Variable, use.Offset)).Concat(definitions.Select(definition => (definition.Variable, definition.Offset)))
            .OrderBy(access => access.Offset)
            .Select(access => access.Variable)
            .Distinct();

        // a value given outside the selection - before it, or on an earlier iteration through it - that a read in it may see.
        bool ReadsFromOutside(SemanticId variable, bool startsInside) => uses.Any(use => use.Variable == variable
            && reaching.ReachingBefore(use.Offset, variable).Any(definition => !region.Contains(definition.Offset) && !(startsInside && definition.Kind is VariableDefinitionKind.Entry)));

        // a Dim does not give its variable its initial value again: a local is created and initialized once, with the invocation (MS-VBAL §2.3, procedure
        // extent). One declared in a selection that can run again in the same activation - a
        // loop around it, a jump back to it - starts with the value it was left with, and that comes from outside the new procedure.
        var runsAgain = RunsAgain(region, states.Edges);

        var parameters = new List<ExtractionParameter>();
        var locals = new List<ExtractionLocal>();
        var movedOut = new List<SemanticId>();
        var refusals = new List<ExtractionRefusal>();
        var releasedEarlier = new List<string>();
        var assignsResult = false;
        var readsResult = false;
        foreach (var variable in touched)
        {
            if (variable == procedure)
            {
                // the result the selection may leave as it found it is read too: what it had before the selection is what the source procedure returns then.
                var assigns = definitions.Any(definition => definition.Variable == variable);
                assignsResult |= assigns;
                readsResult |= ReadsFromOutside(variable, startsInside: false) || assigns && !AlwaysReplaces(region, states, live, accesses, variable, isResult: true);
                continue;
            }

            if (SymbolOf(scopes, variable) is not VBLocalVariableSymbol local)
            {
                // a field, a global: the new procedure names the same variable.
                continue;
            }

            var isUndeclared = local is not VBParameterSymbol && local.DeclaredBy.IsImplicit();
            var declaredInside = local is not VBParameterSymbol && !isUndeclared
                && local.Range.Start >= selection.Range.Start && local.Range.End <= selection.Range.End;

            // a variable declared in the selection starts with its initial value, which moves in with the declaration - unless the selection runs again.
            var output = definitions.Any(definition => definition.Variable == variable) && liveAfter.Contains(variable);

            // an output the selection may leave as it found it - written on some paths through it only - is an input as well: what it had before is what is read
            // after it on the others (`x = 5`, then `If c Then x = 10`).
            var input = ReadsFromOutside(variable, startsInside: declaredInside && !runsAgain)
                        || output && !AlwaysReplaces(region, states, live, accesses, variable, isResult: false);
            var usedOutside = accesses.Uses.Any(use => use.Variable == variable && !region.Contains(use.Offset))
                || accesses.Definitions.Any(definition => definition.Variable == variable && definition.Offset >= 0 && !region.Contains(definition.Offset));

            // a Static's lifetime is the source procedure's: its declaration stays there, wherever it is used. So does the declaration of a variable the new
            // procedure is given, which it declares as its parameter.
            if (declaredInside && (usedOutside || local.IsStatic || input || output || local is ParamArrayParameterSymbol))
            {
                movedOut.Add(variable);
            }

            // an undeclared variable has the type its name's DefType says, or Variant (MS-VBAL §5.2.3.1.5), which the new procedure declares it as.
            var type = local.ResolvedType;
            if (local is ParamArrayParameterSymbol)
            {
                // its elements are the caller's variables, which a copy cannot write.
                refusals.AddRange(definitions.Where(definition => definition.Variable == variable)
                    .Select(definition => new ExtractionRefusal(ExtractionRefusalKind.ParamArrayWritten, definition.Expression?.SourceLocation, local.Name)));
                parameters.Add(new ExtractionParameter(variable, local.Name, null, ExtractionPassing.ByVal, ExtractionPassingReason.ParamArray, input, output, MayBeMissing: true));
            }
            else if (local.IsStatic)
            {
                parameters.Add(new ExtractionParameter(variable, local.Name, type, ExtractionPassing.ByRef, ExtractionPassingReason.Static, input, output));
            }
            else if (input || output)
            {
                var (passing, reason) = PassingOf(type, input, output);
                parameters.Add(new ExtractionParameter(variable, local.Name, type, passing, reason, input, output,
                    MayBeMissing: local is VBParameterSymbol { IsOptional: true, DefaultValue: null, ResolvedType: VBVariantType }, IsUndeclared: isUndeclared));
            }
            else if (!declaredInside || usedOutside)
            {
                // nothing comes in or goes out: the new procedure has a local of its own, which takes the declaration when nothing else uses it.
                locals.Add(new ExtractionLocal(variable, local.Name, type,
                    MovesDeclaration: !usedOutside && local is not VBParameterSymbol && !isUndeclared, IsUndeclared: isUndeclared));
            }

            // a variable that becomes the new procedure's own is released when the new procedure ends, and an object it holds last terminates then.
            var setBySelection = definitions.Any(definition => definition.Variable == variable
                && accesses.Instructions.Items[definition.Offset].Node is AssignmentStatementNode { Kind: AssignmentKind.Set } or ForEachStatementNode);
            if (local is not ParamArrayParameterSymbol && !local.IsStatic && !input && !output && MayHoldAnObject(type, setBySelection))
            {
                releasedEarlier.Add(local.Name);
            }
        }

        // the variable a With block names is given to the new procedure once, as the block's target, which the new procedure's own block names.
        var withTarget = WithTargetOf(selection, accesses.Instructions, model, refusals);
        if (withTarget is { } target)
        {
            parameters.RemoveAll(parameter => parameter.Variable == target.Variable);
        }

        var constants = ConstantsOf(selection, accesses.Instructions, model, scopes, refusals);
        // the exits and jumps are of the code as it is written: one a literal condition never takes is still there, and an edit of the literal takes it.
        var (exits, jumps) = ExtractionControlFlow.Of(selection, graph.AsWritten());
        var (startsWithResumeNext, changes, errors) = ExtractionControlFlow.ErrorHandlingOf(region, states, ReadsErr(region, accesses.Instructions, scopes, model));
        if (releasedEarlier.Count > 0)
        {
            changes.Add(new ExtractionBehaviourChange(ExtractionBehaviourChangeKind.ObjectReleasedEarlier, [], [], IsNote: false) { Variables = [.. releasedEarlier] });
        }

        return new ExtractionPlan(selection, [.. parameters], [.. locals], [.. movedOut], assignsResult, readsResult, withTarget, [.. jumps, .. errors, .. refusals],
            [.. exits], startsWithResumeNext, [.. changes])
        {
            Constants = [.. constants],
            LiveAfter = liveAfter,
            ResultNames = [.. uses.Where(use => use.Variable == procedure).Select(use => use.Expression.SourceLocation.Range)
                .Concat(definitions.Where(definition => definition.Variable == procedure && definition.Expression is SimpleNameExpressionNode)
                    .Select(definition => definition.Expression!.SourceLocation.Range))
                .Distinct()
                .Order()],
        };
    }

    // the local constants the selection reads: those declared outside it are declared again by the new procedure, and one it declares that the code after it reads
    // cannot move with it.
    private static List<SemanticId> ConstantsOf(
        ExtractionSelection selection, InstructionList instructions, ProcedureSemanticModel model, ScopeTree scopes, List<ExtractionRefusal> refusals)
    {
        IEnumerable<(SyntaxNode Node, VBLocalConstantSymbol Constant)> ReadBy(IEnumerable<Instruction> where) => where
            .Select(instruction => instruction.Node).OfType<StatementNode>()
            .SelectMany(OwnExpressionsOf)
            .Select(node => (Node: node, Fact: model.Expressions.TryGetValue(node.Identity, out var fact) ? fact : null))
            .Where(read => read.Fact is { Classification: ExpressionClassification.Constant, Binding: not null })
            .Select(read => (read.Node, Constant: SymbolOf(scopes, read.Fact!.Binding!.Value) as VBLocalConstantSymbol))
            .Where(read => read.Constant is not null)
            .Select(read => (read.Node, read.Constant!));

        var inside = ReadBy(selection.Region.Select(offset => instructions.Items[offset])).ToArray();
        var after = ReadBy(instructions.Items.Where(instruction => !selection.Region.Contains(instruction.Offset))).ToArray();
        bool DeclaredInside(VBLocalConstantSymbol constant) => constant.Range.Start >= selection.Range.Start && constant.Range.End <= selection.Range.End;

        foreach (var (node, constant) in after.Where(read => DeclaredInside(read.Constant)).DistinctBy(read => read.Constant.SemanticId))
        {
            refusals.Add(new ExtractionRefusal(ExtractionRefusalKind.ConstantUsedAfter, node.SourceLocation, constant.Name));
        }

        return [.. inside.Select(read => read.Constant).Where(constant => !DeclaredInside(constant)).Select(constant => constant.SemanticId).Distinct()];
    }

    // whether a variable of the type can hold an object: one declared as an object, an array of them, a user-defined type with an object field; a Variant, an
    // element or a field of one, when the selection Set-assigns the variable or a For Each in it gives the variable an element. The type of a field the variable's own
    // type does not bind is not known: it is an object's when the selection Set-assigns it.
    private static bool MayHoldAnObject(VBType? type, bool setBySelection) => type switch
    {
        null or VBVariantType or VBUnknownType => setBySelection,
        VBObjectType or VBClassType => true,
        VBArrayType array => MayHoldAnObject(array.ItemType, setBySelection),
        VBUserDefinedType record => record.Members.OfType<VBUserDefinedTypeFieldSymbol>().Any(field => MayHoldAnObject(field.ResolvedType, setBySelection)),
        _ => false,
    };

    // whether the selection reads the library's Err or Erl, which the library runs (an external target): a name the workspace declares is its own.
    private static bool ReadsErr(IReadOnlySet<int> region, InstructionList instructions, ScopeTree scopes, ProcedureSemanticModel model)
        => region.Select(offset => instructions.Items[offset].Node).OfType<StatementNode>()
            .SelectMany(OwnExpressionsOf)
            .OfType<SimpleNameExpressionNode>()
            .Any(name => (name.IdentifierName.Equals("Err", StringComparison.OrdinalIgnoreCase) || name.IdentifierName.Equals("Erl", StringComparison.OrdinalIgnoreCase))
                && model.Expressions.TryGetValue(name.Identity, out var fact) && fact.Binding is { } binding
                && SymbolOf(scopes, binding)?.GetProperty(SymbolProperties.ExternalTarget) is not null);

    // the expressions of the statement itself, nested ones included: those of the statements of its blocks are theirs.
    private static IEnumerable<SyntaxNode> OwnExpressionsOf(SyntaxNode node)
        => node.Children.Where(child => child is not StatementNode).SelectMany(child => (IEnumerable<SyntaxNode>)[child, .. OwnExpressionsOf(child)]);

    // whether every path through the selection, from its first instruction to where it leaves it for a place the variable is live at, replaces the whole of it:
    // an instruction that raises has not completed, and replaces nothing on its error edge. The result of a function is no value of an activation an error ends,
    // unless Resume Next goes on past its last instruction.
    private static bool AlwaysReplaces(
        IReadOnlySet<int> region, ErrorHandlingStates states, LiveVariables live, DefinitionsAndUses accesses, SemanticId variable, bool isResult)
    {
        var edges = states.Edges;
        var exit = states.Graph.Exit;
        var entries = ExtractionControlFlow.EntriesOf(region, states.Graph);
        // a selection that starts with an #If block that may compile no branch is passed over whole, and replaces nothing.
        if (entries.Any(entry => !region.Contains(entry) && live.IsLiveBefore(entry, variable)))
        {
            return false;
        }

        var seen = entries.Where(region.Contains).ToHashSet();
        var pending = new Stack<int>(seen);
        while (pending.TryPop(out var node))
        {
            var replaces = accesses.DefinitionsAt(node).Any(definition => definition.Variable == variable && definition.IsComplete);
            foreach (var edge in edges.SuccessorsOf(node).Where(edge => !(replaces && edge.Kind is not ControlFlowEdgeKind.Error)))
            {
                if (region.Contains(edge.To))
                {
                    if (seen.Add(edge.To))
                    {
                        pending.Push(edge.To);
                    }

                    continue;
                }

                var raisedToTheCaller = edge.Kind is ControlFlowEdgeKind.Error && edge.To == exit
                    && !states.At(node).Any(state => state is { Policy: ErrorHandlingPolicy.ResumeNext, IsHandling: false });
                if (live.IsLiveBefore(edge.To, variable) && !(isResult && raisedToTheCaller))
                {
                    return false;
                }
            }
        }

        return true;
    }

    // whether control that leaves the selection can come back to it in the same activation.
    private static bool RunsAgain(IReadOnlySet<int> region, FlowEdges edges)
    {
        var seen = new HashSet<int>();
        var pending = new Stack<int>(region.SelectMany(node => edges.SuccessorsOf(node)).Select(edge => edge.To).Where(to => !region.Contains(to)).Distinct());
        while (pending.TryPop(out var node))
        {
            if (region.Contains(node))
            {
                return true;
            }

            if (!seen.Add(node) || node >= edges.Count - 1)
            {
                continue;
            }

            foreach (var edge in edges.SuccessorsOf(node))
            {
                pending.Push(edge.To);
            }
        }

        return false;
    }

    // arrays and variables of a Type are passed by reference only; a fixed-length string is passed as a String, by reference, so that what is written to it comes back.
    private static (ExtractionPassing Passing, ExtractionPassingReason Reason) PassingOf(VBType? type, bool input, bool output) => type switch
    {
        VBArrayType => (ExtractionPassing.ByRef, ExtractionPassingReason.Array),
        VBUserDefinedType => (ExtractionPassing.ByRef, ExtractionPassingReason.UserDefinedType),
        VBFixedStringType => (ExtractionPassing.ByRef, ExtractionPassingReason.FixedLengthString),
        _ when input && output => (ExtractionPassing.ByRef, ExtractionPassingReason.ReadAndWritten),
        _ when output => (ExtractionPassing.ByRef, ExtractionPassingReason.WrittenOnly),
        _ => (ExtractionPassing.ByVal, ExtractionPassingReason.ReadOnly),
    };

    // the With block the selection is inside of, when an instruction of it uses a member relative to that block: the statements of a selection are siblings of one
    // block, so there is one at most. A With block the selection holds whole is extracted as it is.
    private static ExtractionWithTarget? WithTargetOf(
        ExtractionSelection selection, InstructionList instructions, ProcedureSemanticModel model, List<ExtractionRefusal> refusals)
    {
        var used = selection.Region
            .Select(offset => instructions.Items[offset])
            .Where(instruction => instruction.EnclosingWith is { } with && !selection.Region.Contains(with) && instruction.Node is { } node && UsesAMemberOfTheBlock(node))
            .Select(instruction => instruction.EnclosingWith!.Value)
            .FirstOrDefault(-1);
        if (used < 0 || instructions.Items[used].Node is not WithStatementNode block)
        {
            return null;
        }

        if (block.WithExpression is not SimpleNameExpressionNode name || !model.Expressions.TryGetValue(name.Identity, out var fact) || fact.Binding is not { } variable
            || fact.Classification is not ExpressionClassification.Variable)
        {
            refusals.Add(new ExtractionRefusal(ExtractionRefusalKind.WithTargetIsNotAVariable, block.WithExpression.SourceLocation, null));
            return null;
        }

        var passing = fact.DeclaredType is VBUserDefinedType ? ExtractionPassing.ByRef : ExtractionPassing.ByVal;
        return new ExtractionWithTarget(block, variable, name.IdentifierName, fact.DeclaredType, passing);
    }

    // a member written relative to a With block (`.Name`), among the expressions of the statement itself: those of the statements of its blocks are theirs.
    private static bool UsesAMemberOfTheBlock(SyntaxNode node)
        => node is MemberAccessExpressionNode { Owner: null } || node.Children.Any(child => child is not StatementNode && UsesAMemberOfTheBlock(child));

    // the symbol a binding of the static pass names, when one of the scopes declares it: what kind of variable it is.
    internal static Symbol? SymbolOf(ScopeTree scopes, SemanticId id)
        => scopes.TryGetScope(id.Uri, out var scope)
            ? scope.SelfAndAncestors().SelectMany(outer => outer.Declarations).FirstOrDefault(symbol => symbol.SemanticId == id)
            : null;
}
