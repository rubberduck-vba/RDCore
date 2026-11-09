using Microsoft.Extensions.Logging;

namespace RDCore.SDK.Server.Logging;

/// <summary>
/// The logging of the <c>OmniSharp</c> language server's container, for what it sends before its client is initialized.
/// </summary>
/// <remarks>
/// A server sends nothing but a few notifications before the client is initialized (<strong>LSP 3.17</strong> §Initialize Request). OmniSharp holds the rest -
/// the <c>workspace/configuration</c> request it makes as it starts, a registration, a progress - and sends it once it has started, and warns of each message it
/// holds. Nothing is lost: the warning says only that a message waited, and it is written to every component's log at every start.
/// </remarks>
internal static class DeferredOutputLogging
{
    /// <summary>
    /// The category OmniSharp warns under, of a message it holds until the client is initialized.
    /// </summary>
    public const string Category = "OmniSharp.Extensions.LanguageServer.Server.LspServerOutputFilter";

    /// <summary>
    /// Logs only the errors of the category that warns of a message held until the client is initialized (<see cref="Category"/>).
    /// </summary>
    /// <remarks>
    /// A component's file log (<c>AddFile</c>) comes with a rule of its own for every category, and a rule of a provider is chosen over one that names only the
    /// category: the file log is given the category's rule as well.
    /// </remarks>
    /// <param name="builder">The logging of the language server's container.</param>
    /// <returns>The same builder.</returns>
    public static ILoggingBuilder WithoutDeferredOutputWarnings(this ILoggingBuilder builder)
        => builder
            .AddFilter(Category, LogLevel.Error)
            .AddFilter<Serilog.Extensions.Logging.SerilogLoggerProvider>(Category, LogLevel.Error);
}
