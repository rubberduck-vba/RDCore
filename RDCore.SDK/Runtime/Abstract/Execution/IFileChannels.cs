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
    IFileChannelOutput Output { get; }

    /// <summary>
    /// The channel as a character-input source, for <c>Line Input #</c> and <c>Input #</c>.
    /// </summary>
    /// <remarks>
    /// Reads at the same <em>file-pointer-position</em> <see cref="Output"/> writes at: <strong>MS-VBAL
    /// §5.4.5</strong> gives a channel one position and not one per direction, which is what makes an
    /// <c>Append</c> channel able to read back what it appended.
    /// <para>
    /// 👉 As with <see cref="Output"/>, a caller is expected to have asked <see cref="FileStatementAccess"/>
    /// whether the statement is valid on this channel first.
    /// </para>
    /// </remarks>
    IFileChannelInput Input { get; }

    /// <summary>
    /// The current <em>file-pointer-position</em>, one-based — counted in <em>records</em> when the channel
    /// was opened <see cref="VBFileMode.Random"/> and in bytes otherwise (<strong>MS-VBAL §5.4.5.3</strong>).
    /// </summary>
    long Position { get; }

    /// <summary>
    /// Repositions the channel so the next operation happens at <paramref name="position"/>
    /// (<strong>MS-VBAL §5.4.5.3</strong>).
    /// </summary>
    /// <remarks>
    /// A position past the end of the file extends it — "the extended content of the file is implementation
    /// defined and can be undefined" — except on a channel whose access is
    /// <see cref="VBFileAccessMode.Read"/>, which the specification exempts.
    /// </remarks>
    /// <param name="position">The new position, in the same units <see cref="Position"/> is counted in.</param>
    /// <returns>
    /// The error that stopped it, or <c>null</c>. A position of <c>0</c> or less is one — the specification
    /// says "an error is raised" without naming it, and MS-VBA raises <c>63</c>, <c>Bad record number</c>.
    /// </returns>
    Model.Errors.VBRuntimeErrorId? Seek(long position);
}

/// <summary>
/// The writing side of an <see cref="IFileChannel"/> — an <see cref="IRuntimeOutput"/> that also has the
/// <em>maximum line length</em> a <c>Width</c> statement sets (<strong>MS-VBAL §5.4.5.7</strong>).
/// </summary>
/// <remarks>
/// The only thing a file's output has that the session's own output does not, and the reason it is here
/// rather than on <see cref="IRuntimeOutput"/>: a line length is a property of a <em>file</em> being written,
/// and the <c>Immediate</c> window has no such limit to set.
/// </remarks>
public interface IFileChannelOutput : IRuntimeOutput
{
    /// <summary>
    /// The most characters a line of this file may hold, or <c>0</c> for no maximum — which is what a channel
    /// has until a <c>Width</c> statement says otherwise, and what <c>Width #n, 0</c> returns it to.
    /// </summary>
    /// <remarks>
    /// Reaching it while writing "immediately" writes the line termination sequence and continues on the next
    /// line (<strong>§5.4.5.8</strong>), so it wraps output rather than truncating it.
    /// </remarks>
    int MaxLineLength { get; set; }
}

/// <summary>
/// The reading side of an <see cref="IFileChannel"/> — the characters at and after its current
/// <em>file-pointer-position</em> (<strong>MS-VBAL §5.4.5</strong>).
/// </summary>
/// <remarks>
/// Character-mode reading, which is what <c>Line Input #</c> and <c>Input #</c> do: the specification
/// describes both as consuming bytes that are "converted in an implementation dependent manner" into data
/// values, so the bytes-to-characters step belongs to the channel — the only thing that knows its own
/// encoding — and the statements above it see characters.
/// <para>
/// 🚧 Binary- and random-mode reading (<c>Get</c>) addresses <em>records</em> rather than characters, and
/// wants a surface of its own beside this one.
/// </para>
/// </remarks>
public interface IFileChannelInput
{
    /// <summary>
    /// Whether there are no characters at or after the current <em>file-pointer-position</em> — which is
    /// what <c>EOF</c> reports, and what makes a character-mode read raise error <c>62</c>.
    /// </summary>
    bool IsEndOfFile { get; }

    /// <summary>
    /// The character at the current <em>file-pointer-position</em>, without consuming it.
    /// </summary>
    /// <returns>The character, or <c>-1</c> at end of file.</returns>
    int Peek();

    /// <summary>
    /// The character at the current <em>file-pointer-position</em>, advancing past it.
    /// </summary>
    /// <returns>The character, or <c>-1</c> at end of file.</returns>
    int Read();

    /// <summary>
    /// Reads from the current <em>file-pointer-position</em> through the end of the current line
    /// (<strong>MS-VBAL §5.4.5.6</strong>), leaving the position after the line termination sequence.
    /// </summary>
    /// <remarks>
    /// The line termination sequence is not part of the result. A line ended by the end of the file rather
    /// than by a terminator still reads as a line — the specification says so outright — so an empty string
    /// and <c>null</c> mean different things here.
    /// </remarks>
    /// <returns>The line, or <c>null</c> when <see cref="IsEndOfFile"/> already was <c>true</c>.</returns>
    string? ReadLine();
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
