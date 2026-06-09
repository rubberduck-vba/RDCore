namespace RDCore.SDK.Client;

/// <summary>
/// This exception is thrown when the application is misconfigured and cannot be started.
/// </summary>
/// <param name="message">The message of this exception.</param>
/// <param name="verbose">The verbose message for this exception.</param>
/// <param name="inner">An <em>inner exception</em>, if there is one.</param>
public class BadConfigurationException(string message, string? verbose, Exception? inner = default) : SdkException(message, verbose, inner) { }
