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
    /// The name of the project the language's standard library is: what a project-qualified reference to it uses
    /// (<c>VBA.Strings.LenB</c>, <c>VBA.LenB</c>; <strong>MS-VBAL §5.6.12</strong>), and what its members say they belong to.
    /// </summary>
    /// <remarks>
    /// <c>VBA</c> for RD-VBA, <c>VB</c> for VB6, and <c>RDC</c> for the platform's BASIC.
    /// </remarks>
    public string StandardLibraryName { get; init; } = StdLibSymbolProvider.DefaultLibraryName;

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
    /// In VB6 it is the <c>Print</c> member of the form or report it is written in, so it exists only where there is one; and VBA has no such
    /// statement at all, which is why a call of it is undefined. A BASIC has it as its own: it writes to the output of the program.
    /// </remarks>
    public bool HasBarePrint { get; init; }

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
    /// RD-VBA: the platform's own implementation of <strong>MS-VBAL</strong>, and the default. Its standard library is <c>VBA</c>.
    /// </summary>
    public static SupportedLanguage RDVBA { get; } = new(
        "vba", "Microsoft Visual Basic for Applications", "*.bas", "*.cls", "*.frm", "*.doccls");

    /// <summary>
    /// VB6. Its standard library is <c>VB</c>.
    /// </summary>
    public static SupportedLanguage VB6 { get; } = new("vb6", "Microsoft Visual Basic 6.0", "*.bas", "*.cls", "*.frm")
    {
        StandardLibraryName = "VB",
    };

    /// <summary>
    /// The platform's BASIC, which an interactive shell is written in. Its standard library is <c>RDC</c>.
    /// </summary>
    public static SupportedLanguage BASIC { get; } = new("basic", "RDCore BASIC", "*.bas")
    {
        StandardLibraryName = "RDC",
        ImplicitDeclarationScope = ImplicitDeclarationScope.Module,
        HasBarePrint = true,
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
