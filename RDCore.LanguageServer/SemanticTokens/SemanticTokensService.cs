using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;

namespace RDCore.LanguageServer.SemanticTokens;

/// <summary>
/// One semantic token of a document, as an index into <see cref="SemanticTokenLegend"/> and the modifiers of it as bits.
/// </summary>
/// <param name="Line">The zero-based line.</param>
/// <param name="Character">The zero-based character of the line, in UTF-16 code units.</param>
/// <param name="Length">The length, in UTF-16 code units.</param>
/// <param name="Type">The index of the token type in <see cref="SemanticTokenLegend.TokenTypes"/>.</param>
/// <param name="Modifiers">The token modifiers, as the bits of <see cref="SemanticTokenLegend.TokenModifiers"/>.</param>
internal readonly record struct SemanticToken(int Line, int Character, int Length, int Type, int Modifiers);

/// <summary>
/// The semantic tokens of a document (<strong>LSP 3.17</strong> §Semantic Tokens).
/// </summary>
internal interface ISemanticTokensService
{
    /// <summary>
    /// The semantic tokens of a document, in the order they are in it.
    /// </summary>
    /// <param name="documentUri">The document.</param>
    /// <param name="token">A token that cancels the request.</param>
    /// <returns>The tokens of the text the document has now; none for a document the server does not hold.</returns>
    Task<IReadOnlyList<SemanticToken>> GetAsync(Uri documentUri, CancellationToken token);

    /// <summary>
    /// The semantic tokens of code fragments a client supplies - the condition of a breakpoint, say - which are in no document, with the names in them told apart
    /// by what a module declares.
    /// </summary>
    /// <param name="moduleName">The module's programmatic name: the module of the workspace by that name is the one the source stands for.</param>
    /// <param name="source">The complete source of the module, which the names are resolved against.</param>
    /// <param name="fragments">The fragments of code, each as it is written.</param>
    /// <param name="token">A token that cancels the request.</param>
    /// <returns>The tokens of each fragment, in the order they were asked, positioned in the fragment.</returns>
    Task<IReadOnlyList<IReadOnlyList<SemanticToken>>> GetFragmentsAsync(string moduleName, string source, IReadOnlyList<string> fragments, CancellationToken token);
}

/// <summary>
/// The lexical tokens the parsing server finds in the text of a document, with the names in them told apart by what the module they are in says they are.
/// </summary>
/// <remarks>
/// A name is the name of a constant, or of a type, where the module - or the workspace - declares one by that name: the classification goes by the name, not by
/// the binding, which the resolver does not hand to the language server. A name that follows a <c>.</c> is a member of something, and is never either. The
/// text of the document is what is tokenized, whatever kind of file it is: the positions are the ones of the text a client is showing.
/// </remarks>
internal sealed class SemanticTokensService(IWorkspaceDocumentService documents, IParsingClientService parsing) : ISemanticTokensService
{
    public async Task<IReadOnlyList<SemanticToken>> GetAsync(Uri documentUri, CancellationToken token)
    {
        if (!documents.TryGetDocument(documentUri, out var document))
        {
            return [];
        }

        var text = document.Text;
        var tokens = await parsing.TokenizeAsync(documentUri, text, token);
        var parse = await parsing.ParseDocumentAsync(documentUri, token);
        return Classified(text, tokens, DeclaredNames(documentUri, parse));
    }

    public async Task<IReadOnlyList<IReadOnlyList<SemanticToken>>> GetFragmentsAsync(string moduleName, string source, IReadOnlyList<string> fragments, CancellationToken token)
    {
        // the fragments are code of the module, whose address they are tokenized and its declarations read at.
        var address = SymbolUri.Module(documents.WorkspaceRoot, moduleName);
        var names = DeclaredNames(address, await parsing.ParseFragmentAsync(address, source, token));

        var result = new List<IReadOnlyList<SemanticToken>>(fragments.Count);
        foreach (var fragment in fragments)
        {
            token.ThrowIfCancellationRequested();
            result.Add(fragment.Length == 0 ? [] : Classified(fragment, await parsing.TokenizeAsync(address, fragment, token), names));
        }

        return result;
    }

    // the tokens of a text, with the names in them told apart by what is declared.
    private static List<SemanticToken> Classified(string text, IReadOnlyList<SyntaxToken> tokens, (HashSet<string> Constants, HashSet<string> Types) names)
    {
        var lines = text.Split('\n');
        var result = new List<SemanticToken>(tokens.Count);
        foreach (var syntaxToken in tokens)
        {
            if (syntaxToken.Length <= 0)
            {
                continue;
            }

            var type = SemanticTokenLegend.TypeOf(syntaxToken.Kind);
            var modifiers = 0;
            if (syntaxToken.Kind == SyntaxTokenKind.Identifier && NameAt(lines, syntaxToken) is { } name && !IsMemberAccess(lines, syntaxToken))
            {
                if (names.Types.Contains(name))
                {
                    type = SemanticTokenLegend.TypeOf(SyntaxTokenKind.TypeName);
                }
                else if (names.Constants.Contains(name))
                {
                    modifiers = SemanticTokenLegend.ReadOnly;
                }
            }

            result.Add(new SemanticToken(syntaxToken.Line, syntaxToken.Character, syntaxToken.Length, type, modifiers));
        }

        return result;
    }

    private (HashSet<string> Constants, HashSet<string> Types) DeclaredNames(Uri documentUri, ModuleParseResult parse)
    {
        var constants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // the class modules of the workspace are types, whatever module of it is being highlighted. A document that is no file, or of a server with no workspace,
        // is a project of its own (SymbolUri.RootOf), as completion composes it: its own module is the only one there is.
        var isOwnProject = !documentUri.IsFile || documents.WorkspaceRoot.Length == 0;
        IEnumerable<WorkspaceDocument> modules = !isOwnProject ? documents.GetAllDocuments()
            : documents.TryGetModule(documentUri, out var own) ? [own]
            : [];
        foreach (var other in modules)
        {
            var path = other.Id.Uri.GetFileSystemPath();
            if (string.Equals(Path.GetExtension(path), ".cls", StringComparison.OrdinalIgnoreCase))
            {
                types.Add(Path.GetFileNameWithoutExtension(path));
            }
        }

        if (parse.SyntaxTree is { } module)
        {
            Collect(module, constants, types);
        }

        return (constants, types);
    }

    private static void Collect(SyntaxNode node, HashSet<string> constants, HashSet<string> types)
    {
        switch (node)
        {
            case ConstantDeclarationNode constant:
                constants.Add(constant.Name);
                break;
            case MemberDeclarationNode { MemberKind: MemberKind.Enum or MemberKind.UserDefinedType } declaration:
                types.Add(declaration.Name);
                break;
        }

        foreach (var child in node.Children)
        {
            Collect(child, constants, types);
        }
    }

    private static string? NameAt(string[] lines, SyntaxToken token)
        => token.Line < lines.Length && token.Character + token.Length <= lines[token.Line].Length
            ? lines[token.Line].Substring(token.Character, token.Length)
            : null;

    private static bool IsMemberAccess(string[] lines, SyntaxToken token)
    {
        if (token.Line >= lines.Length)
        {
            return false;
        }

        var before = lines[token.Line].AsSpan(0, Math.Min(token.Character, lines[token.Line].Length)).TrimEnd();
        return before.Length > 0 && before[^1] is '.' or '!';
    }
}
