using RDCore.SDK.Platform;
using System.Diagnostics;

namespace RDCore.SDK.Client;

/// <summary>
/// What a client passes to start the debug adapter of a deployed platform.
/// </summary>
public sealed record class RDCoreDebugAdapterLaunchOptions
{
    /// <summary>
    /// The platform root: the folder of <c>rdcore.json</c> (<see cref="PlatformManifest.FileName"/>), with a subfolder for each component.
    /// </summary>
    /// <remarks>A relative path is relative to the current directory of the process that prepares the launch.</remarks>
    public required string PlatformRoot { get; init; }

    /// <summary>
    /// The workspace the program is in, as the client names it: its folder's absolute <c>file:</c> URI.
    /// </summary>
    public required string Workspace { get; init; }

    /// <summary>
    /// The programmatic name of the module that has the entry point; <see langword="null"/> for the one standard module of the program that declares it, which the
    /// debug adapter finds.
    /// </summary>
    public string? Module { get; init; }

    /// <summary>
    /// The name of the procedure the program starts with; <see langword="null"/> for <c>Main</c>.
    /// </summary>
    public string? EntryPoint { get; init; }
}

/// <summary>
/// How to start the debug adapter of a deployed platform, and what to launch with it: the preparation an IDE does before it starts a debugging session of a
/// workspace.
/// </summary>
/// <remarks>
/// <para>
/// The debug adapter is the language server's executable, started as one: <c>--dap --workspace &lt;uri&gt;</c> (<see cref="Switch"/>). It speaks the <strong>Debug
/// Adapter Protocol</strong> over its standard input and output, and brings up the rest of the platform itself, from the manifest, for the workspace it was given.
/// The platform root is given to it explicitly (<see cref="RDCoreLanguageServerLaunch.PlatformRootEnvironmentVariable"/>), as it is to the language server.
/// </para>
/// <para>
/// What to run is said in the DAP <c>launch</c> request, beyond what every <c>launch</c> request says: <see cref="LaunchArguments"/>, named
/// <see cref="ModuleArgument"/> and <see cref="EntryPointArgument"/>. Each is there only when the client said it, and the adapter decides what is not: a launch
/// that names no module runs the one standard module that declares the entry point, and is refused, with the reason, when none does or more than one does.
/// </para>
/// </remarks>
public sealed class RDCoreDebugAdapterLaunch
{
    /// <summary>The switch that starts the language server's executable as a debug adapter: <c>--dap</c>.</summary>
    public const string Switch = "--dap";

    /// <summary>The type of debug adapter this is, as a DAP client's <c>initialize</c> request says it (<c>adapterID</c>): <c>rdcore</c>.</summary>
    public const string AdapterId = "rdcore";

    /// <summary>The argument of the <c>launch</c> request that names the module with the entry point: <c>module</c>.</summary>
    public const string ModuleArgument = "module";

    /// <summary>The argument of the <c>launch</c> request that names the procedure the program starts with: <c>entryPoint</c>.</summary>
    public const string EntryPointArgument = "entryPoint";

    /// <summary>The platform root, as a full path.</summary>
    public required string PlatformRoot { get; init; }

    /// <summary>The platform manifest the debug adapter was found from.</summary>
    public required PlatformManifest Manifest { get; init; }

    /// <summary>The executable of the debug adapter, as a full path: the language server's.</summary>
    public required string ExecutablePath { get; init; }

    /// <summary>The folder to start it in: its own.</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>Its arguments, one each: <c>--dap</c> and <c>--workspace</c>.</summary>
    public required IReadOnlyList<string> Arguments { get; init; }

    /// <summary>The environment variables to set for it, over the client's own.</summary>
    public required IReadOnlyDictionary<string, string> Environment { get; init; }

    /// <summary>
    /// What the client's <c>launch</c> request says beyond what every one says, by name: <see cref="ModuleArgument"/> and <see cref="EntryPointArgument"/>, each
    /// when the client gave one, and none when it gave neither.
    /// </summary>
    public required IReadOnlyDictionary<string, string> LaunchArguments { get; init; }

    /// <summary>
    /// Prepares the start of the debug adapter of the platform at <see cref="RDCoreDebugAdapterLaunchOptions.PlatformRoot"/>, and its <c>launch</c>.
    /// </summary>
    /// <param name="options">The platform, the workspace, and what to run.</param>
    /// <returns>How to start it, and what to launch.</returns>
    /// <exception cref="ArgumentException">The workspace is not an absolute <c>file:</c> URI.</exception>
    /// <exception cref="DirectoryNotFoundException">The platform root does not exist.</exception>
    /// <exception cref="FileNotFoundException">The platform root has no manifest, or the manifest names a language server that is not there.</exception>
    /// <exception cref="InvalidDataException">The manifest is not one, or names no language server.</exception>
    public static RDCoreDebugAdapterLaunch Prepare(RDCoreDebugAdapterLaunchOptions options)
    {
        var (root, manifest, executable) = RDCoreLanguageServerLaunch.LanguageServerOf(options.PlatformRoot, options.Workspace, nameof(options));

        var launch = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(options.Module))
        {
            launch[ModuleArgument] = options.Module;
        }

        if (!string.IsNullOrWhiteSpace(options.EntryPoint))
        {
            launch[EntryPointArgument] = options.EntryPoint;
        }

        return new RDCoreDebugAdapterLaunch
        {
            PlatformRoot = root,
            Manifest = manifest,
            ExecutablePath = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            Arguments = [Switch, "--workspace", options.Workspace],
            Environment = new Dictionary<string, string> { [RDCoreLanguageServerLaunch.PlatformRootEnvironmentVariable] = root },
            LaunchArguments = launch,
        };
    }

    /// <summary>
    /// The start of the debug adapter as a <see cref="ProcessStartInfo"/>, for a client that starts it itself: its standard input and output are redirected,
    /// since the protocol is spoken over them.
    /// </summary>
    public ProcessStartInfo ToProcessStartInfo()
    {
        var info = new ProcessStartInfo(ExecutablePath)
        {
            WorkingDirectory = WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
        };

        foreach (var argument in Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in Environment)
        {
            info.Environment[name] = value;
        }

        return info;
    }
}
