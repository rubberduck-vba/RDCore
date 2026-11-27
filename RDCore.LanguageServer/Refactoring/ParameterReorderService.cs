using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Services.Refactoring;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics.Refactoring;

namespace RDCore.LanguageServer.Refactoring;

/// <summary>
/// Reorder Parameters (<see cref="RefactoringKinds.ReorderParameters"/>), of the procedure at a position of a document: what it asks and what it refuses or
/// may change, and the edit it makes with the order the user puts the parameters in.
/// </summary>
internal interface IParameterReorderService
{
    /// <summary>
    /// The parameters of the procedure at a position, as a list the user puts in another order, and why their order cannot be changed, or what an order can
    /// change of what the code does.
    /// </summary>
    /// <param name="documentUri">The document.</param>
    /// <param name="position">The position, in the text of the document: on the procedure's name, or on the declaration of one of its parameters.</param>
    /// <param name="token">A token that cancels the composition of the workspace.</param>
    /// <returns>The choice and the notices: a refusal, or the calls whose arguments an order can evaluate in another order.</returns>
    Task<RefactorPrepareResult> PrepareAsync(Uri documentUri, SourcePosition position, CancellationToken token);

    /// <summary>
    /// The edit that puts the parameters of the procedure at a position in the order the user answers, with the arguments of every call to it; or why it is
    /// not made.
    /// </summary>
    /// <param name="documentUri">The document.</param>
    /// <param name="position">The position, in the text of the document.</param>
    /// <param name="answers">The answer to the list of the parameters; an item that is not answered stays where it is.</param>
    /// <param name="accepted">The codes of the changes the user accepted.</param>
    /// <param name="token">A token that cancels the composition of the workspace.</param>
    /// <returns>The edit, or why it is not made.</returns>
    Task<ParameterReorderAnswer> ApplyAsync(
        Uri documentUri, SourcePosition position, IReadOnlyList<RefactorAnswer> answers, IReadOnlyList<string> accepted, CancellationToken token);

    /// <summary>
    /// The title of the code action that reorders the parameters of the procedure at a position, and why it is refused; <see langword="null"/> where no
    /// procedure has parameters to reorder.
    /// </summary>
    /// <param name="documentUri">The document.</param>
    /// <param name="position">The position, in the text of the document.</param>
    /// <param name="token">A token that cancels the composition of the workspace.</param>
    Task<(string Title, string? Refusal)?> OfferAsync(Uri documentUri, SourcePosition position, CancellationToken token);
}

/// <summary>
/// The edit that reorders the parameters of a procedure, or why it is not made.
/// </summary>
/// <param name="Edit">The edit; <see langword="null"/> when it is not made.</param>
/// <param name="Refusal">Why it is not made, in the language of the user interface; <see langword="null"/> when it is.</param>
internal sealed record class ParameterReorderAnswer(RefactorWorkspaceEdit? Edit, string? Refusal);

internal sealed class ParameterReorderService(IRenameService rename) : IParameterReorderService
{
    /// <summary>
    /// The choice of the order of the parameters: a list, whose items are the parameters by name.
    /// </summary>
    public const string ParametersChoice = "parameters";

    /// <summary>
    /// The code of the change an order makes to a call whose arguments do more than read a value: they are evaluated in another order.
    /// </summary>
    public const string EvaluatedCode = "reorderParameters.evaluated";

    public async Task<RefactorPrepareResult> PrepareAsync(Uri documentUri, SourcePosition position, CancellationToken token)
    {
        var (planned, refusal) = await PlannedAsync(documentUri, position, answers: null, token);
        if (planned is null)
        {
            return new RefactorPrepareResult { Notices = [Refused(refusal!)] };
        }

        var (part, module, target, plan) = planned;
        return new RefactorPrepareResult
        {
            Choices =
            [
                new RefactorChoice
                {
                    Id = ParametersChoice,
                    Kind = RefactorChoiceKind.List,
                    Label = ParameterReorderRefusal.Format("ReorderParameters_Choice", culture: null),
                    Description = ParameterReorderRefusal.Format("ReorderParameters_ChoiceDescription", culture: null),
                    CanReorder = true,
                    Items = [.. target.Parameters.Select((name, index) => new RefactorItem { Id = name, Label = target.Declarations[index] })],
                },
            ],
            Notices = [.. plan.Possible.Select(change => Notice(part, change, RefactorNoticeKind.Change, EvaluatedCode))],
            Range = SourceRangeOf(module.ToTextRange(target.Range)),
        };
    }

    public async Task<ParameterReorderAnswer> ApplyAsync(
        Uri documentUri, SourcePosition position, IReadOnlyList<RefactorAnswer> answers, IReadOnlyList<string> accepted, CancellationToken token)
    {
        var (planned, refusal) = await PlannedAsync(documentUri, position, answers, token);
        if (planned is null)
        {
            return new(null, refusal!.Message);
        }

        var (part, _, _, plan) = planned;
        if (!accepted.Contains(EvaluatedCode) && plan.Changes.FirstOrDefault() is { } declined)
        {
            return new(null, ParameterReorderRefusal.Format("ReorderParameters_NotAccepted", culture: null, declined.ToMessage(RenameWorkspace.Locator(part))));
        }

        return new(new RefactorWorkspaceEdit
        {
            Documents =
            [
                // each edit goes to the document of the part of the module it is in (RD-VBAL §5.1), and the edits of one document are one edit of it.
                .. plan.Edits
                    .Select(edit => (Edit: edit, Module: part.Modules.First(candidate => candidate.Is(edit.Module, edit.Document))))
                    .GroupBy(edited => edited.Module.Document.Id.Uri)
                    .Select(group => new RefactorDocumentEdit
                    {
                        Uri = group.First().Module.Document.Id.Uri.ToUri(),
                        Version = group.First().Module.Document.Version,
                        Edits = [.. group.Select(edited => new RefactorTextEdit
                        {
                            Range = SourceRangeOf(edited.Module.ToTextRange(edited.Edit.Range)),
                            NewText = edited.Edit.NewText,
                        })],
                    }),
            ],
        }, null);
    }

    public async Task<(string Title, string? Refusal)?> OfferAsync(Uri documentUri, SourcePosition position, CancellationToken token)
    {
        if (await rename.ComposeAsync(token) is not { } workspace
            || workspace.DocumentAt(documentUri, new Position(position.Line, position.Character)) is not ({ } part, { } module))
        {
            return null;
        }

        var (target, refusal) = ParameterReorder.Prepare(
            part.Context, module.Module, module.ToModulePosition(new Position(position.Line, position.Character)), module.WrittenIn);
        if (target is null)
        {
            return refusal is { FindsNothing: false } ? (ParameterReorderRefusal.Title(refusal.Name), refusal.ToMessage(RenameWorkspace.Locator(part))) : null;
        }

        var plan = ParameterReorder.Plan(part.Context, target, Identity(target));
        return (ParameterReorderRefusal.Title(target.Name), plan.Refusal?.ToMessage(RenameWorkspace.Locator(part)));
    }

    private sealed record class Planned(RenameWorkspace.ProjectPart Part, RenameWorkspace.DocumentModule Module, ParameterReorderTarget Target, ParameterReorderPlan Plan);

    private sealed record class Refusal(string Code, string Message, SourceLocation? Location);

    // the procedure at a position, and its plan with the order answered: the order it has when there is no answer.
    private async Task<(Planned? Planned, Refusal? Refusal)> PlannedAsync(
        Uri documentUri, SourcePosition position, IReadOnlyList<RefactorAnswer>? answers, CancellationToken token)
    {
        if (await rename.ComposeAsync(token) is not { } workspace
            || workspace.DocumentAt(documentUri, new Position(position.Line, position.Character)) is not ({ } part, { } module))
        {
            return (null, RefusalOf(null, new(ParameterReorderRefusalKind.NoProcedure, string.Empty)));
        }

        var (target, refused) = ParameterReorder.Prepare(
            part.Context, module.Module, module.ToModulePosition(new Position(position.Line, position.Character)), module.WrittenIn);
        if (target is null)
        {
            return (null, RefusalOf(part, refused!));
        }

        if (OrderOf(target, answers) is not { } order)
        {
            return (null, RefusalOf(part, new(ParameterReorderRefusalKind.Order, target.Name)));
        }

        var plan = ParameterReorder.Plan(part.Context, target, order);
        return plan.Refusal is { } refusal ? (null, RefusalOf(part, refusal)) : (new Planned(part, module, target, plan), null);
    }

    // the order answered (RefactorAnswer.Items): the items answered take, in their order, the positions of the items answered; the others stay where they
    // are. Null for an answer that names an item twice, or one that is not a parameter.
    private static IReadOnlyList<int>? OrderOf(ParameterReorderTarget target, IReadOnlyList<RefactorAnswer>? answers)
    {
        var answered = answers?.FirstOrDefault(answer => answer.Id == ParametersChoice)?.Items.Select(item => item.Id).ToList() ?? [];
        var indexes = answered.Select(id => target.Parameters.IndexOf(target.Parameters.FirstOrDefault(name => string.Equals(name, id, StringComparison.OrdinalIgnoreCase)) ?? string.Empty)).ToList();
        if (indexes.Any(index => index < 0) || indexes.Distinct().Count() != indexes.Count)
        {
            return null;
        }

        var order = Identity(target).ToArray();
        var positions = indexes.Order().ToList();
        for (var at = 0; at < positions.Count; at++)
        {
            order[positions[at]] = indexes[at];
        }

        return order;
    }

    private static IReadOnlyList<int> Identity(ParameterReorderTarget target) => [.. Enumerable.Range(0, target.Parameters.Length)];

    private static Refusal RefusalOf(RenameWorkspace.ProjectPart? part, ParameterReorderRefusal refusal) => new(
        Code(refusal.Kind),
        refusal.ToMessage(part is null ? null : RenameWorkspace.Locator(part)),
        refusal.Module is { } module ? LocationOf(part, module, refusal.Document, refusal.Position) : null);

    private static RefactorNotice Refused(Refusal refusal)
        => new() { Code = refusal.Code, Kind = RefactorNoticeKind.Refusal, Message = refusal.Message, Location = refusal.Location };

    private static RefactorNotice Notice(RenameWorkspace.ProjectPart part, ParameterReorderRefusal change, RefactorNoticeKind kind, string code) => new()
    {
        Code = code,
        Kind = kind,
        Message = change.ToMessage(RenameWorkspace.Locator(part)),
        Location = change.Module is { } module ? LocationOf(part, module, change.Document, change.Position) : null,
    };

    // where a position of a module is, in the text of the document that module is: of the part of a module of parts the document names (RD-VBAL §5.1).
    private static SourceLocation? LocationOf(RenameWorkspace.ProjectPart? part, Uri module, Uri? written, SourcePosition position)
        => part?.Modules.FirstOrDefault(candidate => candidate.Is(module, written)) is { } document
            ? new SourceLocation(document.Document.Id.Uri.ToUri(), SourceRangeOf(document.ToTextRange(new SourceRange(position, position))))
            : null;

    private static SourceRange SourceRangeOf(OmniSharp.Extensions.LanguageServer.Protocol.Models.Range range)
        => new(range.Start.Line, range.Start.Character, range.End.Line, range.End.Character);

    private static string Code(ParameterReorderRefusalKind kind) => $"reorderParameters.{char.ToLowerInvariant(kind.ToString()[0])}{kind.ToString()[1..]}";
}
