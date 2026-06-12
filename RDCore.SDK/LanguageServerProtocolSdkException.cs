namespace RDCore.SDK
{
    /// <summary>
    /// Something went wrong in the LSP client/server configuration layer.
    /// </summary>
    /// <param name="message"></param>
    /// <param name="verbose"></param>
    /// <param name="inner"></param>
    public class LanguageServerProtocolSdkException(string message, string? verbose = default, Exception? inner = default): SdkException(message, verbose, inner) { }
}