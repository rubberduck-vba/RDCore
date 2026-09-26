using RDCore.SDK.Model.AST.Statements;

namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// One <em>file number</em> that an <c>Open</c> statement associated with an external data file
/// (<strong>MS-VBAL §5.4.5</strong>), and the processing modes it was opened under.
/// </summary>
/// <remarks>
/// The association "remains in effect until they are explicitly disassociated using a
/// <c>close-statement</c>" (<strong>§5.4.5.1</strong>) — so a channel outlives the procedure that opened it
/// and belongs to the session, not to a call frame.
/// </remarks>
public interface IFileChannel
{
    /// <summary>
    /// The file number source refers to this channel by — <c>#1</c>'s <c>1</c>.
    /// </summary>
    int FileNumber { get; }

    /// <summary>
    /// The complete path specification the channel was opened on.
    /// </summary>
    string Path { get; }

    /// <summary>
    /// How data is read from and written to the file. <see cref="VBFileMode.Random"/> when the
    /// <c>Open</c> declared no <c>For</c> clause (<strong>§5.4.5.1</strong>).
    /// </summary>
    VBFileMode Mode { get; }

    /// <summary>
    /// What later statements may do with the channel. Implied by <see cref="Mode"/> when the <c>Open</c>
    /// declared no <c>Access</c> clause.
    /// </summary>
    VBFileAccessMode Access { get; }

    /// <summary>
    /// What the channel locks against other processes. <see cref="VBFileLockMode.Shared"/> when the
    /// <c>Open</c> declared no lock.
    /// </summary>
    VBFileLockMode Lock { get; }

    /// <summary>
    /// The <c>Len =</c> record length, or <c>0</c> when the <c>Open</c> declared none. Ignored for
    /// <see cref="VBFileMode.Binary"/>, which the specification says to disregard it for.
    /// </summary>
    int RecordLength { get; }

    /// <summary>
    /// The channel as a character-output target, for <c>Print #</c> and <c>Write #</c>.
    /// </summary>
    /// <remarks>
    /// The same <see cref="IRuntimeOutput"/> the session's own output is, so <strong>MS-VBAL
    /// §5.4.5.8</strong>'s output rules - print zones, the numeric space, <c>Spc</c>, <c>Tab</c>, a trailing
    /// <c>;</c> - are evaluated once and written wherever they are aimed. A channel counts its own line
    /// position, which is what those rules are relative to.
    /// <para>
    /// 👉 A caller is expected to have asked <see cref="FileStatementAccess"/> whether the statement is valid
    /// on this channel first. Writing to one opened for reading fails rather than corrupting it, but reporting
    /// <em>which</em> statement was wrong is the caller's job, not this one's.
    /// </para>
    /// </remarks>
    IRuntimeOutput Output { get; }
}

/// <summary>
/// The file numbers a session has open (<strong>MS-VBAL §5.4.5</strong>) — the shim every file statement
/// goes through.
/// </summary>
/// <remarks>
/// 🎯 A single seam on purpose, and not only because twelve statements share it. File I/O is the most
/// consequential thing a VBA program does to the machine it runs on, so it is the thing an administrator
/// most needs to be able to see, restrict, or redirect, and the thing a test most needs to be able to fake.
/// Every one of those is a matter of <em>which implementation the session was composed with</em> rather than
/// of anything the statements know.
/// <para>
/// The file <em>system</em> is already abstracted platform-wide (<c>System.IO.Abstractions</c>), so what this
/// adds is the part VBA has and a file system does not: numbered channels, the modes they were opened under,
/// and the rules about which statement may use which.
/// </para>
/// <para>
/// ⚖️<strong>RDCore</strong> provides an implementation of this interface <strong>licensed under GPLv3</strong>.
/// </para>
/// </remarks>
public interface IFileChannels
{
    /// <summary>
    /// The channels currently open, in no particular order.
    /// </summary>
    IEnumerable<IFileChannel> Open { get; }

    /// <summary>
    /// The channel <paramref name="fileNumber"/> is open on.
    /// </summary>
    /// <param name="fileNumber">The file number source referred to.</param>
    /// <param name="channel">The channel it is open on.</param>
    /// <returns><c>false</c> when that file number is not currently open.</returns>
    bool TryGet(int fileNumber, out IFileChannel? channel);

    /// <summary>
    /// Associates <paramref name="fileNumber"/> with <paramref name="path"/>
    /// (<strong>MS-VBAL §5.4.5.1</strong>).
    /// </summary>
    /// <remarks>
    /// Creates the external file when it does not exist, <em>unless</em> the mode is
    /// <see cref="VBFileMode.Input"/>, which the specification says is an error instead.
    /// </remarks>
    /// <param name="fileNumber">The file number to associate. Must not already be open.</param>
    /// <param name="path">The complete path specification.</param>
    /// <param name="mode">The mode, defaulted by the caller from the statement's own clauses.</param>
    /// <param name="access">The access, defaulted by the caller from <paramref name="mode"/>.</param>
    /// <param name="lock">The lock, <see cref="VBFileLockMode.Shared"/> when the statement declared none.</param>
    /// <param name="recordLength">The <c>Len =</c> record length, or <c>0</c>.</param>
    /// <returns>
    /// The error that stopped it, or <c>null</c> when the channel is open. The specification names which:
    /// <c>55</c> when the file number is already open, <c>53</c> when an <see cref="VBFileMode.Input"/> names
    /// no existing file, <c>55</c> when an <see cref="VBFileMode.Append"/>/<see cref="VBFileMode.Output"/>
    /// names a file another channel already has open, <c>70</c> when the requested lock cannot be had, and
    /// <c>75</c> when the file cannot be created.
    /// </returns>
    Model.Errors.VBRuntimeErrorId? TryOpen(
        int fileNumber, string path, VBFileMode mode, VBFileAccessMode access, VBFileLockMode @lock, int recordLength);

    /// <summary>
    /// Disassociates <paramref name="fileNumber"/>, closing the file
    /// (<strong>MS-VBAL §5.4.5.2</strong>).
    /// </summary>
    /// <param name="fileNumber">The file number to close.</param>
    /// <returns><c>false</c> when it was not open.</returns>
    bool Close(int fileNumber);

    /// <summary>
    /// Closes every open channel — what a <c>Close</c> with no file number, and a <c>Reset</c>, both do
    /// (<strong>MS-VBAL §5.4.5.2</strong>).
    /// </summary>
    /// <returns>How many were closed.</returns>
    int CloseAll();
}
