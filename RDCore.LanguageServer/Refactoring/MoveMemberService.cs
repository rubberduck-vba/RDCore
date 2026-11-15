using OmniSharp.Extensions.LanguageServer.Protocol;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Services.Refactoring.MoveMember;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.ProtocolExtensions;
using RDCore.SDK.Workspace;
using System.Globalization;
using System.Resources;

namespace RDCore.LanguageServer.Refactoring;

/// <summary>
/// Move Member Up and Move Member Down, as a client asks them of a range of a document (<see cref="MemberMoves"/>).
/// </summary>
internal interface IMoveMemberService
{
    /// <summary>
    /// The moves of the procedure a range is in: up when there is a procedure before it, down when there is one after it, each refused when it would take the
    /// procedure into a branch of an <c>#If</c> block or out of one. None when the range is in no procedure.
    /// </summary>
    Task<IReadOnlyList<MemberMoveOffer>> OffersAsync(Uri documentUri, SourceRange range, CancellationToken token);

    /// <summary>
    /// The edits that move the procedure a range is in, made and verified on a copy of the module; why it is not moved when it is refused, or when the moved
    /// copy is not the module as the move should leave it.
    /// </summary>
    Task<MemberMoveAnswer> MoveAsync(Uri documentUri, SourceRange range, MemberMoveDirection direction, CancellationToken token);
}

/// <summary>
/// A move of a procedure: which way, its title, and why it is refused, or null.
/// </summary>
internal sealed record class MemberMoveOffer(MemberMoveDirection Direction, string Title, string? Refusal);

/// <summary>
/// The edits that move a procedure, of the version of the document they were made from; null with why the procedure is not moved.
/// </summary>
internal sealed record class MemberMoveAnswer(Uri Document, int? Version, IReadOnlyList<RefactorTextEdit>? Edits, string? Refusal);

/// <summary>
/// Move Member Up and Move Member Down over the workspace as a rename composes it (<see cref="IRenameService.ComposeAsync"/>): the module's text and its parse,
/// moved and parsed again to verify the move.
/// </summary>
internal sealed class MoveMemberService(IRenameService rename, IParsingClientService parsing) : IMoveMemberService
{
    private static readonly ResourceManager Messages = new("RDCore.LanguageServer.Services.Refactoring.MoveMember.MoveMemberMessages", typeof(MoveMemberService).Assembly);

    /// <summary>
    /// The refusal of a move whose document has changed since it was offered.
    /// </summary>
    public static string DocumentChanged() => Format("MoveMember_DocumentChanged");

    public async Task<IReadOnlyList<MemberMoveOffer>> OffersAsync(Uri documentUri, SourceRange range, CancellationToken token)
    {
        if (await ModuleOfAsync(documentUri, token) is not { } module)
        {
            return [];
        }

        return [.. new[] { MemberMoveDirection.Up, MemberMoveDirection.Down }
            .Select(direction => MemberMoves.Plan(module.Parse, module.Source, range, direction))
            .OfType<MemberMovePlan>()
            .Select(plan => new MemberMoveOffer(plan.Direction, Title(plan), plan.Refusal is { } refusal ? RefusalOf(plan, refusal) : null))];
    }

    public async Task<MemberMoveAnswer> MoveAsync(Uri documentUri, SourceRange range, MemberMoveDirection direction, CancellationToken token)
    {
        if (await ModuleOfAsync(documentUri, token) is not { } module
            || MemberMoves.Plan(module.Parse, module.Source, range, direction) is not { } plan)
        {
            return new MemberMoveAnswer(documentUri, null, null, Format("MoveMember_Nothing"));
        }

        var version = module.Document.Version;
        if (plan.Refusal is { } refusal)
        {
            return new MemberMoveAnswer(documentUri, version, null, RefusalOf(plan, refusal));
        }

        var edits = MemberMoves.Edits(module.Source, plan);
        var moved = await parsing.ParseFragmentAsync(module.Document.Id.Uri.ToUri(), MemberMoves.Applied(module.Source, edits), token);
        return MemberMoves.Verify(plan, moved)
            ? new MemberMoveAnswer(documentUri, version, edits, null)
            : new MemberMoveAnswer(documentUri, version, null, Format("MoveMember_VerificationFailed", plan.Units[plan.Moved].Name));
    }

    // the module of a document of the workspace; null for a document of no module, and for a program, which has no procedures to move.
    private async Task<RenameWorkspace.DocumentModule?> ModuleOfAsync(Uri documentUri, CancellationToken token)
        => await rename.ComposeAsync(token) is { } workspace
            && workspace.DocumentOf(documentUri) is (_, { } module)
            && !BasicProgramText.IsProgram(module.Document.Id.Uri.GetFileSystemPath())
            ? module
            : null;

    private static string Title(MemberMovePlan plan)
        => Format(plan.Direction is MemberMoveDirection.Up ? "MoveMember_UpTitle" : "MoveMember_DownTitle", plan.Units[plan.Moved].Name);

    private static string RefusalOf(MemberMovePlan plan, MemberMoveRefusalKind refusal)
        => Format($"MoveMember_{refusal}", plan.Units[plan.Moved].Name, plan.Units[plan.Passed].Name);

    private static string Format(string key, params object?[] arguments)
        => string.Format(CultureInfo.CurrentUICulture, Messages.GetString(key, CultureInfo.CurrentUICulture) ?? key, arguments);
}
