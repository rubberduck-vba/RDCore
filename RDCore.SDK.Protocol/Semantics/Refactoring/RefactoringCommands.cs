namespace RDCore.SDK.Semantics.Refactoring;

/// <summary>
/// The commands of the platform's code actions that a client runs itself (<strong>LSP 3.17</strong> <c>CodeAction.command</c>): what only the client can do,
/// such as asking its user for a name.
/// </summary>
public static class RefactoringCommands
{
    /// <summary>
    /// Renames the symbol named at a position of a document, to a name the client asks its user for: the client asks, and sends <c>textDocument/rename</c> with
    /// the name it is given, unless the user cancels. Its arguments are the URI of the document and the position (<strong>LSP 3.17</strong> <c>Position</c>).
    /// </summary>
    public const string Rename = "rdcore.rename";

    /// <summary>
    /// Shows its user where a symbol is referred to: the command of a code lens that counts the references of a member (<strong>LSP 3.17</strong>
    /// <c>CodeLens.command</c>). Its arguments are the URI of the document, the position of the member's name (<strong>LSP 3.17</strong> <c>Position</c>), and the
    /// references (<strong>LSP 3.17</strong> <c>Location[]</c>): the arguments of VS Code's <c>editor.action.showReferences</c>.
    /// </summary>
    public const string ShowReferences = "rdcore.showReferences";
}
