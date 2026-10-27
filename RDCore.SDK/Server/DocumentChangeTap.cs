using System.Text;
using System.Text.Json;

namespace RDCore.SDK.Server;

/// <summary>
/// Reads a client's messages as they pass, and says to <see cref="IncomingDocumentChanges"/> which of them change a document: a stream that is the one it
/// wraps, read for read.
/// </summary>
/// <remarks>
/// The messages are framed the way <strong>LSP 3.17</strong> §Base Protocol has it: a <c>Content-Length</c> header, an empty line, and that many bytes of
/// JSON. Only a message that names a document notification is parsed. A message the tap can't make out is passed on all the same, and only goes unseen: the
/// worst that comes of it is a request that waits for the change, as it would without the tap.
/// </remarks>
internal sealed class DocumentChangeTap(Stream inner, IncomingDocumentChanges changes) : Stream
{
    private static readonly byte[] HeaderEnd = "\r\n\r\n"u8.ToArray();
    private static readonly byte[] DocumentNotification = "\"textDocument/did"u8.ToArray();
    private static readonly HashSet<string> Changes = ["textDocument/didOpen", "textDocument/didChange", "textDocument/didClose"];

    // what has been read of the message being read: its header, then its body.
    private readonly MemoryStream _message = new();
    private int _bodyLength = -1;

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = inner.Read(buffer, offset, count);
        Observe(buffer.AsSpan(offset, read));
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        var read = inner.Read(buffer);
        Observe(buffer[..read]);
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer, cancellationToken);
        Observe(buffer.Span[..read]);
        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => await ReadAsync(buffer.AsMemory(offset, count), cancellationToken);

    private void Observe(ReadOnlySpan<byte> read)
    {
        while (!read.IsEmpty)
        {
            if (_bodyLength < 0)
            {
                // the header: up to the empty line, which ends it.
                var before = (int)_message.Length;
                _message.Write(read);
                var header = _message.GetBuffer().AsSpan(0, (int)_message.Length);
                var end = header.IndexOf(HeaderEnd);
                if (end < 0)
                {
                    if (_message.Length > 4096)
                    {
                        // no header is that long: what is being read is not a message the tap can make out, and it starts over.
                        _message.SetLength(0);
                    }
                    return;
                }

                _bodyLength = ContentLength(header[..end]);
                var bodyStart = end + HeaderEnd.Length;
                var consumed = bodyStart - before;
                _message.SetLength(0);
                read = read[consumed..];
                if (_bodyLength < 0)
                {
                    continue;
                }
            }

            var take = Math.Min(read.Length, _bodyLength - (int)_message.Length);
            _message.Write(read[..take]);
            read = read[take..];
            if (_message.Length == _bodyLength)
            {
                Seen(_message.GetBuffer().AsSpan(0, _bodyLength));
                _message.SetLength(0);
                _bodyLength = -1;
            }
        }
    }

    private static int ContentLength(ReadOnlySpan<byte> header)
    {
        foreach (var line in Encoding.ASCII.GetString(header).Split("\r\n"))
        {
            var colon = line.IndexOf(':');
            if (colon > 0 && line[..colon].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line[(colon + 1)..].Trim(), out var length) && length >= 0)
            {
                return length;
            }
        }

        return -1;
    }

    // a notification that changes a document says which, as it arrives.
    private void Seen(ReadOnlySpan<byte> body)
    {
        if (body.IndexOf(DocumentNotification) < 0)
        {
            return;
        }

        try
        {
            using var message = JsonDocument.Parse(body.ToArray());
            var root = message.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && !root.TryGetProperty("id", out _)
                && root.TryGetProperty("method", out var method) && method.GetString() is { } name && Changes.Contains(name)
                && root.TryGetProperty("params", out var parameters) && parameters.ValueKind == JsonValueKind.Object
                && parameters.TryGetProperty("textDocument", out var document) && document.ValueKind == JsonValueKind.Object
                && document.TryGetProperty("uri", out var uri) && uri.GetString() is { } text
                && IncomingDocumentChanges.DocumentOf(text) is { } documentUri)
            {
                changes.Arrived(documentUri);
            }
        }
        catch (JsonException)
        {
            // not a message the tap can read: the server reads it, and says what's wrong with it.
        }
    }

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => inner.Flush();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _message.Dispose();
        }
        base.Dispose(disposing);
    }
}
