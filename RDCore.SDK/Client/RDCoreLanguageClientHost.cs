using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RDCore.SDK.Server;

namespace RDCore.SDK.Client;

/// <summary>
/// Simplifies implementing a <c>RDCore</c> <em>LSP client</em> application.
/// </summary>
/// <remarks>
/// 🧩 <c>override</c> templated methods to customize your application.<br/>
/// <list type="bullet">
/// <item>Implement <see cref="AppHost{TApp}.Configure(IConfiguration, Microsoft.Extensions.DependencyInjection.IServiceCollection, string[])"/> to override the default <see cref="IConfiguration"/> providers.</item>
/// <item>Implement <see cref="AppHost{TApp}.ConfigureExternalLogging(Microsoft.Extensions.DependencyInjection.IServiceCollection, ILoggingBuilder, IConfiguration)(Microsoft.Extensions.DependencyInjection.IServiceCollection, ILoggingBuilder, IConfiguration)"/> to override the default <see cref="ILoggingBuilder"/> providers.</item>
/// </list>
/// </remarks>
/// <typeparam name="TApp">A specific class type implementing <see cref="IRDCoreClientApp"/>.</typeparam>
public abstract class RDCoreLanguageClientHost<TApp>() : AppHost<TApp>()
    where TApp : class, IRDCoreClientApp
{
}
