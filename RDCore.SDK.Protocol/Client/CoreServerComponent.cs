namespace RDCore.SDK.Client;

/// <summary>
/// Which component of the RDCore platform an application is: what a client says it expects to be connected to as it initializes the platform
/// (<c>rdcore/platform/initialize</c>), and what the component says it is.
/// </summary>
public enum CoreServerComponent
{
    /// <summary>
    /// Application is a RD-VBA runtime environment host component.
    /// </summary>
    EnvironmentHost,
    /// <summary>
    /// Application is a RDCore platform orchestration and RD-VBA language server.
    /// </summary>
    LanguageServer,
    /// <summary>
    /// Application is a parsing server component.
    /// </summary>
    ParsingServer,
    /// <summary>
    /// Application is a platform extension server component.
    /// </summary>
    Extension,
    /// <summary>
    /// Application is the external host of an environment host: <c>rdc.exe</c> in external mode, the process that makes a program's calls to the outside world.
    /// </summary>
    ExternalHost,
    /// <summary>
    /// Application is a LSP client.
    /// </summary>
    /// <remarks>
    /// This application type cannot be started from a server app.
    /// </remarks>
    ClientApp,
}
