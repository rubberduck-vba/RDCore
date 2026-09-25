using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace RDCore.SDK.Services.VerboseMessages;

/// <summary>
/// Registers the services that assemble the verbose half of an RDCore diagnostic or run-time error
/// message.
/// </summary>
public static class VerboseMessageServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="IVerboseMessageBuilder"/> and the formatters it composes.
    /// </summary>
    /// <remarks>
    /// Any component that runs VBA needs these, because a run-time error carries a verbose message
    /// whether or not anything ends up displaying it. The options bind from
    /// <c>Configuration:VerboseMessages</c> and fall back to the record's own defaults, so a host
    /// that says nothing about them still gets a working builder.
    /// </remarks>
    /// <param name="services">The service collection to register into.</param>
    public static IServiceCollection AddVerboseMessages(this IServiceCollection services) => services
        .AddSingleton(provider => provider.GetRequiredService<IOptions<VerboseMessageOptions>>().Value)
        .AddSingleton<IExpressionInfoFormatter, ExpressionInfoBuilder>()
        .AddSingleton<IExpressionInfoFormatterProvider, ExpressionInfoFormatterProvider>()
        .AddSingleton<IStackTraceFormatter, DefaultStackTraceFormatter>()
        .AddSingleton<IVerboseMessageBuilder, VerboseMessageBuilder>();
}
