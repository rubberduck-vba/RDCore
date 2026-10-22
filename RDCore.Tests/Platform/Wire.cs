using OmniSharp.Extensions.LanguageServer.Protocol.Serialization;

namespace RDCore.Tests.Platform;

/// <summary>
/// The JSON-RPC hop a request makes between two processes of the platform, without the processes.
/// </summary>
internal static class Wire
{
    /// <summary>
    /// What the other end reads: <paramref name="payload"/> as the transport's own (<c>Newtonsoft.Json</c>) serializer writes it and reads it back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A payload the transport cannot write is never sent and never answered, and the platform waits for the answer: a harness that hands the handler its
    /// request as an object never finds that out.
    /// </para>
    /// <para>
    /// A <see cref="Uri"/> is written as the string it was made of, and read back as relative or absolute: one made of a path (<c>/tmp/ws</c>) is read
    /// back relative on Linux. The platform's own are <c>file:</c> URIs (<see cref="FileUri"/>), which are absolute on any platform.
    /// </para>
    /// </remarks>
    public static T RoundTrip<T>(T payload)
        => LspSerializer.Instance.DeserializeObject<T>(LspSerializer.Instance.SerializeObject(payload));

    /// <summary>
    /// The <c>file:</c> URI of a path, written as one: what a client sends the platform as its workspace root.
    /// </summary>
    public static Uri FileUri(string path) => new(new Uri(path).AbsoluteUri);
}
