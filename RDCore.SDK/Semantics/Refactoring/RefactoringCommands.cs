using System.Globalization;
using System.Resources;

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
    /// references (<strong>LSP 3.17</strong> <c>Location[]</c>) - the arguments of VS Code's <c>editor.action.showReferences</c>.
    /// </summary>
    public const string ShowReferences = "rdcore.showReferences";
}

/// <summary>
/// What the platform's code actions are called, in the language of the reader.
/// </summary>
public static class RefactoringTitles
{
    private static readonly ResourceManager Messages = new("RDCore.SDK.Semantics.Refactoring.RefactoringMessages", typeof(RefactoringTitles).Assembly);

    /// <summary>The title of the code action that renames a symbol to a name the user chooses: <c>Rename 'x'…</c>.</summary>
    /// <param name="name">The name the symbol is declared with.</param>
    /// <param name="culture">The language of the reader; the current UI culture by default.</param>
    public static string Rename(string name, CultureInfo? culture = null) => Say("CodeAction_Rename", culture, name);

    /// <summary>The title of the code action that renames a symbol to a name it suggests: <c>Rename 'strName' to 'Name'</c>.</summary>
    /// <param name="name">The name the symbol is declared with.</param>
    /// <param name="newName">The name it is renamed to.</param>
    /// <param name="culture">The language of the reader; the current UI culture by default.</param>
    public static string RenameTo(string name, string newName, CultureInfo? culture = null) => Say("CodeAction_RenameTo", culture, name, newName);

    /// <summary>The title of the code lens that counts the references of a member: <c>3 references</c>.</summary>
    /// <param name="count">The number of references.</param>
    /// <param name="culture">The language of the reader; the current UI culture by default.</param>
    /// <remarks>Each language says none, one and more its own way: French says <c>0 référence</c>, English <c>0 references</c>.</remarks>
    public static string References(int count, CultureInfo? culture = null)
        => Say(count switch { 0 => "CodeLens_References_None", 1 => "CodeLens_References_One", _ => "CodeLens_References_Many" }, culture, count);

    /// <summary>
    /// The label of a rename's edits of the strings a library calls the symbol by (<c>RenameEdit.IsInString</c>): <c>Called by its name in a string</c>.
    /// A client that takes change annotations shows it with the edits it asks its user to confirm (<strong>LSP 3.17</strong> <c>ChangeAnnotation.label</c>).
    /// </summary>
    /// <param name="culture">The language of the reader; the current UI culture by default.</param>
    public static string RenameInStringLabel(CultureInfo? culture = null) => Say("Rename_InString_Label", culture);

    /// <summary>
    /// What the edits of <see cref="RenameInStringLabel"/> are, less prominently than the label (<strong>LSP 3.17</strong> <c>ChangeAnnotation.description</c>):
    /// why the user is asked.
    /// </summary>
    /// <param name="name">The name the symbol is declared with.</param>
    /// <param name="culture">The language of the reader; the current UI culture by default.</param>
    public static string RenameInStringDescription(string name, CultureInfo? culture = null) => Say("Rename_InString_Description", culture, name);

    /// <summary>
    /// What the user of a client that does not take change annotations is asked, before a rename's edits of the strings a library calls the symbol by are sent
    /// to it: <c>'Foo' is called by its name in 2 strings, the first at Module1 4:12. Rename it there too?</c>
    /// </summary>
    /// <param name="name">The name the symbol is declared with.</param>
    /// <param name="count">How many strings the rename edits.</param>
    /// <param name="where">Where the first of them is, as the client names a position.</param>
    /// <param name="culture">The language of the reader; the current UI culture by default.</param>
    public static string RenameInStringQuestion(string name, int count, string where, CultureInfo? culture = null)
        => Say(count == 1 ? "Rename_InString_Question_One" : "Rename_InString_Question_Many", culture, name, count, where);

    /// <summary>
    /// The answer to <see cref="RenameInStringQuestion"/> that renames the name in the strings too.
    /// </summary>
    /// <param name="culture">The language of the reader; the current UI culture by default.</param>
    public static string RenameInStringRename(CultureInfo? culture = null) => Say("Rename_InString_Rename", culture);

    /// <summary>
    /// The answer to <see cref="RenameInStringQuestion"/> that renames the symbol and leaves the strings as they are.
    /// </summary>
    /// <param name="culture">The language of the reader; the current UI culture by default.</param>
    public static string RenameInStringLeave(CultureInfo? culture = null) => Say("Rename_InString_Leave", culture);

    private static string Say(string key, CultureInfo? culture, params object[] arguments)
    {
        culture ??= CultureInfo.CurrentUICulture;
        return string.Format(culture, Messages.GetString(key, culture) ?? key, arguments);
    }
}
