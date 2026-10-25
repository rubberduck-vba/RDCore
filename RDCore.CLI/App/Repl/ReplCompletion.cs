using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Client;
using RDCore.SDK.Model.Symbols.Completion;

namespace RDCore.CLI.App.Repl;

/// <summary>
/// The completion lists of the line at the shell's prompt: what the language server says can be written there, in the program the line will be part of once it is
/// entered - and, for the first word of a line that is not numbered, the shell's own commands, which are what that word can be too.
/// </summary>
/// <remarks>
/// The line is no document: it is asked about with the text of the module it will be in (<see cref="ReplProgram.ToCompletionSource"/>), over
/// <c>rdcore/session/completion</c>. A language server that does not provide that capability gives no lists, and the commands are the only names there are.
/// </remarks>
/// <param name="program">The program buffer.</param>
/// <param name="platform">The language server.</param>
/// <param name="commands">The shell's commands.</param>
internal sealed class ReplCompletion(ReplProgram program, IReplPlatformClient platform, IReplCommandDispatcher commands)
{
    /// <summary>
    /// The items that can be written at <paramref name="cursor"/> of <paramref name="line"/>.
    /// </summary>
    /// <param name="line">The line, as it is typed so far.</param>
    /// <param name="cursor">Where the cursor is in it.</param>
    /// <param name="token">A token that cancels the request.</param>
    public async Task<IReadOnlyList<CompletionItem>> CompleteAsync(string line, int cursor, CancellationToken token)
    {
        var items = new List<CompletionItem>();
        if (IsCommandWord(line, cursor))
        {
            items.AddRange(commands.Commands.Select(command => new CompletionItem
            {
                Label = command.Name,
                Kind = CompletionItemKind.Keyword,
                Detail = command.Summary,
                SortText = command.Name.ToLowerInvariant(),
            }));
        }

        if (platform.Provides<SessionCompletion>() && program.ToCompletionSource(line, cursor) is { } site)
        {
            var list = await platform.CompleteAsync(site.Source, ReplProgram.ModuleName, site.Line, site.Character, token);
            var commandNames = new HashSet<string>(items.Select(item => item.Label), StringComparer.OrdinalIgnoreCase);
            items.AddRange(list.Items.Where(item => !commandNames.Contains(item.Label)));
        }

        return items;
    }

    // the first word of a line that has no number, which a command is named by.
    private static bool IsCommandWord(string line, int cursor)
    {
        var before = line[..Math.Min(cursor, line.Length)];
        var site = CompletionSite.Read(before);
        return site.Kind == CompletionSiteKind.Name && before.TrimStart().Length == site.Prefix.Length;
    }
}
