namespace RDCore.SDK.Client;

/// <summary>
/// This exception is thrown when a LSP client application cannot locate the LSP server executable as configured.
/// </summary>
/// <param name="Path">The <c>Path</c> where the LSP language server application was expected to be found.</param>
public class LanguageServerNotFoundException(string Path) 
    : BadConfigurationException(Exceptions.LanguageServerNotFoundException_Message, Exceptions.LanguageServerNotFoundException_Verbose) 
{
    /// <summary>
    /// Gets the <c>Path</c> where the LSP language server application was expected to be found.
    /// </summary>
    public string Path { get; } = Path;
}

/// <summary>
/// This exception is thrown when a LSP client application attempts to launch an already-running language server.
/// </summary>
/// <param name="ProcessId"></param>
public class LanguageServerAlreadyRunningException(int ProcessId)
    : SdkException(Exceptions.LanguageServerAlreadyRunningException_Message, Exceptions.LanguageServerAlreadyRunningException_Verbose)
{
    /// <summary>
    /// Gets the PID of the already-running language server application.
    /// </summary>
    /// <remarks>
    /// Client should then <c>kill</c> it and start a new server instance, as needed or applicable.
    /// </remarks>
    public int ProcessId { get; } = ProcessId;
}