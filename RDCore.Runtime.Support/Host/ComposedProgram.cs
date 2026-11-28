using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Support.Build;
using RDCore.Runtime.Support.Host.Handlers;
using RDCore.SDK.Platform.Components;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Hosting;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using System.IO.Abstractions;

namespace RDCore.Runtime.Support.Host;

// a program as it was composed from its files, which composes each of its machines without a parser, and is written to a file for a host to load: its
// project and the root the identities of its symbols are relative to, the environment its machines run in and what runs their code, the modules each file
// declares (its source is not read again), what was defined in the session it was composed in, in order, and its documents, each file with its parse.
internal sealed record class ProgramCompositionImage(
    RDCoreProject Project, Uri Root, BuiltEnvironment Environment, VBExecutionEngine ExecutionEngine, IReadOnlyList<BuiltModule> Modules,
    IReadOnlyList<DefineSymbolsParams> Definitions, IReadOnlyList<RDCoreDocument> Documents)
{
    // the version of the format this platform writes, and the only one it reads.
    public const int CurrentFormatVersion = 1;

    // the extension of a composition file, as rdc compose names it.
    public const string Extension = ".rdcomposition";

    public int FormatVersion { get; init; } = CurrentFormatVersion;

    [System.Text.Json.Serialization.JsonIgnore]
    public IRuntimeEnvironmentProfile Profile => Environment.Profile with { ExecutionEngine = ExecutionEngine };

    // the modules of each file, by its path relative to the root, as a session is composed with them (EnvironmentSessionProvider.Compose).
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyDictionary<string, IReadOnlyList<DeclaredModule>> Known => Modules
        .GroupBy(module => module.RelativeUri, StringComparer.Ordinal).ToDictionary(file => file.Key, IReadOnlyList<DeclaredModule> (file) =>
            [.. file.Select(module => new DeclaredModule(module.Name, module.Kind, module.IsBlock) { IsPartial = module.IsPartial })],
        StringComparer.Ordinal);

    public string ToJson() => PlatformJson.Serialize(this);

    // a composition of another format is refused: what it holds may mean something else to this platform.
    public static ProgramCompositionImage Of(string json, string source)
    {
        ProgramCompositionImage? image;
        try
        {
            image = PlatformJson.Deserialize<ProgramCompositionImage>(json);
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new InvalidDataException($"{source} is not a program composition: {exception.Message}", exception);
        }

        if (image is null)
        {
            throw new InvalidDataException($"{source} is not a program composition.");
        }

        return image.FormatVersion == CurrentFormatVersion
            ? image
            : throw new InvalidDataException(
                $"{source} is a program composition of format {image.FormatVersion}; this platform reads format {CurrentFormatVersion}.");
    }
}

// what the machines of a composed program use where they run: the file system their sessions act on, the libraries the project references, what they log
// to, and the workspace's settings (the defaults when none are given).
internal sealed record class ProgramServices(
    IFileSystem FileSystem, ILibrarySource Libraries, ILoggerFactory Loggers, IOptions<SdkAppOptions>? Options = null);

// a program composed once, which makes as many machines as its host runs at once, concurrently. The session it was composed in (loaded), when it is given,
// is the first machine's: nothing ran in it. Every other machine has a session of its own, composed again from the image. Disposing the program disposes
// that first session if no machine took it, and no machine.
internal sealed class ComposedProgram(ProgramCompositionImage image, ProgramServices services, EnvironmentSessionProvider? loaded = null) : IComposedProgram
{
    private EnvironmentSessionProvider? _loaded = loaded;
    private int _disposed;

    public IReadOnlyList<RDCoreDocument> Documents => image.Documents;

    public async ValueTask<IProgramMachine> CreateMachineAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var errorDetails = new ErrorDetailsRecorder();
        var provider = Interlocked.Exchange(ref _loaded, null) ?? await ComposeAsync(errorDetails, cancellationToken);
        return new ProgramMachine(provider, errorDetails, ownsSession: true);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Interlocked.Exchange(ref _loaded, null)?.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    // a session composed for the project and each definition defined in it again, in the order it was defined in the first, then its code swept as the
    // first's was. The definitions loaded once: one that does not load again is a defect of the image, not of the program.
    private async Task<EnvironmentSessionProvider> ComposeAsync(IErrorDetailsRecorder errorDetails, CancellationToken token)
    {
        var profile = image.Profile;
        var provider = new EnvironmentSessionProvider(
            profile, services.FileSystem, services.Loggers.CreateLogger<EnvironmentSessionProvider>(), services.Libraries, options: services.Options);
        try
        {
            provider.Compose(image.Project, image.Root, image.Known);
            var define = new DefineSymbolsHandler(
                provider,
                new ProjectAnalysisSessions(
                    profile, services.FileSystem, services.Loggers.CreateLogger<ProjectAnalysisSessions>(), services.Libraries, services.Options),
                errorDetails,
                services.Loggers.CreateLogger<DefineSymbolsHandler>());
            foreach (var definition in image.Definitions)
            {
                var defined = await define.Handle(definition, token);
                if (defined.CodeErrors.Count > 0)
                {
                    throw new InvalidDataException($"{definition.ModuleName}: {string.Join("; ", defined.CodeErrors)}");
                }
            }

            await provider.Image.Semantics.WhenAllEvaluatedAsync(token);
            return provider;
        }
        catch
        {
            provider.Dispose();
            throw;
        }
    }
}
