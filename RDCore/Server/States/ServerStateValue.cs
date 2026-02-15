namespace RDCore.Server.States;

public enum ServerStateValue
{
    /// <summary>
    /// Server process has started but the language server has not yet been initialized.
    /// </summary>
    Starting,
    /// <summary>
    /// Server has received the initialize request and is in the process of initializing the language server, but has not yet completed initialization.
    /// </summary>
    Initializing,
    /// <summary>
    /// Language server is running and ready to handle client requests.
    /// </summary>
    Running,
    /// <summary>
    /// Language server is running and ready to handle client requests with trace-level logs enabled.
    /// </summary>
    RunningVerbose,
    /// <summary>
    /// Language server is running and ready to handle client requests with tracing disabled.
    /// </summary>
    RunningTraceless,
    /// <summary>
    /// Server has received a shutdown request and is in the process of cleanly shutting down the language server.
    /// </summary>
    ShuttingDown,
    /// <summary>
    /// Server has received an exit request and its process is about to terminate.
    /// </summary>
    Exiting,
}
