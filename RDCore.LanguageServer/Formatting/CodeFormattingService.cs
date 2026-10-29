using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Formatting;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;

namespace RDCore.LanguageServer.Formatting;

/// <summary>
/// The edits that format a document the server holds, by the formatting settings as they are when it is asked (<see cref="ModuleIndenter"/>).
/// </summary>
internal interface ICodeFormattingService
{
    /// <summary>
    /// The edits that indent a document's lines: all of them, or those of <paramref name="lines"/>; none for a document the server does not hold.
    /// </summary>
    /// <param name="documentUri">The document.</param>
    /// <param name="lines">The first and the last line to format, from 0; the whole document when <see langword="null"/>.</param>
    /// <param name="token">A token that cancels the request.</param>
    Task<IReadOnlyList<TextEdit>> FormatAsync(Uri documentUri, (int First, int Last)? lines, CancellationToken token);
}

internal sealed class CodeFormattingService(IWorkspaceDocumentService documents, IParsingClientService parsing, IOptions<SdkAppOptions> options) : ICodeFormattingService
{
    public async Task<IReadOnlyList<TextEdit>> FormatAsync(Uri documentUri, (int First, int Last)? lines, CancellationToken token)
    {
        // a program of the platform's BASIC is parsed as the body of a procedure its text does not write: the shell lists its lines as it formats them.
        if (!documents.TryGetDocument(documentUri, out var document) || BasicProgramText.IsProgram(document.Id.Uri.GetFileSystemPath()))
        {
            return [];
        }

        // the settings are read as they are now: one the user changed applies to the next request.
        var formatting = options.Value.Formatting.ToOptions();
        var text = document.Text;
        var parse = await parsing.ParseDocumentAsync(documentUri, token);
        var (first, last) = lines ?? (0, int.MaxValue);
        return [.. ModuleIndenter.Indent(parse, text, formatting, first, last).Select(edit => new TextEdit
        {
            Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(edit.Line, edit.Character, edit.EndLine, edit.EndCharacter),
            NewText = edit.NewText,
        })];
    }
}
