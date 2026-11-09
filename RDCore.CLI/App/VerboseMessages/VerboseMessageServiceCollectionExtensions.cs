using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace RDCore.CLI.App.VerboseMessages;

public static class VerboseMessageServiceCollectionExtensions
{
    public static IServiceCollection AddVerboseMessages(this IServiceCollection services) => services
        .AddSingleton(provider => provider.GetRequiredService<IOptions<VerboseMessageOptions>>().Value)
        .AddSingleton<IExpressionInfoFormatter, ExpressionInfoBuilder>()
        .AddSingleton<IExpressionInfoFormatterProvider, ExpressionInfoFormatterProvider>()
        .AddSingleton<IStackTraceFormatter, DefaultStackTraceFormatter>()
        .AddSingleton<IVerboseMessageBuilder, VerboseMessageBuilder>();
}
