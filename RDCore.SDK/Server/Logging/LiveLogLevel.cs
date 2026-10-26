using Microsoft.Extensions.Logging;

namespace RDCore.SDK.Server.Logging;

/// <summary>
/// The least level of a message a component's logs write, as it is now: <see cref="Configuration.SdkServerOptions.TraceLevel"/>, which takes effect while the
/// component runs (<see cref="Configuration.SettingReach.Live"/>).
/// </summary>
/// <remarks>
/// It is what the filter of a component's logging reads for each message (<see cref="AppHost{TApp}.ConfigureExternalLogging"/>): a provider that is added with
/// a level of its own is added with <see cref="LogLevel.Trace"/>, and this decides.
/// </remarks>
/// <param name="minimum">The level the component starts with.</param>
public sealed class LiveLogLevel(LogLevel minimum)
{
    private volatile int _minimum = (int)minimum;

    /// <summary>The least level a message is written at; <see cref="LogLevel.None"/> writes none.</summary>
    public LogLevel Minimum
    {
        get => (LogLevel)_minimum;
        set => _minimum = (int)value;
    }

    /// <summary>Whether a message of a level is written.</summary>
    /// <param name="level">The level of the message.</param>
    public bool IsEnabled(LogLevel level)
    {
        var minimum = Minimum;
        return minimum != LogLevel.None && level != LogLevel.None && level >= minimum;
    }
}
