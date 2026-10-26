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

    private static string Say(string key, CultureInfo? culture, params object[] arguments)
    {
        culture ??= CultureInfo.CurrentUICulture;
        return string.Format(culture, Messages.GetString(key, culture) ?? key, arguments);
    }
}
