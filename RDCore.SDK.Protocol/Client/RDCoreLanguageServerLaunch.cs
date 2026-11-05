using RDCore.SDK.Platform;
using System.Diagnostics;
using System.Text.Json;

namespace RDCore.SDK.Client;

/// <summary>
/// What a client passes to start the language server of a deployed platform.
/// </summary>
public sealed record class RDCoreLanguageServerLaunchOptions
{
    /// <summary>
    /// The platform root: the folder of <c>rdcore.json</c> (<see cref="PlatformManifest.FileName"/>), with a subfolder for each component.
    /// </summary>
    /// <remarks>A relative path is relative to the current directory of the process that prepares the launch.</remarks>
    public required string PlatformRoot { get; init; }

    /// <summary>
    /// The workspace the language server opens, as the client names it: its folder's absolute <c>file:</c> URI.
    /// </summary>
    public required string Workspace { get; init; }

    /// <summary>
    /// The name of the pipe the language server listens on, and the client connects to; <see langword="null"/> for a new one
    /// (<see cref="RDCoreLanguageServerLaunch.NewPipeName"/>).
    /// </summary>
    public string? PipeName { get; init; }

    /// <summary>
    /// The process of the client, which the language server ends with when it ends; <see langword="null"/> to leave that to the LSP <c>initialize</c> request's
    /// <c>processId</c>.
    /// </summary>
    public int? ClientProcessId { get; init; }
}

/// <summary>
/// How to start the language server of a deployed platform, as a client that connects to it over a named pipe starts it: the pre-launch preparation an IDE does
/// before it starts the server it registered.
/// </summary>
/// <remarks>
/// <para>
/// The language server listens on the pipe; the client starts the process and connects to it (an IDE's <c>"pipeRole": "connect"</c>). The command line is
/// <c>--pipe-name &lt;pipe&gt; --workspace &lt;uri&gt;</c>, which is all the language server needs: it brings up the rest of the platform itself, from the manifest.
/// </para>
/// <para>
/// The platform root is given to the server explicitly (<see cref="PlatformRootEnvironmentVariable"/>), whatever the folder the IDE starts it in, and is inherited by
/// every process it starts.
/// </para>
/// <para>
/// The executable is the one the manifest names, with or without <c>.exe</c>: a platform deployed on Windows names <c>RDCore.LanguageServer.exe</c>, which is
/// <c>RDCore.LanguageServer</c> elsewhere.
/// </para>
/// </remarks>
public sealed class RDCoreLanguageServerLaunch
{
    /// <summary>
    /// The environment variable that names the platform root to a process of the platform: <c>RDCORE_PLATFORM_ROOT</c>.
    /// </summary>
    public const string PlatformRootEnvironmentVariable = "RDCORE_PLATFORM_ROOT";

    private static readonly JsonSerializerOptions ManifestOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>The platform root, as a full path.</summary>
    public required string PlatformRoot { get; init; }

    /// <summary>The platform manifest the language server was found from.</summary>
    public required PlatformManifest Manifest { get; init; }

    /// <summary>The executable of the language server, as a full path.</summary>
    public required string ExecutablePath { get; init; }

    /// <summary>The folder to start it in: its own.</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>The pipe it listens on.</summary>
    public required string PipeName { get; init; }

    /// <summary>Its arguments, one each: <c>--pipe-name</c>, <c>--workspace</c>, and <c>--client-process-id</c> when there is one.</summary>
    public required IReadOnlyList<string> Arguments { get; init; }

    /// <summary>The environment variables to set for it, over the client's own.</summary>
    public required IReadOnlyDictionary<string, string> Environment { get; init; }

    /// <summary>
    /// Prepares the start of the language server of the platform at <see cref="RDCoreLanguageServerLaunchOptions.PlatformRoot"/>.
    /// </summary>
    /// <param name="options">The platform, the workspace, and the pipe.</param>
    /// <returns>How to start it.</returns>
    /// <exception cref="ArgumentException">The workspace is not an absolute <c>file:</c> URI.</exception>
    /// <exception cref="DirectoryNotFoundException">The platform root does not exist.</exception>
    /// <exception cref="FileNotFoundException">The platform root has no manifest, or the manifest names a language server that is not there.</exception>
    /// <exception cref="InvalidDataException">The manifest is not one, or names no language server.</exception>
    public static RDCoreLanguageServerLaunch Prepare(RDCoreLanguageServerLaunchOptions options)
    {
        var (root, manifest, executable) = LanguageServerOf(options.PlatformRoot, options.Workspace, nameof(options));
        var pipe = options.PipeName ?? NewPipeName();
        List<string> arguments = ["--pipe-name", pipe, "--workspace", options.Workspace];
        if (options.ClientProcessId is { } clientProcessId)
        {
            arguments.AddRange(["--client-process-id", clientProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        }

        return new RDCoreLanguageServerLaunch
        {
            PlatformRoot = root,
            Manifest = manifest,
            ExecutablePath = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            PipeName = pipe,
            Arguments = arguments,
            Environment = new Dictionary<string, string> { [PlatformRootEnvironmentVariable] = root },
        };
    }

    // the language server's executable of a platform root, for a workspace: what starting it as a language server and as a debug adapter both need, and are
    // refused for the same reasons.
    internal static (string Root, PlatformManifest Manifest, string Executable) LanguageServerOf(string platformRoot, string workspace, string parameter)
    {
        if (!Uri.TryCreate(workspace, UriKind.Absolute, out var uri) || !uri.IsFile)
        {
            throw new ArgumentException($"The workspace '{workspace}' is not an absolute file: URI.", parameter);
        }

        var root = Path.GetFullPath(platformRoot);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"The platform root '{root}' does not exist.");
        }

        var manifest = ReadManifest(root);
        if (string.IsNullOrWhiteSpace(manifest.LangService))
        {
            throw new InvalidDataException($"The platform manifest of '{root}' names no language server ('langService').");
        }

        var named = Path.GetFullPath(Path.Combine(root, manifest.LangService.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar)));
        var executable = ExecutableOf(named)
            ?? throw new FileNotFoundException($"The platform manifest of '{root}' names a language server that is not there.", named);
        return (root, manifest, executable);
    }

    // the executable as named, or else as this platform names it: a manifest written on Windows names an .exe, which an apphost elsewhere has no extension for.
    private static string? ExecutableOf(string named)
    {
        if (File.Exists(named))
        {
            return named;
        }

        var other = named.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? named[..^4] : named + ".exe";
        return File.Exists(other) ? other : null;
    }

    /// <summary>
    /// Reads the manifest of a platform root.
    /// </summary>
    /// <param name="platformRoot">The platform root.</param>
    /// <exception cref="FileNotFoundException">The platform root has no manifest.</exception>
    /// <exception cref="InvalidDataException">The manifest is not one.</exception>
    public static PlatformManifest ReadManifest(string platformRoot)
    {
        var path = Path.Combine(platformRoot, PlatformManifest.FileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"The platform root '{platformRoot}' has no manifest.", path);
        }

        try
        {
            return JsonSerializer.Deserialize<PlatformManifest>(File.ReadAllText(path), ManifestOptions)
                ?? throw new InvalidDataException($"The platform manifest '{path}' is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The platform manifest '{path}' is not one: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// A new pipe name for a language server, one no other instance has: <c>RDCore.LanguageServer.Pipe.&lt;random&gt;</c>.
    /// </summary>
    public static string NewPipeName() => $"RDCore.LanguageServer.Pipe.{Random.Shared.NextInt64()}";

    /// <summary>
    /// The start of the language server as a <see cref="ProcessStartInfo"/>, for a client that starts it itself.
    /// </summary>
    public ProcessStartInfo ToProcessStartInfo()
    {
        var info = new ProcessStartInfo(ExecutablePath)
        {
            WorkingDirectory = WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
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
