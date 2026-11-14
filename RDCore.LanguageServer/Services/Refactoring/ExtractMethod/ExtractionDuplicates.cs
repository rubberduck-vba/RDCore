using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using System.Collections.Immutable;
using System.Text;

namespace RDCore.LanguageServer.Services.Refactoring.ExtractMethod;

/// <summary>
/// Code written as the selection is, elsewhere, which a call to the new procedure can replace.
/// </summary>
/// <param name="Module">The address of its module (<c>workspace#Module</c>).</param>
/// <param name="Source">Its module, and the procedure it is in.</param>
/// <param name="Plan">Its plan: what extracting it would take, as it would for a selection of the same statements.</param>
/// <param name="Variables">For each variable of the source procedure that the selection names, the variable of the duplicate's procedure named in its place.</param>
internal sealed record class ExtractionDuplicate(Uri Module, ExtractionSource Source, ExtractionPlan Plan, ImmutableDictionary<SemanticId, SemanticId> Variables)
{
    /// <summary>
    /// What identifies the duplicate among the others: its module, and the line and the column it starts at, from 1 (<c>Main:12:5</c>).
    /// </summary>
    public string Id => $"{SymbolUri.ModuleNameOf(Module)}:{Plan.Selection.Range.Start.Line + 1}:{Plan.Selection.Range.Start.Character + 1}";

    /// <summary>
    /// The edits that replace the duplicate: the call, of kind <see cref="ExtractionTextEditKind.Replacement"/>, and the declarations of its procedure that move
    /// into the new one, as they move from the source procedure.
    /// </summary>
    /// <param name="plan">The plan of the selection.</param>
    /// <param name="choices">What the user chose of the extraction of the selection.</param>
    public ImmutableArray<ExtractionTextEdit> Replacement(ExtractionPlan plan, ExtractionChoices choices)
    {
        // the call passes the duplicate's variables where the selection's call passes the source's, in the same order.
        var ordered = choices.Parameters.IsEmpty
            ? [.. ExtractionParameters.InDeclarationOrder(plan).Select(parameter => new ExtractionParameterChoice(parameter.Variable, parameter.Name, parameter.Passing, []))]
            : choices.Parameters;
        var mapped = new ExtractionChoices(choices.Name)
        {
            ReturnValue = choices.ReturnValue is { } returned ? Variables[returned] : null,
            RouteExits = choices.RouteExits,
            Accessibility = choices.Accessibility,
            Parameters = [.. ordered.Select(choice => choice with { Variable = Variables[choice.Variable], Names = [] })],
        };

        return
        [
            .. ExtractMethodEdit.Of(Plan, Source, mapped).Edits
                .Where(edit => edit.Kind is ExtractionTextEditKind.Declaration or ExtractionTextEditKind.Call)
                .Select(edit => edit.Kind is ExtractionTextEditKind.Call ? edit with { Kind = ExtractionTextEditKind.Replacement } : edit),
        ];
    }
}

/// <summary>
/// Finds the duplicates of a selection: other runs of statements written as it is, but for the variables they name, which a call to the new procedure can
/// replace.
/// </summary>
/// <remarks>
/// <para>
/// A run is a duplicate when its statements are the selection's, node for node: of the same kinds, written with the same keywords, operators and literals, and
/// the same labels between them, whatever their spacing, their line continuations and their comments. A name that the selection binds to a variable, a
/// parameter or a constant of the source procedure is one of the duplicate's procedure, the same one wherever it is named; any other name binds to the same
/// declaration in both.
/// </para>
/// <para>
/// The run is then planned as a selection would be, and the call replaces it only when it does what the run did: the variables it stands for are passed as the
/// selection's are, each declared as the one it stands for, and those the run gives a value that is read after it are passed by reference, or returned; the new
/// procedure's own variables are those of the run; it leaves for the same places, with the same exit codes; and it changes nothing the user would have to accept.
/// A selection that uses the object of a <c>With</c> block, or the source's result, or that declares what the source procedure keeps, has no duplicates.
/// </para>
/// </remarks>
internal static class ExtractionDuplicates
{
    /// <summary>
    /// The duplicates of a selection, in the modules searched, in the order they are written: none overlaps the selection, or another.
    /// </summary>
    /// <param name="plan">The plan of the selection.</param>
    /// <param name="source">Its module, and the procedure it is in.</param>
    /// <param name="choices">What the user chose of the extraction.</param>
    /// <param name="context">The workspace, whose modules are bound.</param>
    /// <param name="module">The address of the selection's module in <paramref name="context"/>.</param>
    /// <param name="searched">The modules searched, the selection's among them when it is.</param>
    /// <param name="analysisOf">The analysis of a module of the workspace, by its address.</param>
    public static ImmutableArray<ExtractionDuplicate> Find(
        ExtractionPlan plan, ExtractionSource source, ExtractionChoices choices, RenameContext context, Uri module, IEnumerable<Uri> searched,
        Func<Uri, ExtractionModuleAnalysis?> analysisOf)
    {
        if (!HasDuplicates(plan) || RunOf(source.Procedure, plan) is not { } run || context.Modules.FirstOrDefault(each => each.Module.AbsoluteUri == module.AbsoluteUri) is not { } selected)
        {
            return [];
        }

        var selection = new Side(new ExtractionText(source.Text), NameOccurrences.BindingsOf(context, selected), source.ProcedureId);
        var names = ExtractMethodEdit.ModuleNamesOf(plan, source, choices.Name).ToArray();
        var found = new List<ExtractionDuplicate>();
        foreach (var uri in searched)
        {
            if (context.Modules.FirstOrDefault(each => each.Module.AbsoluteUri == uri.AbsoluteUri) is not { } syntax || analysisOf(uri) is not { Parse.SyntaxTree: { } tree } analysis)
            {
                continue;
            }

            var bindings = NameOccurrences.BindingsOf(context, syntax);
            var text = new ExtractionText(analysis.Text);
            foreach (var member in tree.Children.OfType<MemberDeclarationNode>())
            {
                if (analysis.Procedure(member.Name, member.MemberKind) is not { } procedure
                    || uri.AbsoluteUri != module.AbsoluteUri && Declares(analysis.Scopes, procedure.Model.Procedure, names))
                {
                    continue;
                }

                var candidate = new Side(text, bindings, procedure.Model.Procedure);
                var memberSource = new ExtractionSource(tree, member, procedure.Model.Procedure, analysis.Text, analysis.Scopes);
                foreach (var (node, _) in NameOccurrences.Walk(member))
                {
                    var children = node.ChildNodes().ToArray();
                    for (var index = 0; index + run.Length <= children.Length; index++)
                    {
                        var nodes = children[index..(index + run.Length)];
                        var range = new SourceRange(nodes[0].SourceLocation.Range.Start, nodes[^1].SourceLocation.Range.End);
                        if (uri.AbsoluteUri == module.AbsoluteUri && Overlaps(range, plan.Selection.Range)
                            || found.Any(duplicate => duplicate.Module.AbsoluteUri == uri.AbsoluteUri && Overlaps(range, duplicate.Plan.Selection.Range))
                            || Match(selection, run, candidate, nodes) is not { } variables
                            || DuplicateOf(plan, source, choices, uri, memberSource, procedure, analysis, range, variables) is not { } duplicate)
                        {
                            continue;
                        }

                        found.Add(duplicate);
                        index += run.Length - 1;
                    }
                }
            }
        }

        return [.. found];
    }

    // a selection whose call the source procedure does more around than call the new procedure has no duplicate: what it does would be done for the duplicate
    // too, by code that is not there.
    private static bool HasDuplicates(ExtractionPlan plan)
        => plan.IsExtractable && plan.WithTarget is null && !plan.AssignsResult && !plan.ReadsResult && plan.MovedOut.IsEmpty;

    // the statements and declarations of the selection, siblings in the tree; none when the selection holds the #If block at either end of them.
    private static SyntaxNode[]? RunOf(MemberDeclarationNode procedure, ExtractionPlan plan)
    {
        var first = plan.Selection.Statements[0];
        if (NameOccurrences.Walk(procedure).FirstOrDefault(each => ReferenceEquals(each.Node, first)).Parent is not { } parent)
        {
            return null;
        }

        var range = plan.Selection.Range;
        var run = parent.ChildNodes().Where(child => child.SourceLocation.Range.Start >= range.Start && child.SourceLocation.Range.End <= range.End).ToArray();
        return run.Length > 0 && run[0].SourceLocation.Range.Start == range.Start && run[^1].SourceLocation.Range.End == range.End ? run : null;
    }

    // whether the procedure, or its module, declares one of the names: a call written there would mean it, not the new procedure.
    private static bool Declares(ScopeTree scopes, SemanticId procedure, IReadOnlyList<string> names)
        => scopes.ScopeFor(procedure.Uri).SelfAndAncestors()
            .TakeWhile(scope => scope.Kind is LexicalScopeKind.Procedure or LexicalScopeKind.Module)
            .Any(scope => names.Any(name => scope.DeclaredAs(name).Any()));

    private static bool Overlaps(SourceRange range, SourceRange other) => range.Start < other.End && other.Start < range.End;

    // a run of statements, its text and what its names bind to, in the procedure it is in.
    private sealed record class Side(ExtractionText Text, Func<ExpressionNode, SemanticId?> Bindings, SemanticId Procedure)
    {
        public bool IsLocal(SemanticId id) => id.Uri.AbsoluteUri.StartsWith(Procedure.Uri.AbsoluteUri + ".", StringComparison.OrdinalIgnoreCase);
    }

    // the variables the candidate names in place of the selection's, when it is written as the selection is; null when it is not.
    private static ImmutableDictionary<SemanticId, SemanticId>? Match(Side selection, SyntaxNode[] run, Side candidate, SyntaxNode[] nodes)
    {
        var variables = new Dictionary<SemanticId, SemanticId>();
        var stoodFor = new Dictionary<SemanticId, SemanticId>();

        // what is written between the statements: the separators, and the labels.
        for (var index = 1; index < run.Length; index++)
        {
            if (Normalized(selection.Text.Of(new SourceRange(run[index - 1].SourceLocation.Range.End, run[index].SourceLocation.Range.Start)))
                != Normalized(candidate.Text.Of(new SourceRange(nodes[index - 1].SourceLocation.Range.End, nodes[index].SourceLocation.Range.Start))))
            {
                return null;
            }
        }

        var pending = new Stack<(SyntaxNode Selected, SyntaxNode Candidate)>(run.Zip(nodes).Reverse());
        while (pending.Count > 0)
        {
            var (selected, other) = pending.Pop();
            if (selected.GetType() != other.GetType())
            {
                return null;
            }

            if (selected is SimpleNameExpressionNode name)
            {
                if (!Binds(selection, name, candidate, (SimpleNameExpressionNode)other, variables, stoodFor))
                {
                    return null;
                }

                continue;
            }

            var selectedChildren = selected.ChildNodes().ToArray();
            var otherChildren = other.ChildNodes().ToArray();
            if (selectedChildren.Length != otherChildren.Length
                || OwnText(selection.Text, selected, selectedChildren) != OwnText(candidate.Text, other, otherChildren))
            {
                return null;
            }

            foreach (var pair in selectedChildren.Zip(otherChildren).Reverse())
            {
                pending.Push(pair);
            }
        }

        return variables.ToImmutableDictionary();
    }

    // a name of the source procedure's own stands for one of the candidate's procedure, the same wherever it is named; any other binds to the same declaration.
    private static bool Binds(
        Side selection, SimpleNameExpressionNode selected, Side candidate, SimpleNameExpressionNode other,
        Dictionary<SemanticId, SemanticId> variables, Dictionary<SemanticId, SemanticId> stoodFor)
    {
        var meant = selection.Bindings(selected);
        var otherMeant = candidate.Bindings(other);
        if (meant is not { } own || !selection.IsLocal(own))
        {
            return meant is null
                ? otherMeant is null && Normalized(selection.Text.Of(selected.SourceLocation.Range)) == Normalized(candidate.Text.Of(other.SourceLocation.Range))
                : meant == otherMeant;
        }

        if (otherMeant is not { } theirs || !candidate.IsLocal(theirs))
        {
            return false;
        }

        if (variables.TryGetValue(own, out var known) || stoodFor.TryGetValue(theirs, out _))
        {
            return known == theirs && stoodFor.GetValueOrDefault(theirs) == own;
        }

        variables[own] = theirs;
        stoodFor[theirs] = own;
        return true;
    }

    // what a node is written as, but for the nodes it is made of: its keywords, its operators, its literal.
    private static string OwnText(ExtractionText text, SyntaxNode node, SyntaxNode[] children)
    {
        var range = node.SourceLocation.Range;
        var own = new StringBuilder();
        var from = range.Start;
        foreach (var child in children.Select(child => child.SourceLocation.Range).Where(child => child.Start >= range.Start && child.End <= range.End).OrderBy(child => child.Start))
        {
            if (child.Start >= from)
            {
                own.Append(text.Of(new SourceRange(from, child.Start))).Append(' ');
                from = child.End;
            }
        }

        return Normalized(own.Append(text.Of(new SourceRange(from, range.End))).ToString());
    }

    // code as it means, whatever it is written with: without its comments and line continuations, its spaces made one, and in one letter case outside its strings.
    internal static string Normalized(string code)
    {
        var normalized = new StringBuilder(code.Length);
        var inString = false;
        for (var index = 0; index < code.Length; index++)
        {
            var character = code[index];
            if (inString)
            {
                normalized.Append(character);
                inString = character != '"';
                continue;
            }

            if (character == '"')
            {
                inString = true;
                normalized.Append(character);
            }
            else if (character == '\'')
            {
                while (index + 1 < code.Length && code[index + 1] is not ('\r' or '\n'))
                {
                    index++;
                }
            }
            else if (char.IsWhiteSpace(character) || IsLineContinuation(code, index))
            {
                normalized.Append(' ');
            }
            else
            {
                normalized.Append(char.ToLowerInvariant(character));
            }
        }

        return string.Join(' ', normalized.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    // an underscore after a space, with nothing but spaces after it on its line.
    private static bool IsLineContinuation(string code, int index)
        => code[index] == '_' && (index == 0 || char.IsWhiteSpace(code[index - 1])) && code.AsSpan(index + 1).TrimStart(' ') is var rest && (rest.IsEmpty || rest[0] is '\r' or '\n');

    // the duplicate, when the call does for the run what it does for the selection: planned as the selection was, and the same in every way the call depends on.
    private static ExtractionDuplicate? DuplicateOf(
        ExtractionPlan plan, ExtractionSource source, ExtractionChoices choices, Uri module, ExtractionSource candidateSource, ExtractionProcedureAnalysis procedure,
        ExtractionModuleAnalysis analysis, SourceRange range, ImmutableDictionary<SemanticId, SemanticId> variables)
    {
        if (!ExtractionSelection.TrySnap(candidateSource.Procedure, procedure.Instructions, analysis.Directives, range, out var selection, out _, out _) || selection!.Range != range)
        {
            return null;
        }

        var candidate = ExtractionAnalysis.Plan(selection, procedure.Graph, procedure.Accesses, procedure.Model, analysis.Scopes);
        if (!HasDuplicates(candidate) || candidate.BehaviourChanges.Any(change => !change.IsNote) || candidate.StartsWithResumeNext != plan.StartsWithResumeNext)
        {
            return null;
        }

        SemanticId? StandsFor(SemanticId variable) => variables.TryGetValue(variable, out var other) ? other : null;
        var parameters = plan.Parameters.ToDictionary(parameter => StandsFor(parameter.Variable) ?? parameter.Variable);
        if (plan.Parameters.Any(parameter => StandsFor(parameter.Variable) is null)
            || !parameters.Keys.ToHashSet().SetEquals(candidate.Parameters.Select(parameter => parameter.Variable))
            || !plan.Locals.Select(local => StandsFor(local.Variable)).ToHashSet().SetEquals(candidate.Locals.Select(local => (SemanticId?)local.Variable))
            || !plan.Constants.Select(StandsFor).ToHashSet().SetEquals(candidate.Constants.Select(constant => (SemanticId?)constant)))
        {
            return null;
        }

        foreach (var other in candidate.Parameters)
        {
            var parameter = parameters[other.Variable];
            var passing = choices.Parameters.FirstOrDefault(choice => choice.Variable == parameter.Variable)?.Passing ?? parameter.Passing;
            var returned = parameter.Variable == choices.ReturnValue;
            if (other.IsOutput && passing is ExtractionPassing.ByVal && !returned
                || returned && !candidate.ReturnCandidates.Any(each => each.Variable == other.Variable)
                || other.IsOptional != parameter.IsOptional
                || other.Reason is ExtractionPassingReason.ParamArray != parameter.Reason is ExtractionPassingReason.ParamArray)
            {
                return null;
            }
        }

        // each variable is declared as the one it stands for, and the exits are the same: the new procedure is written from the selection.
        if (variables.Any(pair => Normalized(ExtractMethodEdit.DeclaredTypeOf(plan, source, pair.Key)) != Normalized(ExtractMethodEdit.DeclaredTypeOf(candidate, candidateSource, pair.Value)))
            || !ExtractMethodEdit.ExitCodesOf(plan, source, choices.Name).ToHashSet().SetEquals(ExtractMethodEdit.ExitCodesOf(candidate, candidateSource, choices.Name)))
        {
            return null;
        }

        return new ExtractionDuplicate(module, candidateSource, candidate, variables);
    }
}
