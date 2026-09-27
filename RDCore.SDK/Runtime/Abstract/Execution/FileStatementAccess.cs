using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Statements;

namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// Which file statements a channel opened under a given mode and access may be used by
/// (<strong>MS-VBAL §5.4.5.1</strong>'s own table).
/// </summary>
/// <remarks>
/// The specification states this as one table covering every file statement, so it is encoded once here
/// rather than restated by each of them — a statement asks whether it is allowed on a channel and reports
/// <c>Bad file mode</c> when it is not. Getting it wrong in twelve places independently is the alternative.
/// <para>
/// 👉 A blank cell in that table means the statement is not valid in that mode <em>at all</em>, whatever the
/// access; a cell listing accesses means valid only under those.
/// </para>
/// </remarks>
public static class FileStatementAccess
{
    // MS-VBAL 5.4.5.1's table, transcribed: per statement, the accesses valid in each mode. An absent mode
    // key is a blank cell - not valid in that mode under any access.
    private static readonly Dictionary<string, Dictionary<VBFileMode, VBFileAccessMode[]>> _table = new(StringComparer.OrdinalIgnoreCase)
    {
        [Tokens.Get] = new()
        {
            [VBFileMode.Binary] = [VBFileAccessMode.Read, VBFileAccessMode.ReadWrite],
            [VBFileMode.Random] = [VBFileAccessMode.Read, VBFileAccessMode.ReadWrite],
        },
        [Tokens.Put] = new()
        {
            [VBFileMode.Binary] = [VBFileAccessMode.ReadWrite, VBFileAccessMode.Write],
            [VBFileMode.Random] = [VBFileAccessMode.ReadWrite, VBFileAccessMode.Write],
        },
        [Tokens.Input] = new()
        {
            [VBFileMode.Binary] = [VBFileAccessMode.Read, VBFileAccessMode.ReadWrite],
            [VBFileMode.Input] = [VBFileAccessMode.Read],
        },
        [Tokens.LineInput] = new()
        {
            [VBFileMode.Binary] = [VBFileAccessMode.Read, VBFileAccessMode.ReadWrite],
            [VBFileMode.Input] = [VBFileAccessMode.Read],
        },
        [Tokens.Print] = new()
        {
            [VBFileMode.Append] = [VBFileAccessMode.ReadWrite, VBFileAccessMode.Write],
            [VBFileMode.Output] = [VBFileAccessMode.Write],
        },
        [Tokens.Write] = new()
        {
            [VBFileMode.Append] = [VBFileAccessMode.ReadWrite, VBFileAccessMode.Write],
            [VBFileMode.Output] = [VBFileAccessMode.Write],
        },
        [Tokens.Seek] = EveryMode(),
        [Tokens.Width] = EveryMode(),
        [Tokens.Lock] = EveryMode(),
        [Tokens.Unlock] = EveryMode(),
    };

    /// <summary>
    /// Whether <paramref name="statement"/> may be used on a channel opened under <paramref name="mode"/>
    /// and <paramref name="access"/>.
    /// </summary>
    /// <remarks>
    /// A statement this table says nothing about is not restricted by it — <c>Close</c> and <c>Reset</c> are
    /// valid on any channel, and the table does not list them.
    /// </remarks>
    /// <param name="statement">The statement's keyword — a <see cref="Tokens"/> constant, including the
    /// two-keyword <see cref="Tokens.LineInput"/>.</param>
    /// <param name="mode">The mode the channel was opened under.</param>
    /// <param name="access">The access the channel was opened under.</param>
    public static bool IsValid(string statement, VBFileMode mode, VBFileAccessMode access)
        => !_table.TryGetValue(statement, out var byMode)
            || (byMode.TryGetValue(mode, out var accesses) && accesses.Contains(access));

    // Seek, Width and Lock share one row: valid in every mode, under whichever accesses that mode implies.
    // Append and Output are write-side modes, so a Read-only channel of them cannot exist to be asked about.
    private static Dictionary<VBFileMode, VBFileAccessMode[]> EveryMode() => new()
    {
        [VBFileMode.Append] = [VBFileAccessMode.ReadWrite, VBFileAccessMode.Write],
        [VBFileMode.Binary] = [VBFileAccessMode.Read, VBFileAccessMode.ReadWrite, VBFileAccessMode.Write],
        [VBFileMode.Input] = [VBFileAccessMode.Read],
        [VBFileMode.Output] = [VBFileAccessMode.Write],
        [VBFileMode.Random] = [VBFileAccessMode.Read, VBFileAccessMode.ReadWrite, VBFileAccessMode.Write],
    };

    /// <summary>
    /// The access an <c>Open</c> with no <c>Access</c> clause implies, from its mode
    /// (<strong>MS-VBAL §5.4.5.1</strong>'s other table).
    /// </summary>
    /// <param name="mode">The mode the <c>Open</c> declared, or defaulted to <see cref="VBFileMode.Random"/>.</param>
    public static VBFileAccessMode ImpliedAccess(VBFileMode mode) => mode switch
    {
        VBFileMode.Input => VBFileAccessMode.Read,
        VBFileMode.Output => VBFileAccessMode.Write,
        _ => VBFileAccessMode.ReadWrite,
    };
}
