using RDCore.SDK.Client;
using RDCore.SDK.ConsoleIO.Model;
using System.Globalization;

namespace RDCore.CLI.App.Repl.Commands;

/// <summary>
/// What <c>PEEK</c> and <c>POKE</c> share: the capability they need, and how they read an address.
/// </summary>
internal static class MemoryAccessService
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
