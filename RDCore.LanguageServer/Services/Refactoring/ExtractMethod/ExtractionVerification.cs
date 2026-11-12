using RDCore.Runtime.LanguageCore.Flow;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;

namespace RDCore.LanguageServer.Services.Refactoring.ExtractMethod;

/// <summary>
/// Why an extraction, written, is not made: the module it would leave is checked first, and what the check finds wrong is a refusal, never an edit.
/// </summary>
internal enum ExtractionVerificationFailureKind
{
    /// <summary>
    /// The edited module does not parse, or does not declare the source procedure or the new one.
    /// </summary>
    DoesNotParse,

    /// <summary>
    /// A name of the source procedure or the new one declares a variable by being named, which no name of the source procedure did before: it binds to nothing
    /// that was declared.
    /// </summary>
    NameDoesNotBind,

    /// <summary>
    /// The edited module has a compile error the module did not have, in the source procedure, the new one, or the declarations its syntax shows.
    /// </summary>
    NewCompileError,

    /// <summary>
    /// The call, analyzed as the selection was, does not take in what the selection took in, give back what it gave back, or leave live what it left live.
    /// </summary>
    DataFlowDiffers,

    /// <summary>
    /// The call does not leave the source procedure for every place the selection left it for: an exit, a loop it ended, a label it went to.
    /// </summary>
    ExitNotReached,
}

/// <summary>
/// Why an extraction is not made, and what it is about.
/// </summary>
/// <param name="Kind">Why.</param>
/// <param name="Detail">
/// What it is about: the syntax error, the name, the compile error's description, the variable, the exit; <see langword="null"/> when there is nothing to name.
/// </param>
internal sealed record class ExtractionVerificationFailure(ExtractionVerificationFailureKind Kind, string? Detail);

/// <summary>
/// Checks the module an extraction would leave, before the extraction is made: it parses, every name binds, it has no compile error it did not have, and the
/// call does what the selection did (the same variables in and out, the same live after it, the same exits).
/// </summary>
/// <remarks>
/// The call is analyzed as a selection to extract would be, in the edited source procedure: what its plan says of the data flow and the exits is what the plan
/// of the selection said, by every variable the source procedure had.
/// </remarks>
internal static class ExtractionVerification
{
    /// <summary>
    /// Verifies an extraction.
    /// </summary>
    /// <param name="plan">The plan the extraction was written from.</param>
    /// <param name="edit">The extraction, written.</param>
    /// <param name="name">The name of the new procedure.</param>
    /// <param name="sourceProcedure">The name of the source procedure.</param>
    /// <param name="sourceKind">Its kind, which tells the procedures of a property apart.</param>
    /// <param name="before">The module as it is.</param>
    /// <param name="analyze">Analyzes the module as the edit leaves it, given its text: bound in the workspace as the module is.</param>
    /// <returns>Why the extraction is not made; <see langword="null"/> when it is verified.</returns>
    public static ExtractionVerificationFailure? Verify(
        ExtractionPlan plan, ExtractionEdit edit, string name, string sourceProcedure, MemberKind sourceKind, ExtractionModuleAnalysis before,
        Func<string, ExtractionModuleAnalysis> analyze)
    {
        var (text, written) = ExtractionText.Apply(before.Text, edit.Edits);
        var after = analyze(text);
        if (!after.Parse.IsSuccess)
        {
            return new(ExtractionVerificationFailureKind.DoesNotParse, after.Parse.SyntaxErrors.FirstOrDefault()?.Verbose);
        }

        if (before.Procedure(sourceProcedure, sourceKind) is not { } original)
        {
            return new(ExtractionVerificationFailureKind.DoesNotParse, sourceProcedure);
        }

        // the new procedure is a Function when it gives a value or an exit code back, and a Sub otherwise.
        var edited = after.Procedure(sourceProcedure, sourceKind);
        var extracted = after.Procedure(name, MemberKind.Function) ?? after.Procedure(name, MemberKind.Procedure);
        if (edited is null || extracted is null)
        {
            return new(ExtractionVerificationFailureKind.DoesNotParse, edited is null ? sourceProcedure : name);
        }

        // a variable declared by being named is one the source procedure had, whichever of the two now names it.
        var implicitBefore = ImplicitNamesOf(original).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (ImplicitNamesOf(edited).Concat(ImplicitNamesOf(extracted)).FirstOrDefault(implicitName => !implicitBefore.Contains(implicitName)) is { } unbound)
        {
            return new(ExtractionVerificationFailureKind.NameDoesNotBind, unbound);
        }

        // an error is new when there are more of its kind than there were: the edit moves where every error is.
        var errorsBefore = before.DeclarationErrors.Concat(original.Model.CompileErrors).CountBy(error => error.ErrorId).ToDictionary();
        var errorsAfter = after.DeclarationErrors.Concat(edited.Model.CompileErrors).Concat(extracted.Model.CompileErrors).ToArray();
        if (errorsAfter.CountBy(error => error.ErrorId).FirstOrDefault(count => count.Value > errorsBefore.GetValueOrDefault(count.Key)) is { Key: var newError, Value: > 0 })
        {
            return new(ExtractionVerificationFailureKind.NewCompileError, errorsAfter.First(error => error.ErrorId == newError).Description);
        }

        // the call, and what the call site does with what it returns, is where the selection was.
        var call = Enumerable.Range(0, edit.Edits.Length).Single(index => edit.Edits[index].Kind is ExtractionTextEditKind.Call);
        var editedText = new ExtractionText(text);
        var requested = new SourceRange(editedText.PositionOf(written[call].Start), editedText.PositionOf(written[call].End));
        if (!ExtractionSelection.TrySnap(edited.Declaration, edited.Instructions, after.Directives, requested, out var callSite, out _, out _))
        {
            return new(ExtractionVerificationFailureKind.DataFlowDiffers, null);
        }

        var replanned = ExtractionAnalysis.Plan(callSite!, edited.Graph, edited.Accesses, edited.Model, after.Scopes);
        return DataFlowOf(plan, replanned, original, PassedByValue(callSite!, edited, extracted, name)) ?? ExitsOf(plan, replanned);
    }

    // the variables the call passes to a parameter of the new procedure that it cannot write, and only there: a ByVal one, or a ByRef one the new procedure
    // gives no value. The data flow takes every variable passed to a procedure as one it may write, whatever the procedure declares and does.
    private static HashSet<SemanticId> PassedByValue(ExtractionSelection callSite, ExtractionProcedureAnalysis edited, ExtractionProcedureAnalysis extracted, string name)
    {
        var written = extracted.Accesses.Definitions.Where(definition => definition.Kind is not VariableDefinitionKind.Entry).Select(definition => definition.Variable).ToHashSet();
        var unwritten = extracted.Declaration.Children.OfType<ParameterDeclarationNode>()
            .Select(parameter => parameter.ParameterKind is ParameterKind.ExplicitByVal or ParameterKind.ImplicitByVal
                || extracted.Scope.Declarations.OfType<VBParameterSymbol>().FirstOrDefault(symbol => symbol.Name.Equals(parameter.Name, StringComparison.OrdinalIgnoreCase)) is { } symbol
                    && !written.Contains(symbol.SemanticId))
            .ToArray();
        var byValue = callSite.Statements.SelectMany(statement => (IEnumerable<SyntaxNode>)[statement, .. statement.Descendants()])
            .Select(node => node switch
            {
                CallStatementNode { Callee: SimpleNameExpressionNode callee } call when Names(callee, name) => call.Arguments,
                IndexExpressionNode { Callee: SimpleNameExpressionNode callee } call when Names(callee, name) => call.Arguments,
                _ => [],
            })
            .SelectMany(arguments => arguments.Select((argument, index) => (argument, index)))
            .Where(passed => passed.index < unwritten.Length && unwritten[passed.index])
            .Select(passed => passed.argument.Identity)
            .ToHashSet();

        return edited.Accesses.Definitions.Where(definition => callSite.Region.Contains(definition.Offset))
            .GroupBy(definition => definition.Variable)
            .Where(definitions => definitions.All(definition => definition.Kind is VariableDefinitionKind.ByRefArgument && definition.Expression is { } argument && byValue.Contains(argument.Identity)))
            .Select(definitions => definitions.Key)
            .ToHashSet();
    }

    private static bool Names(SimpleNameExpressionNode callee, string name) => callee.IdentifierName.Equals(name, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> ImplicitNamesOf(ExtractionProcedureAnalysis procedure)
        => procedure.Scope.Declarations.OfType<VBLocalVariableSymbol>().Where(local => local.DeclaredBy.IsImplicit()).Select(local => local.Name);

    // what comes out of the call is what came out of the selection, and what it took in it still takes in, by the variables the source procedure had: the copy of
    // its result, and the code of an exit, are the call site's own. The object of a With block the selection is inside of is given to the new procedure, which
    // writes what the selection wrote of it.
    private static ExtractionVerificationFailure? DataFlowOf(
        ExtractionPlan plan, ExtractionPlan replanned, ExtractionProcedureAnalysis original, IReadOnlySet<SemanticId> passedByValue)
    {
        var known = original.Accesses.Variables.ToHashSet();
        IEnumerable<SemanticId> withTarget = plan.WithTarget is { } target ? [target.Variable] : [];
        var outputs = plan.Parameters.Where(parameter => parameter.IsOutput).Select(parameter => parameter.Variable).Concat(withTarget).ToHashSet();
        var inputs = plan.Parameters.Where(parameter => parameter.IsInput).Select(parameter => parameter.Variable).Concat(withTarget);
        var outputsAfter = replanned.Parameters
            .Where(parameter => parameter.IsOutput && known.Contains(parameter.Variable) && !passedByValue.Contains(parameter.Variable))
            .Select(parameter => parameter.Variable)
            .ToHashSet();
        var inputsAfter = replanned.Parameters.Where(parameter => parameter.IsInput).Select(parameter => parameter.Variable).ToHashSet();
        var differs = outputs.Except(outputsAfter)
            .Concat(outputsAfter.Except(outputs))
            .Concat(inputs.Where(variable => !inputsAfter.Contains(variable)))
            .Concat(plan.LiveAfter.Where(known.Contains).ToHashSet().SymmetricExcept(replanned.LiveAfter.Where(known.Contains)))
            .Select(variable => (SemanticId?)variable)
            .FirstOrDefault();
        return differs is { } variable ? new(ExtractionVerificationFailureKind.DataFlowDiffers, NameOf(variable, plan, replanned)) : null;
    }

    private static HashSet<SemanticId> SymmetricExcept(this HashSet<SemanticId> set, IEnumerable<SemanticId> other)
    {
        set.SymmetricExceptWith(other);
        return set;
    }

    private static string NameOf(SemanticId variable, params ExtractionPlan[] plans)
        => plans.SelectMany(plan => plan.Parameters).FirstOrDefault(parameter => parameter.Variable == variable)?.Name
           ?? variable.Uri.Fragment[(variable.Uri.Fragment.LastIndexOf('.') + 1)..];

    // the call leaves the source procedure for every place the selection left it for, and for no other: as many exits of each kind, and the same labels.
    private static ExtractionVerificationFailure? ExitsOf(ExtractionPlan plan, ExtractionPlan replanned)
    {
        var expected = plan.Exits.Select(KeyOf).Order(StringComparer.Ordinal).ToArray();
        var actual = replanned.Exits.Select(KeyOf).Order(StringComparer.Ordinal).ToArray();
        if (expected.SequenceEqual(actual, StringComparer.Ordinal))
        {
            return null;
        }

        return new(ExtractionVerificationFailureKind.ExitNotReached, expected.Except(actual, StringComparer.Ordinal).Concat(actual.Except(expected, StringComparer.Ordinal)).FirstOrDefault());
    }

    private static string KeyOf(ExtractionExit exit) => exit.Kind is ExtractionExitKind.Label ? $"{exit.Kind} {exit.Label}" : exit.Kind.ToString();
}
