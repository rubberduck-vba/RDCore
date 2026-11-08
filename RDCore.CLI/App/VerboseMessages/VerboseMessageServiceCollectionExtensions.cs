using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace RDCore.CLI.App.VerboseMessages;

/// <summary>
/// Registers the services that format the details of a run-time error as the console's verbose text.
/// </summary>
public static class VerboseMessageServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="IVerboseMessageBuilder"/> and the formatters it composes.
    /// </summary>
    /// <remarks>
    /// The runtime records the details of an error as data; how they read is the console's to say. The options fall back to the record's own defaults, so a
    /// console that says nothing about them still gets a working builder.
    /// </remarks>
    /// <param name="services">The service collection to register into.</param>
    public static IServiceCollection AddVerboseMessages(this IServiceCollection services) => services
        .AddSingleton(provider => provider.GetRequiredService<IOptions<VerboseMessageOptions>>().Value)
        .AddSingleton<IExpressionInfoFormatter, ExpressionInfoBuilder>()
        .AddSingleton<IExpressionInfoFormatterProvider, ExpressionInfoFormatterProvider>()
        .AddSingleton<IStackTraceFormatter, DefaultStackTraceFormatter>()
        .AddSingleton<IVerboseMessageBuilder, VerboseMessageBuilder>();
}
