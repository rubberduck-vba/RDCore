using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Client;
using RDCore.SDK.Model.Symbols.Completion;
using RDCore.SDK.Platform.Commands;
using RDCore.SDK.Platform.Protocol;
using System.Text.Json;

namespace RDCore.CLI.App.Repl;

/// <summary>
/// The completion lists of the line at the shell's prompt: what the language server says can be written there, in the program the line will be part of once it is
/// entered - and, for the first word of a line that is not numbered, the shell's own commands, which are what that word can be too.
/// </summary>
/// <remarks>
/// <para>
/// The line is no document: it is asked about with the text of the module it will be in (<see cref="ReplProgram.ToCompletionSource"/>), over
/// <c>rdcore/session/completion</c>. A language server that does not provide that capability gives no lists, and the commands are the only names there are.
/// </para>
/// <para>
/// The arguments of a command are completed by the language server too, as the command describes them (<see cref="IReplCommandDescriptor"/>). The shell registers
/// its described verbs with it the first time one is completed (<c>rdcore/session/commands</c>), and a verb again only when its descriptor has changed since - the
/// themes there are to choose from, and which of them is the current one: a completion request names the verb, and nothing more of it travels.
/// </para>
/// </remarks>
/// <param name="program">The program buffer.</param>
/// <param name="platform">The language server.</param>
/// <param name="commands">The shell's commands.</param>
internal sealed class ReplCompletion(ReplProgram program, IReplPlatformClient platform, IReplCommandDispatcher commands)
{
    // what each verb was registered as, the last time it was: a descriptor that is what it was is not sent again.
    private readonly Dictionary<string, string> _registered = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _registering = new(1, 1);

    /// <summary>
    /// The items that can be written at <paramref name="cursor"/> of <paramref name="line"/>.
    /// </summary>
    /// <param name="line">The line, as it is typed so far.</param>
    /// <param name="cursor">Where the cursor is in it.</param>
    /// <param name="token">A token that cancels the request.</param>
    public async Task<IReadOnlyList<CompletionItem>> CompleteAsync(string line, int cursor, CancellationToken token)
    {
        // the arguments of a command are no VBA: nothing is offered in them unless the command describes them, and then the language server completes them as
        // its verb says, in the program it acts on.
        var typed = line[..Math.Min(cursor, line.Length)].TrimStart();
        var verb = typed.Length > 0 && !char.IsAsciiDigit(typed[0]) ? typed[..IndexOfSeparator(typed)] : string.Empty;
        if (verb.Length < typed.Length && commands.IsCommandName(verb))
        {
            if (CommandNamed(verb) is not IReplCommandDescriptor || !platform.Provides<SessionCompletion>())
            {
                return [];
            }

            await RegisterAsync(token);
            var command = new SessionCommandCompletion { Verb = verb, Arguments = typed[verb.Length..] };
            return [.. (await platform.CompleteCommandAsync(program.ToModuleSource(), ReplProgram.ModuleName, command, token)).Items];
        }

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

        // a list the space after a comma opened is the one a trigger character asks for (ListsAfterSpace); one asked for any other way is the whole of it.
        var listedAfterSpace = Interlocked.Exchange(ref _listedAfterSpace, null);
        var trigger = listedAfterSpace is not null && cursor <= line.Length && line[..cursor] == listedAfterSpace ? " " : null;
        if (platform.Provides<SessionCompletion>() && program.ToCompletionSource(line, cursor) is { } site)
        {
            var list = await platform.CompleteAsync(site.Source, ReplProgram.ModuleName, site.Line, site.Character, token, trigger);
            var commandNames = new HashSet<string>(items.Select(item => item.Label), StringComparer.OrdinalIgnoreCase);
            items.AddRange(list.Items.Where(item => !commandNames.Contains(item.Label)));
        }

        return items;
    }

    /// <summary>
    /// What the call being typed at <paramref name="cursor"/> of <paramref name="line"/> takes, in the program the line will be part of once it is entered.
    /// </summary>
    /// <param name="line">The line, as it is typed so far.</param>
    /// <param name="cursor">Where the cursor is in it.</param>
    /// <param name="token">A token that cancels the request.</param>
    /// <returns>The signature help; <see langword="null"/> for a line that is one of the shell's commands, where no call is typed, and without the capability.</returns>
    public async Task<SignatureHelp?> SignatureHelpAsync(string line, int cursor, CancellationToken token)
    {
        // a command's arguments are no call of the program's.
        var typed = line[..Math.Min(cursor, line.Length)].TrimStart();
        var verb = typed.Length > 0 && !char.IsAsciiDigit(typed[0]) ? typed[..IndexOfSeparator(typed)] : string.Empty;
        if (verb.Length > 0 && commands.IsCommandName(verb) || !platform.Provides<SessionSignatureHelp>() || program.ToCompletionSource(line, cursor) is not { } site)
        {
            return null;
        }

        return await platform.SignatureHelpAsync(site.Source, ReplProgram.ModuleName, site.Line, site.Character, token);
    }

    /// <summary>
    /// Whether the options that can be written after a space are listed as the space is typed: it is typed in the arguments of a command that describes them,
    /// where the language server can say what comes next - or after a <c>,</c> in the arguments of a call, where the constants of an <c>Enum</c> its parameter
    /// is declared as are offered (<c>MsgBox "Hello", </c> lists <c>VbMsgBoxStyle</c>'s), and nothing else is.
    /// </summary>
    /// <param name="beforeCursor">The line up to the cursor, with the space.</param>
    public bool ListsAfterSpace(string beforeCursor)
    {
        var typed = beforeCursor.TrimStart();
        if (typed.Length == 0 || !char.IsWhiteSpace(typed[^1]) || !platform.Provides<SessionCompletion>())
        {
            return false;
        }

        var verb = char.IsAsciiDigit(typed[0]) ? string.Empty : typed[..IndexOfSeparator(typed)];
        if (verb.Length > 0 && verb.Length < typed.Length && commands.IsCommandName(verb))
        {
            return CommandNamed(verb) is IReplCommandDescriptor;
        }

        // the language server is asked as a trigger character asks it: for the list of an Enum argument, and nothing anywhere else.
        if (typed.TrimEnd().EndsWith(',') && SignatureSite.Read(beforeCursor) is not null)
        {
            Interlocked.Exchange(ref _listedAfterSpace, beforeCursor);
            return true;
        }

        return false;
    }

    // the line up to the cursor that a space after a comma opened the list at: the list it asks for is the one a trigger character does, once.
    private string? _listedAfterSpace;

    // the described verbs that are not registered as they are now, in one request; none when every one of them is.
    private async Task RegisterAsync(CancellationToken token)
    {
        await _registering.WaitAsync(token);
        try
        {
            var changed = commands.Commands.OfType<IReplCommandDescriptor>()
                .Select(command => (Descriptor: command.Descriptor, Json: JsonSerializer.Serialize(command.Descriptor)))
                .Where(verb => !_registered.TryGetValue(verb.Descriptor.Name, out var sent) || sent != verb.Json)
                .ToList();
            if (changed.Count == 0)
            {
                return;
            }

            await platform.RegisterCommandsAsync([.. changed.Select(verb => verb.Descriptor)], token);
            foreach (var (descriptor, json) in changed)
            {
                _registered[descriptor.Name] = json;
            }
        }
        finally
        {
            _registering.Release();
        }
    }

    private IReplCommand? CommandNamed(string verb)
        => commands.Commands.FirstOrDefault(command => command.Aliases.Prepend(command.Name).Contains(verb, StringComparer.OrdinalIgnoreCase));

    // a verb ends at the first whitespace, as the shell reads it (ReplInputParser).
    private static int IndexOfSeparator(string text)
    {
        var index = text.AsSpan().IndexOfAny(' ', '\t');
        return index < 0 ? text.Length : index;
    }

    // the first word of a line that has no number, which a command is named by.
    private static bool IsCommandWord(string line, int cursor)
    {
        var before = line[..Math.Min(cursor, line.Length)];
        var site = CompletionSite.Read(before);
        return site.Kind == CompletionSiteKind.Name && before.TrimStart().Length == site.Prefix.Length;
    }
}
