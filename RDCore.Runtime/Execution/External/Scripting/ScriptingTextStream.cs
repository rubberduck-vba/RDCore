using System.Text;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Runtime.Execution.External.Scripting;

/// <summary>
/// A <c>TextStream</c>: a text file read or written a run of characters at a time, as scrrun.dll's is, with its line and column counted as it goes. Each
/// counts from 1: a line feed begins a line, and every other character, a carriage return too, moves the column on.
/// </summary>
/// <remarks>
/// A stream for reading has the file's text, decoded once as it was opened; one for writing encodes what it is given into the file as it is given. A read of
/// a stream for writing, or a write of one for reading, is error 54; a read at the end of the text is 62; any call once the stream is closed is 91.
/// </remarks>
internal sealed class ScriptingTextStream
{
    private readonly string? text;
    private readonly Stream? output;
    private readonly Encoding? encoding;
    private int position;
    private bool closed;

    private ScriptingTextStream(string? text, Stream? output, Encoding? encoding)
    {
        this.text = text;
        this.output = output;
        this.encoding = encoding;
    }

    public static ScriptingTextStream Reading(string text) => new(text, null, null);

    // a stream that appends goes on from the line and the column of what the file has.
    public static ScriptingTextStream Writing(Stream output, Encoding encoding, string existing)
    {
        var stream = new ScriptingTextStream(null, output, encoding);
        stream.Advance(existing);
        return stream;
    }

    public int Line { get; private set; } = 1;

    public int Column { get; private set; } = 1;

    // what a call of the stream cannot do: anything once it is closed, a read of a stream for writing, a write of one for reading.
    public VBRuntimeErrorId? Refuses(bool reading)
        => closed ? VBRuntimeErrorId.ObjectVariableOrWithBlockVariableNotSet
            : reading != (text is not null) ? VBRuntimeErrorId.BadFileMode
            : null;

    public VBRuntimeErrorId? Refuses() => closed ? VBRuntimeErrorId.ObjectVariableOrWithBlockVariableNotSet : null;

    public bool AtEndOfStream => position >= text!.Length;

    // a line ends where ReadLine ends it: at a line feed, or at the carriage return of a CR LF.
    public bool AtEndOfLine => AtEndOfStream || text![position] == '\n' || text[position] == '\r' && position + 1 < text.Length && text[position + 1] == '\n';

    public VBRuntimeErrorId? Read(int count, out string read)
    {
        read = string.Empty;
        if (count <= 0)
        {
            // nothing to read is read, at the end of the text too.
            return count < 0 ? VBRuntimeErrorId.InvalidProcedureCallOrArgument : null;
        }

        if (AtEndOfStream)
        {
            return VBRuntimeErrorId.InputPastEndOfFile;
        }

        read = text!.Substring(position, Math.Min(count, text.Length - position));
        position += read.Length;
        Advance(read);
        return null;
    }

    // a line ends at a line feed, and a carriage return before it is not in the line; one alone is.
    public VBRuntimeErrorId? ReadLine(out string line)
    {
        line = string.Empty;
        if (AtEndOfStream)
        {
            return VBRuntimeErrorId.InputPastEndOfFile;
        }

        var feed = text!.IndexOf('\n', position);
        if (feed < 0)
        {
            line = text[position..];
            position = text.Length;
            Column += line.Length;
            return null;
        }

        line = text[position..(feed > position && text[feed - 1] == '\r' ? feed - 1 : feed)];
        position = feed + 1;
        Line++;
        Column = 1;
        return null;
    }

    public VBRuntimeErrorId? ReadAll(out string rest) => Read(int.MaxValue, out rest);

    // a write the file system refuses (a full disk, a lost device) is error 70, as scrrun.dll's.
    public VBRuntimeErrorId? Write(string written)
    {
        try
        {
            output!.Write(encoding!.GetBytes(written));
        }
        catch (IOException)
        {
            return VBRuntimeErrorId.PermissionDenied;
        }

        Advance(written);
        return null;
    }

    public void Close()
    {
        if (!closed)
        {
            closed = true;
            output?.Dispose();
        }
    }

    private void Advance(string passed)
    {
        foreach (var character in passed)
        {
            if (character == '\n')
            {
                Line++;
                Column = 1;
            }
            else
            {
                Column++;
            }
        }
    }
}
