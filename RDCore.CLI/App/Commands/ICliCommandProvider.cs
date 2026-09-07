using RDCore.CLI.App.Messages;
using RDCore.CLI.App.Messages.Model;
using RDCore.SDK.Client;
using RDCore.SDK.Extensibility;

namespace RDCore.CLI.App.Commands;

/// <summary>
/// Contributes <see cref="ICliCommand"/>s to the <see cref="ICliCommandDispatcher"/>. The dispatcher
/// aggregates every registered provider; the native provider is registered first and wins name
/// collisions.
/// </summary>
internal interface ICliCommandProvider
{
    IEnumerable<ICliCommand> GetCommands();
}

/// <summary>
/// The commands built into <c>rdc.exe</c> itself — the CLI advertises this role with its
/// <c>[assembly: ProvidesCorePlatformClientCapability&lt;CliCommand&gt;]</c> declaration.
/// </summary>
internal sealed class NativeCliCommandProvider(IEnumerable<ICliCommand> commands) : ICliCommandProvider
{
    public IEnumerable<ICliCommand> GetCommands() => commands;
}

/// <summary>
/// Probes discovered extensions for the <see cref="CliCommand"/> capability and (eventually) surfaces
/// the verbs they contribute. For now it only reports what it finds — adapting an extension's verb
/// descriptors to <see cref="ICliCommand"/> is a follow-up.
/// </summary>
internal sealed class ExtensionCliCommandProvider(IExtensionsProvider extensions, IConsoleMessageWriter writer) : ICliCommandProvider
{
    public IEnumerable<ICliCommand> GetCommands()
    {
        foreach (var extension in extensions.Discover())
        {
            if (!extension.Capabilities.Any(capability => capability.Name == nameof(CliCommand) && capability.IsSupported))
            {
                continue;
            }

            writer.WriteMessage(new ConsoleMessageBuilder()
                .WithKind(MessageKind.Trace)
                .WithTitle("🧩")
                .WithMessageBody($"{extension.Title} advertises {nameof(CliCommand)}"));

            // TODO load the extension's verb descriptors and adapt them to ICliCommand.
        }

        yield break;
    }
}
