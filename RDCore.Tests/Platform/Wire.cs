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
    /// A payload the transport cannot write is never sent and never answered, and the platform waits for the answer: a harness that hands the handler its
    /// request as an object never finds that out.
    /// </remarks>
    public static T RoundTrip<T>(T payload)
        => LspSerializer.Instance.DeserializeObject<T>(LspSerializer.Instance.SerializeObject(payload));
}
