using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Services.Refactoring;
using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics.Refactoring;

namespace RDCore.LanguageServer.Refactoring;

/// <summary>
/// Move Closer To Usage (<see cref="RefactoringKinds.MoveCloserToUsage"/>), of the variable named at a position of a document: what it asks and what it
/// refuses or changes, and the edit it makes with the user's answer.
/// </summary>
internal interface IMoveCloserToUsageService
{
    /// <summary>
    /// The keyword a variable of a module is declared with in the procedure that uses it, and why its declaration is not moved, or what each keyword changes.
    /// </summary>
    /// <param name="documentUri">The document.</param>
    /// <param name="position">The position, in the text of the document: on the variable's declaration or on a use of it.</param>
    /// <param name="token">A token that cancels the composition of the workspace.</param>
    /// <returns>The choice, for a variable of a module, and the notices: a refusal, or the changes the user is asked to accept.</returns>
    Task<RefactorPrepareResult> PrepareAsync(Uri documentUri, SourcePosition position, CancellationToken token);

    /// <summary>
    /// The edit that moves the declaration of the variable named at a position, with the keyword answered; or why it is not made.
    /// </summary>
    /// <param name="documentUri">The document.</param>
    /// <param name="position">The position, in the text of the document.</param>
    /// <param name="answers">The answer to the keyword; <c>Static</c> when it is not answered.</param>
    /// <param name="accepted">The codes of the changes the user accepted.</param>
    /// <param name="token">A token that cancels the composition of the workspace.</param>
    /// <returns>The edit, or why it is not made.</returns>
    Task<MoveCloserToUsageAnswer> ApplyAsync(
        Uri documentUri, SourcePosition position, IReadOnlyList<RefactorAnswer> answers, IReadOnlyList<string> accepted, CancellationToken token);

    /// <summary>
    /// The title of the code action that moves the declaration of the variable named at a position, why it is refused, and whether it changes what the code
    /// does with the default keyword; <see langword="null"/> where no variable is named.
    /// </summary>
    /// <param name="documentUri">The document.</param>
    /// <param name="position">The position, in the text of the document.</param>
    /// <param name="token">A token that cancels the composition of the workspace.</param>
    Task<(string Title, string? Refusal, bool Changes)?> OfferAsync(Uri documentUri, SourcePosition position, CancellationToken token);
}

/// <summary>
/// The edit that moves a declaration, or why it is not made.
/// </summary>
/// <param name="Edit">The edit; <see langword="null"/> when it is not made.</param>
/// <param name="Refusal">Why it is not made, in the language of the user interface; <see langword="null"/> when it is.</param>
internal sealed record class MoveCloserToUsageAnswer(RefactorWorkspaceEdit? Edit, string? Refusal);

internal sealed class MoveCloserToUsageService(IRenameService rename) : IMoveCloserToUsageService
{
    /// <summary>
    /// The choice of the keyword a variable of a module is declared with in the procedure: <see cref="StaticKeyword"/> or <see cref="DimKeyword"/>.
    /// </summary>
    public const string KeywordChoice = "keyword";

    /// <summary>
    /// <c>Static</c>: the default, which keeps the value of a variable of a standard module between calls.
    /// </summary>
    public const string StaticKeyword = "static";

    /// <summary>
    /// <c>Dim</c>.
    /// </summary>
    public const string DimKeyword = "dim";

    public async Task<RefactorPrepareResult> PrepareAsync(Uri documentUri, SourcePosition position, CancellationToken token)
    {
        var (planned, refusal) = await PlannedAsync(documentUri, position, token);
        if (planned is null)
        {
            return new RefactorPrepareResult { Notices = [Refused(refusal!)] };
        }

        var target = planned.Target;
        if (!target.IsOfTheModule)
        {
            return new RefactorPrepareResult();
        }

        return new RefactorPrepareResult
        {
            Choices =
            [
                new RefactorChoice
                {
                    Id = KeywordChoice,
                    Kind = RefactorChoiceKind.Option,
                    Label = CloserToUsageNotice.Format("MoveCloserToUsage_Keyword", null),
                    Description = CloserToUsageNotice.Format("MoveCloserToUsage_KeywordDescription", null),
                    Default = StaticKeyword,
                    Options =
                    [
                        new RefactorOption { Value = StaticKeyword, Label = CloserToUsageNotice.Format("MoveCloserToUsage_Static", null) },
                        new RefactorOption { Value = DimKeyword, Label = CloserToUsageNotice.Format("MoveCloserToUsage_Dim", null) },
                    ],
                },
            ],
            Notices =
            [
                .. new[] { CloserToUsageKeyword.Static, CloserToUsageKeyword.Dim }
                    .Select(keyword => CloserToUsage.ChangeOf(target, keyword))
                    .OfType<CloserToUsageNotice>()
                    .Select(change => new RefactorNotice { Code = Code(change.Change!.Value), Kind = RefactorNoticeKind.Change, Message = change.ToMessage() }),
            ],
        };
    }

    public async Task<MoveCloserToUsageAnswer> ApplyAsync(
        Uri documentUri, SourcePosition position, IReadOnlyList<RefactorAnswer> answers, IReadOnlyList<string> accepted, CancellationToken token)
    {
        var answered = answers.FirstOrDefault(answer => answer.Id == KeywordChoice)?.Value;
        if (KeywordOf(answered) is not { } keyword)
        {
            return new(null, CloserToUsageNotice.Format("MoveCloserToUsage_NotAnAnswer", null, answered ?? string.Empty, KeywordChoice));
        }

        var (planned, refusal) = await PlannedAsync(documentUri, position, token);
        if (planned is null)
        {
            return new(null, refusal!.ToMessage());
        }

        if (CloserToUsage.ChangeOf(planned.Target, keyword) is { } change && !accepted.Contains(Code(change.Change!.Value)))
        {
            return new(null, CloserToUsageNotice.Format("MoveCloserToUsage_NotAccepted", null, change.ToMessage()));
        }

        var (edits, refused) = CloserToUsage.Plan(planned.Target, keyword);
        if (edits is null)
        {
            return new(null, refused!.ToMessage());
        }

        // the edits of each module go to the document of the module.
        var documents = new List<RefactorDocumentEdit>();
        foreach (var each in edits)
        {
            if (planned.Part.Modules.FirstOrDefault(candidate => candidate.Is(each.Module, each.Document)) is not { } module)
            {
                return new(null, new CloserToUsageNotice(CloserToUsageRefusalKind.NotAVariable, string.Empty).ToMessage());
            }

            documents.Add(new RefactorDocumentEdit
            {
                Uri = module.Document.Id.Uri.ToUri(),
                Version = module.Document.Version,
                Edits = [.. each.Edits.Select(edit => new RefactorTextEdit { Range = SourceRangeOf(module.ToTextRange(edit.Range)), NewText = edit.NewText })],
            });
        }

        return new(new RefactorWorkspaceEdit { Documents = documents }, null);
    }

    public async Task<(string Title, string? Refusal, bool Changes)?> OfferAsync(Uri documentUri, SourcePosition position, CancellationToken token)
    {
        var (planned, refusal) = await PlannedAsync(documentUri, position, token);
        if (planned is null)
        {
            return refusal is { FindsNothing: false } ? (CloserToUsageNotice.Title(refusal.Name), refusal.ToMessage(), false) : null;
        }

        var (_, refused) = CloserToUsage.Plan(planned.Target, CloserToUsageKeyword.Static);
        return (CloserToUsageNotice.Title(planned.Target.Variable.Name), refused?.ToMessage(), CloserToUsage.ChangeOf(planned.Target, CloserToUsageKeyword.Static) is not null);
    }

    private sealed record class Planned(RenameWorkspace.ProjectPart Part, CloserToUsageTarget Target);

    private async Task<(Planned? Planned, CloserToUsageNotice? Refusal)> PlannedAsync(Uri documentUri, SourcePosition position, CancellationToken token)
    {
        if (await rename.ComposeAsync(token) is not { } workspace
            || workspace.DocumentAt(documentUri, new Position(position.Line, position.Character)) is not ({ } part, { } module)
            || part.Context.Modules.FirstOrDefault(candidate => candidate.Is(module.Module, module.WrittenIn)) is not { } source)
        {
            return (null, new(CloserToUsageRefusalKind.NotAVariable, string.Empty));
        }

        var (target, refusal) = CloserToUsage.Prepare(
            part.Context, source, module.Parse, module.ToModulePosition(new Position(position.Line, position.Character)),
            other => part.Modules.FirstOrDefault(candidate => candidate.Key == other.Key)?.Parse);
        return target is null ? (null, refusal) : (new Planned(part, target), null);
    }

    private static CloserToUsageKeyword? KeywordOf(string? answer) => answer switch
    {
        null or "" => CloserToUsageKeyword.Static,
        _ when string.Equals(answer, StaticKeyword, StringComparison.OrdinalIgnoreCase) => CloserToUsageKeyword.Static,
        _ when string.Equals(answer, DimKeyword, StringComparison.OrdinalIgnoreCase) => CloserToUsageKeyword.Dim,
        _ => null,
    };

    private static RefactorNotice Refused(CloserToUsageNotice refusal)
        => new() { Code = $"moveCloserToUsage.{Camel(refusal.Kind!.Value.ToString())}", Kind = RefactorNoticeKind.Refusal, Message = refusal.ToMessage() };

    // the code of a change the user accepts.
    public static string Code(CloserToUsageChangeKind change) => $"moveCloserToUsage.{Camel(change.ToString())}";

    private static string Camel(string name) => $"{char.ToLowerInvariant(name[0])}{name[1..]}";

    private static SourceRange SourceRangeOf(OmniSharp.Extensions.LanguageServer.Protocol.Models.Range range)
        => new(range.Start.Line, range.Start.Character, range.End.Line, range.End.Character);
}
