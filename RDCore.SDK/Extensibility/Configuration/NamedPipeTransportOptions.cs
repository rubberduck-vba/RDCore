namespace RDCore.SDK.Extensibility.Configuration;

/// <summary>
/// Configures <em>named pipe</em> transport layer settings.
/// </summary>
/// <remarks>
/// 👉 <strong>Named pipes are inherently local</strong> and the preferred way to establish a communication channel between the platform processes.
/// </remarks>
public record class NamedPipeTransportOptions
{
    private const string _defaultPipeName = "RDCore.SDK.Server.Pipe";
    private const int _defaultMaximumInstances = 1;

    /// <summary>
    /// Gets a new random pipe name by concatenating the configured <c>PipeName</c> value with a random <c>Int64</c>.
    /// </summary>
    /// <returns></returns>
    public string GetRandomPipeName() => $"{PipeName}.{Random.Shared.NextInt64():00000}";

    /// <summary>
    /// The base name of the <em>named pipe</em> transport.
    /// </summary>
    /// <remarks>
    /// 🧩 This setting is required in <strong>both <em>client</em> and <em>server</em> applications</strong> and <strong>MUST</strong> be different for each loaded SDK application.
    /// </remarks>
    public string PipeName { get; set; } = _defaultPipeName;
    /// <summary>
    /// The maximum number of pipe instances that can run concurrently.
    /// </summary>
    /// <remarks>
    /// ⚠️ Changing this setting without proper support for it could <strong>corrupt client/server communications</strong>.
    /// Each server instance should append a random suffix to the pipe name to prevent this,
    /// otherwise there needs to be a mechanism (mutex) to prevent multiple instances of the server application from being started.
    /// </remarks>
    //[Option('i', "instances", Group = "pipe", Default = _defaultMaximumInstances)]
    public int MaximumInstances { get; set; } = _defaultMaximumInstances;
}
