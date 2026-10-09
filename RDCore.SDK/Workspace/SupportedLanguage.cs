using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model;
using RDCore.SDK.Runtime.StdLib;

namespace RDCore.SDK.Workspace;

/// <summary>
/// A language the platform serves: what it is called to a client, which files are written in it, and what the environment it runs in
/// is like - which is what tells one dialect of the family from another.
/// </summary>
/// <remarks>
/// The platform is one implementation of the <strong>MS-VBAL</strong> semantics for several members of the BASIC family
/// (<see cref="SupportedLanguages"/>), and a language is the dialect table behind it: the pieces a dialect decides stay behind this type
/// rather than being re-decided wherever they matter.
/// </remarks>
public class SupportedLanguage
{
    /// <summary>
    /// Creates the definition of a language.
    /// </summary>
    /// <param name="id">The language identifier a client names the language by (<c>languageId</c>).</param>
    /// <param name="name">What the language is called.</param>
    /// <param name="fileTypes">The patterns of the files written in it.</param>
    public SupportedLanguage(string id, string name, params string[] fileTypes)
    {
        Id = id;
        Name = name;
        FileTypes = fileTypes;
    }

    /// <summary>The language identifier a client names the language by (<c>languageId</c>).</summary>
    public string Id { get; }

    /// <summary>What the language is called.</summary>
    public string Name { get; }

    /// <summary>The patterns of the files written in the language.</summary>
    public string[] FileTypes { get; }

    /// <summary>
    /// Where the variable that a reference to an undeclared name declares lives (<strong>MS-VBAL §5.6.10</strong>).
    /// </summary>
    /// <remarks>
    /// <see cref="ImplicitDeclarationScope.Procedure"/> - a local of the procedure - is VBA's and VB6's. <see cref="ImplicitDeclarationScope.Module"/>
    /// is a BASIC's: a variable of the module, so that what one line assigns the next one reads.
    /// </remarks>
    public ImplicitDeclarationScope ImplicitDeclarationScope { get; init; } = ImplicitDeclarationScope.Procedure;

    /// <summary>
    /// Whether a <c>Print</c> statement with no file number - <c>Print "x"</c> - is a statement of the language.
    /// </summary>
    /// <remarks>
    /// In VB6 it is the <c>Print</c> member of the form or report it is written in, so it exists only where there is one. A BASIC has it as its
    /// own: it writes to the output of the program.
    /// <para>
    /// In VBA <c>Print</c> is a reserved identifier: it is illegal as the name of anything, it has no semantics, and it is not a recognized statement
    /// inside a procedure. Only the Immediate window accepts a bare <c>Print</c>, where it writes what <c>Debug.Print</c> does - and the Immediate window
    /// is not procedure scope.
    /// </para>
    /// </remarks>
    public bool HasBarePrint { get; init; }

    /// <summary>
    /// Whether the language has the <c>Option Explicit</c> directive (<strong>MS-VBAL Â§5.2.1.3</strong>), with which a module requires that every
    /// name it uses is declared.
    /// </summary>
    /// <remarks>
    /// VBA and VB6 do. A BASIC does not: a variable is whatever a line first assigns, which is what makes it a language to type in a line at a time, and a
    /// module of it cannot state a directive that it has no way to write. Whether a module states it is not something to have an opinion about, then.
    /// </remarks>
    public bool HasOptionExplicit { get; init; } = true;

    /// <summary>
    /// Whether the language is written in the syntax of classic BASIC: a <c>Rem</c> comment and the <c>Error</c> statement are how it writes a comment and raises an
    /// error, and not relics of the language it descends from.
    /// </summary>
    /// <remarks>
    /// A BASIC does, since that is what it is. VBA and VB6 keep both for backward compatibility only: <c>'</c> is the comment, and <c>Err.Raise</c> raises an error
    /// (<strong>MS-VBAL §5.4.4.3</strong>).
    /// </remarks>
    public bool UsesClassicBasicSyntax { get; init; }
    /// <summary>The patterns of the files written in the language, as a document filter pattern.</summary>
    public string FilterString => string.Join(";", FileTypes.Select(fileType => $"**/{fileType}").ToArray());

    /// <summary>The selector of the documents written in the language.</summary>
    public TextDocumentSelector ToTextDocumentSelector() => new(
        new TextDocumentFilter
        {
            Language = Id,
            Pattern = FilterString,
        });

    /// <inheritdoc/>
    public override string ToString() => Id;
}

/// <summary>
/// The languages the platform serves.
/// </summary>
public static class SupportedLanguages
{
    /// <summary>
    /// RD-VBA: the platform's own implementation of <strong>MS-VBAL</strong>, and the default.
    /// </summary>
    public static SupportedLanguage RDVBA { get; } = new(
        "vba", "Microsoft Visual Basic for Applications", "*.bas", "*.cls", "*.frm", "*.doccls");

    /// <summary>
    /// VB6.
    /// </summary>
    public static SupportedLanguage VB6 { get; } = new("vb6", "Microsoft Visual Basic 6.0", "*.bas", "*.cls", "*.frm");

    /// <summary>
    /// The platform's BASIC, which an interactive shell is written in.
    /// </summary>
    /// <remarks>
    /// Its source is a <c>.rdc</c> file: a program of the shell, a text file of lines. It is not a <c>.bas</c>, which is a module of the BASIC of Visual Basic - a file with
    /// procedures in it, and a header - and which this is not.
    /// </remarks>
    public static SupportedLanguage BASIC { get; } = new("basic", "RDCore BASIC", "*.rdc")
    {
        ImplicitDeclarationScope = ImplicitDeclarationScope.Module,
        HasBarePrint = true,
        HasOptionExplicit = false,
        UsesClassicBasicSyntax = true,
    };

    /// <summary>Every language the platform serves.</summary>
    public static IReadOnlyList<SupportedLanguage> All { get; } = [RDVBA, VB6, BASIC];

    /// <summary>
    /// The language with the given identifier.
    /// </summary>
    /// <param name="id">The identifier, as <see cref="SupportedLanguage.Id"/>; compared without regard to case.</param>
    /// <param name="language">The language, when there is one by that identifier.</param>
    /// <returns><see langword="false"/> when the platform serves no such language.</returns>
    public static bool TryGet(string? id, out SupportedLanguage language)
    {
        language = All.FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase))!;
        return language is not null;
    }

    /// <summary>
    /// The language with the given identifier.
    /// </summary>
    /// <param name="id">The identifier, as <see cref="SupportedLanguage.Id"/>.</param>
    /// <exception cref="InvalidOperationException">The platform serves no such language.</exception>
    public static SupportedLanguage Get(string? id)
        => TryGet(id, out var language)
            ? language
            : throw new InvalidOperationException(
                $"'{id}' is not a language the platform serves. They are: {string.Join(", ", All.Select(candidate => candidate.Id))}.");
}
