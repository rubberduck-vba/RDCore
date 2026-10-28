using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Workspace;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.LanguageServer.Folding;

/// <summary>
/// Serves <c>textDocument/selectionRange</c> (<strong>LSP 3.17</strong> §Selection Range Request): for each position, what a selection there grows to, step by
/// step - a name, its expression, its statement, the block, the procedure, the module (<see cref="SyntaxSelection"/>).
/// </summary>
/// <remarks>
/// A position is answered with a range for each position asked, as LSP requires: one that no node is written around is answered with the empty range at
/// it. Ranges are in the text of the document: for a program of the platform's BASIC, its lines.
/// </remarks>
internal sealed class SelectionRangeHandler(
    IWorkspaceDocumentService documents,
    IParsingClientService parsing) : SelectionRangeHandlerBase
{
    public override async Task<Container<SelectionRange>?> Handle(SelectionRangeParams request, CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri.ToUri();
        var document = documents.TryGetModule(uri, out var module) ? module : null;
        var parse = document is null ? null : await parsing.ParseDocumentAsync(uri, cancellationToken);
        var isProgram = document is not null && BasicProgramText.IsProgram(document.Id.Uri.GetFileSystemPath());

        return new Container<SelectionRange>(request.Positions.Select(position =>
        {
            var at = new SourcePosition(isProgram ? position.Line + BasicProgramText.HeaderLines : position.Line, position.Character);
            var ranges = parse?.SyntaxTree is { } module ? SyntaxSelection.RangesAt(module, at) : [];

            // the outermost range is the parent of every other: built from it inward.
            SelectionRange? selection = null;
            foreach (var range in ranges.Reverse())
            {
                var lsp = new LspRange(range.Start.Line, range.Start.Character, range.End.Line, range.End.Character);
                // the outermost has no parent, which LSP writes by leaving it out.
                selection = new SelectionRange { Range = isProgram ? BasicProgramText.ToTextRange(lsp) : lsp, Parent = selection! };
            }

            return selection ?? new SelectionRange { Range = new LspRange(position, position) };
        }));
    }

    protected override SelectionRangeRegistrationOptions CreateRegistrationOptions(SelectionRangeCapability capability, ClientCapabilities clientCapabilities)
        => new();
}
