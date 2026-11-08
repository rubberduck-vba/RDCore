using RDCore.SDK.Model.Source;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// Asks the language server what a refactoring of a document's range would ask its user, and what it would refuse or change, before it is applied
/// (<c>rdcore/refactor/prepare</c>).
/// </summary>
/// <remarks>
/// <para>
/// Every refactoring is offered to any client as an LSP code action, which completes with its defaults and no dialog. A client that has a dialog of its own
/// expects the <c>Refactoring</c> capability, and asks for the choices first: it shows them, and applies the refactoring with its user's answers
/// (<see cref="RefactorApplyParams"/>). One pair of requests serves every refactoring; what a refactoring asks is described as data
/// (<see cref="RefactorChoice"/>).
/// </para>
/// </remarks>
public sealed record class RefactorPrepareParams
{
    /// <summary>
    /// Which refactoring: the kind of its code action (<strong>LSP 3.17</strong> §Code Action Request), as the language server offers it.
    /// </summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>
    /// The address of the document.
    /// </summary>
    public Uri? Uri { get; init; }

    /// <summary>
    /// The range of the document the refactoring is of: the selection, or the position of a name.
    /// </summary>
    public SourceRange Range { get; init; }
}

/// <summary>
/// What a refactoring asks its user, with the answer it takes by default to each question, and what it refuses or would change.
/// </summary>
public sealed record class RefactorPrepareResult
{
    /// <summary>
    /// What the refactoring asks, in the order a dialog shows it. Each has a default, which the refactoring takes when a choice is not answered.
    /// </summary>
    public IReadOnlyList<RefactorChoice> Choices { get; init; } = [];

    /// <summary>
    /// Why the refactoring is refused, and what it would change of what the code does, which the user accepts (<see cref="RefactorNoticeKind"/>).
    /// </summary>
    public IReadOnlyList<RefactorNotice> Notices { get; init; } = [];
}

/// <summary>
/// What a choice of a refactoring is made of.
/// </summary>
public enum RefactorChoiceKind
{
    /// <summary>
    /// A name the user types: of a procedure that is extracted, a parameter, a property. Its default is a name the code can declare there; a name it
    /// cannot is refused when the refactoring is applied.
    /// </summary>
    Name,

    /// <summary>
    /// One of its options (<see cref="RefactorChoice.Options"/>), by its value.
    /// </summary>
    Option,

    /// <summary>
    /// Yes or no: <c>true</c> or <c>false</c>.
    /// </summary>
    Flag,

    /// <summary>
    /// A list of items, each with choices of its own (<see cref="RefactorChoice.Items"/>): the parameters of a signature, the members of an interface. Its
    /// answer says each item's answers, in the order the user puts the items in when the list can be reordered.
    /// </summary>
    List,
}

/// <summary>
/// One thing a refactoring asks its user, with its default.
/// </summary>
public sealed record class RefactorChoice
{
    /// <summary>
    /// What identifies the choice among the refactoring's, in the answer to it (<see cref="RefactorAnswer.Id"/>).
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// What the choice is made of.
    /// </summary>
    public RefactorChoiceKind Kind { get; init; }

    /// <summary>
    /// What a dialog labels the choice with, in the language of the user interface.
    /// </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// What a dialog says of the choice, in the language of the user interface; <see langword="null"/> for a choice its label says enough of.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// The answer the refactoring takes by default: a name, the value of an option, or <c>true</c> or <c>false</c>; <see langword="null"/> for a list, whose
    /// items' choices have their own.
    /// </summary>
    public string? Default { get; init; }

    /// <summary>
    /// The options of an <see cref="RefactorChoiceKind.Option"/> choice, in the order a dialog shows them; none for any other.
    /// </summary>
    public IReadOnlyList<RefactorOption> Options { get; init; } = [];

    /// <summary>
    /// The items of a <see cref="RefactorChoiceKind.List"/> choice, in the order they are written; none for any other.
    /// </summary>
    public IReadOnlyList<RefactorItem> Items { get; init; } = [];

    /// <summary>
    /// Whether the user can put the items of a <see cref="RefactorChoiceKind.List"/> choice in another order: the parameters of a signature can be.
    /// </summary>
    public bool CanReorder { get; init; }
}

/// <summary>
/// One option of a choice.
/// </summary>
public sealed record class RefactorOption
{
    /// <summary>
    /// The answer that picks it.
    /// </summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>
    /// What a dialog labels it with, in the language of the user interface.
    /// </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// What a dialog says of it, in the language of the user interface; <see langword="null"/> for an option its label says enough of.
    /// </summary>
    public string? Description { get; init; }
}

/// <summary>
/// One item of a list a refactoring asks about: a parameter, a member.
/// </summary>
public sealed record class RefactorItem
{
    /// <summary>
    /// What identifies the item among the list's, in the answer to it (<see cref="RefactorItemAnswer.Id"/>).
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// What a dialog labels it with: the item as the code writes it (<c>ByRef total As Long</c>).
    /// </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// Why the item is what it is by default, in the language of the user interface: why a parameter is passed by reference. <see langword="null"/> when
    /// nothing is to be said.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// What the user chooses for the item: its name, how it is passed, whether it is kept.
    /// </summary>
    public IReadOnlyList<RefactorChoice> Choices { get; init; } = [];
}

/// <summary>
/// What a notice of a refactoring says.
/// </summary>
public enum RefactorNoticeKind
{
    /// <summary>
    /// The refactoring is refused, and why: it is not applied, whatever the answers.
    /// </summary>
    Refusal,

    /// <summary>
    /// The refactoring would change what the code does, and how: it is applied only when the user accepts the change
    /// (<see cref="RefactorApplyParams.Accepted"/>).
    /// </summary>
    Change,
}

/// <summary>
/// Why a refactoring is refused, or what it would change of what the code does.
/// </summary>
public sealed record class RefactorNotice
{
    /// <summary>
    /// What the notice is of: a code that names what the refactoring says, the same in any language, by which the user accepts a change.
    /// </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>
    /// Whether it refuses the refactoring, or says a change the user accepts.
    /// </summary>
    public RefactorNoticeKind Kind { get; init; }

    /// <summary>
    /// What the notice says, in the language of the user interface.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Where the code the notice is of is written; <see langword="null"/> for a notice of no place.
    /// </summary>
    public SourceLocation? Location { get; init; }

    /// <summary>
    /// Another place the notice is of, when there is one (<strong>LSP 3.17</strong> §DiagnosticRelatedInformation): the declaration a new name would hide,
    /// the <c>GoTo</c> that jumps into the selection.
    /// </summary>
    public SourceLocation? Related { get; init; }
}

/// <summary>
/// Applies a refactoring of a document's range with its user's answers (<c>rdcore/refactor/apply</c>), and is answered the edit it makes
/// (<see cref="RefactorWorkspaceEdit"/>). A refactoring that is refused, or whose answers it refuses, is answered with an error (<c>RequestFailed</c>,
/// -32803) whose message says why, in the language of the user interface.
/// </summary>
/// <remarks>
/// The edit is not applied by the language server: the client applies it, as it applies the edit of a code action.
/// </remarks>
public sealed record class RefactorApplyParams
{
    /// <inheritdoc cref="RefactorPrepareParams.Kind"/>
    public string Kind { get; init; } = string.Empty;

    /// <inheritdoc cref="RefactorPrepareParams.Uri"/>
    public Uri? Uri { get; init; }

    /// <inheritdoc cref="RefactorPrepareParams.Range"/>
    public SourceRange Range { get; init; }

    /// <summary>
    /// The user's answers, by the choice each is to; a choice that is not answered takes its default.
    /// </summary>
    public IReadOnlyList<RefactorAnswer> Answers { get; init; } = [];

    /// <summary>
    /// The codes of the changes the user accepts (<see cref="RefactorNotice.Code"/>); a refactoring that would make a change that is not accepted is refused.
    /// </summary>
    public IReadOnlyList<string> Accepted { get; init; } = [];
}

/// <summary>
/// The user's answer to one choice.
/// </summary>
public sealed record class RefactorAnswer
{
    /// <summary>
    /// The choice it answers (<see cref="RefactorChoice.Id"/>).
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// A name, the value of an option, or <c>true</c> or <c>false</c>; <see langword="null"/> for a list, whose answer is its items'.
    /// </summary>
    public string? Value { get; init; }

    /// <summary>
    /// For a list, each item's answers, in the order the user put the items in; an item that is not here is kept as it is, where it is.
    /// </summary>
    public IReadOnlyList<RefactorItemAnswer> Items { get; init; } = [];
}

/// <summary>
/// The user's answers for one item of a list.
/// </summary>
public sealed record class RefactorItemAnswer
{
    /// <summary>
    /// The item (<see cref="RefactorItem.Id"/>).
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// The answers to the item's choices; a choice that is not answered takes its default.
    /// </summary>
    public IReadOnlyList<RefactorAnswer> Answers { get; init; } = [];
}

/// <summary>
/// What a refactoring changes in the workspace (<strong>LSP 3.17</strong> §WorkspaceEdit): the documents it creates, then the edits of each document, which a
/// client applies as one change.
/// </summary>
public sealed record class RefactorWorkspaceEdit
{
    /// <summary>
    /// The documents the refactoring creates, empty, before any edit: the module of an interface that is extracted. An edit of the document writes its text.
    /// </summary>
    public IReadOnlyList<Uri> Created { get; init; } = [];

    /// <summary>
    /// The edits of each document, a document once.
    /// </summary>
    public IReadOnlyList<RefactorDocumentEdit> Documents { get; init; } = [];
}

/// <summary>
/// The edits of one document (<strong>LSP 3.17</strong> §TextDocumentEdit).
/// </summary>
public sealed record class RefactorDocumentEdit
{
    /// <summary>
    /// The address of the document.
    /// </summary>
    public Uri? Uri { get; init; }

    /// <summary>
    /// The version of the document the edits are of, as the client said it last; <see langword="null"/> for a document the client has not opened, or that
    /// the refactoring creates. A client refuses edits of any version but the one it has.
    /// </summary>
    public int? Version { get; init; }

    /// <summary>
    /// The edits, of ranges that do not overlap, each of the document as it was before any of them.
    /// </summary>
    public IReadOnlyList<RefactorTextEdit> Edits { get; init; } = [];
}

/// <summary>
/// One edit of a document's text (<strong>LSP 3.17</strong> §TextEdit).
/// </summary>
public sealed record class RefactorTextEdit
{
    /// <summary>
    /// The range of the text it replaces; empty to insert.
    /// </summary>
    public SourceRange Range { get; init; }

    /// <summary>
    /// The text it writes; empty to delete.
    /// </summary>
    public string NewText { get; init; } = string.Empty;
}
