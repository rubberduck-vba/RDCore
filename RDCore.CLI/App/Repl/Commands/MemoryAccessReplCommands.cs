using RDCore.SDK.Client;
using RDCore.SDK.ConsoleIO.Model;
using RDCore.SDK.Platform.Protocol;
using System.Globalization;

namespace RDCore.CLI.App.Repl.Commands;

/// <summary>
/// <c>PEEK &lt;address&gt;</c>: reads the byte at an address in the runtime session's memory.
/// </summary>
internal sealed class PeekReplCommand : IReplCommand
{
    public string Name => "PEEK";
    public IReadOnlyList<string> Aliases => [];
    public string Summary => Resources.Repl_Peek_Summary;

    public async Task<ReplCommandResult> ExecuteAsync(ReplCommandContext context, string arguments, CancellationToken token)
    {
        if (!MemoryAccess.IsAvailable(context))
        {
            return ReplCommandResult.Continue;
        }

        if (MemoryAccess.ParseArguments(arguments) is not [var address])
        {
            context.Console.WriteMessage(MessageKind.Error, Resources.Repl_Memory_BadArguments, arguments);
            return ReplCommandResult.Continue;
        }

        var result = await context.Platform.PeekAsync(address, token);
        if (!result.IsAllocated)
        {
            context.Console.WriteMessage(MessageKind.Error, Resources.Repl_Memory_NotAllocated, MemoryAccess.Format(address));
            return ReplCommandResult.Continue;
        }

        context.Console.WriteLine($" {result.Value} ");
        return ReplCommandResult.Continue;
    }
}

/// <summary>
/// <c>POKE &lt;address&gt;, &lt;value&gt;</c>: writes a byte at an address in the runtime session's
/// memory.
/// </summary>
/// <remarks>
/// Unchecked, exactly as its BASIC namesake is. The byte goes into whatever is there — including the
/// middle of a live variable, which then holds whatever the new bytes spell.
/// </remarks>
internal sealed class PokeReplCommand : IReplCommand
{
    public string Name => "POKE";
    public IReadOnlyList<string> Aliases => [];
    public string Summary => Resources.Repl_Poke_Summary;

    public async Task<ReplCommandResult> ExecuteAsync(ReplCommandContext context, string arguments, CancellationToken token)
    {
        if (!MemoryAccess.IsAvailable(context))
        {
            return ReplCommandResult.Continue;
        }

        if (MemoryAccess.ParseArguments(arguments) is not [var address, var value] || value is < 0 or > 255)
        {
            context.Console.WriteMessage(MessageKind.Error, Resources.Repl_Memory_BadArguments, arguments);
            return ReplCommandResult.Continue;
        }

        var result = await context.Platform.PokeAsync(address, (byte)value, token);
        if (!result.IsWritten)
        {
            context.Console.WriteMessage(MessageKind.Error, Resources.Repl_Memory_NotAllocated, MemoryAccess.Format(address));
        }

        return ReplCommandResult.Continue;
    }
}

/// <summary>
/// What <c>PEEK</c> and <c>POKE</c> share: the capability they need, and how they read an address.
/// </summary>
internal static class MemoryAccess
{
    /// <summary>
    /// Whether the platform provides byte-level session memory access, complaining if it does not.
    /// </summary>
    /// <param name="context">The live session, which is also where the complaint goes.</param>
    public static bool IsAvailable(ReplCommandContext context)
    {
        if (context.Platform.Provides<SessionMemoryAccess>())
        {
            return true;
        }

        context.Console.WriteMessage(MessageKind.Error, Resources.Repl_NotAvailable,
            string.Format(Resources.Repl_NotAvailable_Verbose, nameof(SessionMemoryAccess)));
        return false;
    }

    /// <summary>
    /// Reads the comma-separated numbers of a memory command's argument list, or <c>null</c> when any
    /// of them is not a number.
    /// </summary>
    /// <remarks>
    /// Decimal, or hexadecimal with a <c>&amp;H</c> prefix — VBA's own spelling for a hex literal, and
    /// the one a memory address is most naturally written in.
    /// </remarks>
    /// <param name="arguments">Everything typed after the verb.</param>
    public static int[]? ParseArguments(string arguments)
    {
        var parsed = new List<int>();
        foreach (var part in arguments.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var isHex = part.StartsWith("&H", StringComparison.OrdinalIgnoreCase);
            var digits = isHex ? part[2..] : part;
            if (!int.TryParse(digits, isHex ? NumberStyles.HexNumber : NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                return null;
            }
            parsed.Add(number);
        }

        return parsed.Count == 0 ? null : [.. parsed];
    }

    /// <summary>An address, as a message about it should spell it.</summary>
    public static string Format(int address) => $"&H{address:X}";
}
