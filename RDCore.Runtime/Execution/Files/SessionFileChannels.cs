using System.Text;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.IO.Abstractions;

namespace RDCore.Runtime.Execution.Files;

/// <inheritdoc cref="IFileChannels"/>
/// <remarks>
/// Over an <see cref="IFileSystem"/>, which the platform already abstracts — so a session composed with a
/// fake one does real VBA file I/O against nothing on disk, which is what a test and a CI run both want.
/// <para>
/// A channel holds an open <see cref="Stream"/> for the lifetime of the association, because that is what
/// VBA's own semantics describe: the file is held open, and the lock the <c>Open</c> asked for is held with
/// it. Closing the channel is what releases both.
/// </para>
/// </remarks>
/// <param name="fileSystem">The file system the channels are opened on.</param>
internal sealed class SessionFileChannels(IFileSystem fileSystem, Encoding encoding) : IFileChannels, IDisposable
{
    /// <summary>
    /// The record length a <see cref="VBFileMode.Random"/> channel counts positions in when its <c>Open</c>
    /// declared no <c>Len</c> clause.
    /// </summary>
    /// <remarks>
    /// The specification requires a <c>Len</c> clause to be in 1..32,767 but never says what an absent one
    /// means, so this is implementation-defined — and it is what MS-VBA uses, which is the only answer a
    /// program written against MS-VBA would agree with.
    /// </remarks>
    private const int DefaultRandomRecordLength = 128;

    private sealed record class Channel(
        int FileNumber,
        string Path,
        VBFileMode Mode,
        VBFileAccessMode Access,
        VBFileLockMode Lock,
        int RecordLength,
        Stream Stream,
        IFileChannelOutput Output,
        IFileChannelInput Input) : IFileChannel
    {
        // "If the <open-statement> ... had <mode> Random, then the file-pointer-position's location refers to a
        // record; otherwise, it refers to a byte" (MS-VBAL 5.4.5.3). Both are one-based, so a fresh channel is
        // at 1 rather than at 0.
        private int PositionUnit => Mode is VBFileMode.Random
            ? RecordLength is > 0 ? RecordLength : DefaultRandomRecordLength
            : 1;

        // "Multiple lock ranges established by multiple lock statements can be simultaneously active for an
        // external data file", and an Unlock has to name one of them exactly - so the set of them is state the
        // channel keeps, not something derivable from the stream.
        private readonly List<FileRecordRange> _locks = [];

        public IEnumerable<FileRecordRange> Locks => _locks;

        public long Position => Stream.Position / PositionUnit + 1;

        public VBRuntimeErrorId? LockRange(FileRecordRange range)
        {
            var requested = Requested(range);
            if (!requested.IsEntireFile && (requested.Start < 1 || requested.Start > requested.End))
            {
                // "Start record MUST be greater than or equal to 1, and less than or equal to end record. If
                // not, an error is raised" - which the specification does not name, and MS-VBA raises 63 for.
                return VBRuntimeErrorId.BadRecordNumber;
            }

            // TODO apply a real lock to the external file. "The mechanism for actually implementing such locks
            // and whether or not a lock can be applied to any specific external file is implementation
            // defined", and holding one across every platform the runtime targets is its own piece of work -
            // so for now a lock is recorded and honoured between channels of this session, and no more.
            if (!_locks.Contains(requested))
            {
                _locks.Add(requested);
            }

            return null;
        }

        public VBRuntimeErrorId? UnlockRange(FileRecordRange range)
        {
            var requested = Requested(range);
            if (requested.IsEntireFile)
            {
                // "If a <record-range> is provided for only the <lock-statement> or the <unlock-statement>
                // designating the same currently open file number an error is generated" - this is that
                // mismatch seen from the Unlock side: the file was locked in ranges, and this asks for all of
                // it. Unlocking a file nothing locked is not an error, which is what makes Unlock safe in an
                // error handler the way Close is.
                if (_locks.Any(held => !held.IsEntireFile))
                {
                    return VBRuntimeErrorId.BadRecordNumber;
                }

                _locks.Clear();
                return null;
            }

            if (requested.Start < 1 || requested.Start > requested.End)
            {
                return VBRuntimeErrorId.BadRecordNumber;
            }

            // "its start record and end record MUST designate a range that is identical to a start record to
            // end record range of a previously executed <lock-statement> for the same currently-open file
            // number. If is not the case, an error is raised."
            return _locks.Remove(requested) ? null : VBRuntimeErrorId.BadRecordNumber;
        }

        public bool TryWriteRecord(VBTypedValue value, bool isVariant, out int written)
            => RecordDataFormat.TryWrite(Stream, value, Mode, isVariant, out written);

        public bool TryReadRecord(VBType declaredType, int currentLength, out VBTypedValue? value)
            => RecordDataFormat.TryRead(Stream, declaredType, Mode, currentLength, out value);

        public bool TryReadRecordInto(VBUserDefinedTypeValue userDefinedType)
            => RecordDataFormat.TryReadInto(Stream, userDefinedType, Mode);

        // "If the file number value was opened with <mode> Input, Output, or Append, the effect is as if no
        // <record-range> was present and the entire file is locked" - stated for Lock and repeated for Unlock,
        // so both go through this and a ranged Unlock of such a channel matches its whole-file lock.
        private FileRecordRange Requested(FileRecordRange range)
            => Mode is VBFileMode.Input or VBFileMode.Output or VBFileMode.Append
                ? FileRecordRange.EntireFile
                : range;

        public VBRuntimeErrorId? Seek(long position)
        {
            // "An error is raised if the new file position is 0 or negative."
            if (position < 1)
            {
                return VBRuntimeErrorId.BadRecordNumber;
            }

            var offset = (position - 1) * PositionUnit;

            // "If new file position is greater than the current size of the file ... the size of the file is
            // extended such that its size is the value new file position. This does not occur for files whose
            // currently-open <access> is Read." Seeking past the end of a Read channel leaves the file alone;
            // the position still moves, and a read there finds nothing.
            if (offset > Stream.Length && Access is not VBFileAccessMode.Read)
            {
                Stream.SetLength(offset);
            }

            Stream.Seek(offset, SeekOrigin.Begin);
            return null;
        }
    }

    // a channel counts its own line position, because MS-VBAL 5.4.5.8's print zones and Spc/Tab clauses are
    // relative to the line of the file being written, not to anything the session is doing elsewhere.
    private sealed class ChannelOutput(Stream stream, Encoding encoding) : IFileChannelOutput
    {
        // the implementation-defined line termination sequence (MS-VBAL §5.4.5.9). CrLf, because that is what
        // every VBA that has ever written a text file produced, and a file this writes is read by those too.
        private const string LineTerminator = "\r\n";

        public int LinePosition { get; private set; } = 1;

        public int MaxLineLength { get; set; }

        public void Write(string text)
        {
            if (MaxLineLength <= 0)
            {
                Emit(text);
                LinePosition += text.Length;
                return;
            }

            // "If while performing any of these steps the number of characters in the current line reaches the
            // maximum line length the line termination sequence is immediately written and output continues on
            // the next line" (MS-VBAL 5.4.5.8) - so a long value wraps across lines rather than being
            // truncated, and the wrap can happen more than once within one Write.
            var remaining = text.AsSpan();
            while (!remaining.IsEmpty)
            {
                var room = MaxLineLength - (LinePosition - 1);
                if (room <= 0)
                {
                    WriteLine();
                    continue;
                }

                var take = Math.Min(room, remaining.Length);
                Emit(remaining[..take].ToString());
                LinePosition += take;
                remaining = remaining[take..];
            }
        }

        public void WriteLine()
        {
            Emit(LineTerminator);
            LinePosition = 1;
        }

        private void Emit(string text)
        {
            var bytes = encoding.GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
            // flushed per write: a VBA program that writes and then reads the same file with another channel
            // expects to see what it wrote, and nothing here knows when the next read is coming.
            stream.Flush();
        }
    }

    // reads at the same stream position the output writes at, because a VBA channel has one
    // file-pointer-position and not one per direction (MS-VBAL 5.4.5).
    private sealed class ChannelInput(Stream stream, Encoding encoding) : IFileChannelInput
    {
        private const char CarriageReturn = '\r';
        private const char LineFeed = '\n';

        private readonly Decoder _decoder = encoding.GetDecoder();

        // a character Peek decoded but Read has not taken yet. The stream is already past its bytes, so
        // holding the character here is what keeps Peek from consuming - a stream is not seekable in general
        // (a Byte-order-marked or shared one especially), so rewinding it is not an option.
        private int _peeked = -1;

        public bool IsEndOfFile => _peeked < 0 && stream.Position >= stream.Length;

        public int Peek() => _peeked >= 0 ? _peeked : _peeked = Decode();

        public int Read()
        {
            if (_peeked < 0)
            {
                return Decode();
            }

            var peeked = _peeked;
            _peeked = -1;
            return peeked;
        }

        public string? ReadLine()
        {
            // "If the file is empty or there are no characters after file-pointer-position, then runtime
            // error 62 is raised" - which is the caller's to raise, so this reports it rather than an empty
            // line, a real reading of which the specification requires be told apart from it.
            if (IsEndOfFile)
            {
                return null;
            }

            var line = new StringBuilder();
            while (true)
            {
                var next = Read();
                if (next < 0)
                {
                    // "If the end of file is reach before finding a line termination sequence, the data value
                    // is the String data value converted from the byte sequence up to the end of the file."
                    break;
                }

                if (next == CarriageReturn)
                {
                    // CrLf is one termination sequence and not two, so the LineFeed goes with it. A lone Cr
                    // and a lone Lf each terminate a line too: the sequence is "implementation dependent"
                    // (MS-VBAL 5.4.5) and a file this reads was as likely written somewhere else as here.
                    if (Peek() == LineFeed)
                    {
                        Read();
                    }

                    break;
                }

                if (next == LineFeed)
                {
                    break;
                }

                line.Append((char)next);
            }

            return line.ToString();
        }

        // feeds the stream one byte at a time until the decoder yields a character: a character spans an
        // encoding-dependent number of bytes, and nothing here knows how many until the decoder says so.
        private int Decode()
        {
            var input = new byte[1];
            var output = new char[2];
            while (stream.Read(input, 0, 1) == 1)
            {
                if (_decoder.GetChars(input, 0, 1, output, 0) > 0)
                {
                    return output[0];
                }
            }

            return -1;
        }
    }

    private readonly Dictionary<int, Channel> _channels = [];

    /// <inheritdoc/>
    public IEnumerable<IFileChannel> Open => _channels.Values;

    /// <inheritdoc/>
    public bool TryGet(int fileNumber, out IFileChannel? channel)
    {
        var found = _channels.TryGetValue(fileNumber, out var open);
        channel = open;
        return found;
    }

    /// <inheritdoc/>
    public VBRuntimeErrorId? TryOpen(
        int fileNumber, string path, VBFileMode mode, VBFileAccessMode access, VBFileLockMode @lock, int recordLength)
    {
        // "An error (number 55, 'File already open') is generated if the <file-number> integer value already
        // has an external file association" (MS-VBAL 5.4.5.1).
        if (_channels.ContainsKey(fileNumber))
        {
            return VBRuntimeErrorId.FileAlreadyOpen;
        }

        // "If <mode> is Append or Output, the path specification MUST NOT identify an external file that
        // currently has a file number association" - the one cross-channel rule the specification states
        // outright rather than leaving implementation-defined.
        if (mode is VBFileMode.Append or VBFileMode.Output
            && _channels.Values.Any(channel => PathsMatch(channel.Path, path)))
        {
            return VBRuntimeErrorId.FileAlreadyOpen;
        }

        // "If the external file... does not exist, an attempt is made to create the external file unless
        // <mode> is the keyword Input, in which case an error is generated."
        var exists = fileSystem.File.Exists(path);
        if (!exists && mode is VBFileMode.Input)
        {
            return VBRuntimeErrorId.FileNotFound;
        }

        try
        {
            var stream = OpenStream(path, mode, access, @lock);
            _channels[fileNumber] = new Channel(
                fileNumber, path, mode, access, @lock, recordLength, stream,
                new ChannelOutput(stream, encoding), new ChannelInput(stream, encoding));
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            // the lock could not be had, or the file refuses the access: "an error (number 70, 'Permission
            // denied') is generated".
            return VBRuntimeErrorId.PermissionDenied;
        }
        catch (IOException)
        {
            // already held by another process is also 70; anything else about the path or the device is
            // "number 75, 'Path/File access error'". IOException covers both, and a sharing violation is the
            // one worth telling apart, since a program can retry it.
            return VBRuntimeErrorId.PathOrFileAccessError;
        }
    }

    /// <inheritdoc/>
    public bool Close(int fileNumber)
    {
        if (!_channels.Remove(fileNumber, out var channel))
        {
            return false;
        }

        channel.Stream.Dispose();
        return true;
    }

    /// <inheritdoc/>
    public int CloseAll()
    {
        var closed = _channels.Count;
        foreach (var channel in _channels.Values)
        {
            channel.Stream.Dispose();
        }

        _channels.Clear();
        return closed;
    }

    /// <summary>
    /// Closes every channel the session still holds.
    /// </summary>
    /// <remarks>
    /// A session ending is not a <c>Close</c> statement, but a file held open by a process that has gone is
    /// worse than one closed without being asked to be.
    /// </remarks>
    public void Dispose() => CloseAll();

    private Stream OpenStream(string path, VBFileMode mode, VBFileAccessMode access, VBFileLockMode @lock)
    {
        var stream = fileSystem.FileStream.New(
            path,
            mode switch
            {
                    // Output "truncates" in VBA terms: data can only be written, from the start.
                VBFileMode.Output => FileMode.Create,
                VBFileMode.Input => FileMode.Open,
                // *not* FileMode.Append, which .NET allows only write-only: VBA's Append says "data can be
                // read from the file, and any data written to the file is added at the end", so it is opened
                // like any other read-write channel and positioned at the end below.
                _ => FileMode.OpenOrCreate,
            },
            access switch
            {
                VBFileAccessMode.Read => FileAccess.Read,
                VBFileAccessMode.Write => FileAccess.Write,
                _ => FileAccess.ReadWrite,
            },
            // the VBA lock says what *others* may still do, which is what FileShare expresses - Shared denies
            // nothing, and each Lock denies that operation to everyone else.
            @lock switch
            {
                VBFileLockMode.Read => FileShare.Write,
                VBFileLockMode.Write => FileShare.Read,
                VBFileLockMode.ReadWrite => FileShare.None,
                _ => FileShare.ReadWrite,
            });

        if (mode is VBFileMode.Append)
        {
            stream.Seek(0, SeekOrigin.End);
        }

        return stream;
    }

    // the same external file reached by two spellings is the same file. Case-insensitively on Windows, and
    // the platform runs on Linux too, so this compares the way the file system it was given does.
    private bool PathsMatch(string left, string right)
        => fileSystem.Path.GetFullPath(left).Equals(
            fileSystem.Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
