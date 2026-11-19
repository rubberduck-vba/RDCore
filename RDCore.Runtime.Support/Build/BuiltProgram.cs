using Microsoft.Extensions.Logging.Abstractions;
using RDCore.External.Interaction;
using RDCore.Runtime.Support.Host;
using RDCore.Runtime.Support.Host.Handlers;
using RDCore.Runtime.Compiled;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.External.Application;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Client;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Platform;
using RDCore.SDK.Platform.Components;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Workspace;
using System.IO.Abstractions;
using System.Reflection;

namespace RDCore.Runtime.Support.Build;

internal sealed record class BuiltEnvironment(
    bool Is64Bit, int Lcid, int AnsiCodePage, bool SupportsOptionCompareDatabase, OptionCompare DatabaseCompare, VBErlLineNumbering ErlLineNumbering,
    bool AllowDllImports, bool AllowAutomation, bool LegacyRegistrySettings, VBBuildConfiguration Configuration, bool ReleaseDropsDebugStatements,
    VBErlAfterErrClear ErlAfterErrClear, VBOperatingSystem OperatingSystem, string? Language = null)
{
    // Language: the identifier of the language the program was built in, which decides its libraries where it runs as where it was built. A program built
    // before it was recorded has none, and runs in RD-VBA, as it was built.
    public static BuiltEnvironment Of(IRuntimeEnvironmentProfile environment) => new(
        environment.Is64Bit, environment.Lcid, environment.AnsiCodePage, environment.SupportsOptionCompareDatabase, environment.DatabaseCompare,
        environment.ErlLineNumbering, environment.AllowDllImports, environment.AllowAutomation, environment.LegacyRegistrySettings, environment.Configuration,
        environment.ReleaseDropsDebugStatements, environment.ErlAfterErrClear, environment.OperatingSystem, environment.Language.Id);

    // the procedures of a built program run compiled. Not serialized: the profile's encoding is no JSON.
    [System.Text.Json.Serialization.JsonIgnore]
    public RuntimeEnvironmentProfile Profile => new(
        Is64Bit, Lcid, AnsiCodePage, SupportsOptionCompareDatabase, DatabaseCompare, ErlLineNumbering, AllowDllImports, AllowAutomation: AllowAutomation,
        LegacyRegistrySettings: LegacyRegistrySettings, ExecutionEngine: VBExecutionEngine.Compiled, Configuration: Configuration,
        ReleaseDropsDebugStatements: ReleaseDropsDebugStatements, ErlAfterErrClear: ErlAfterErrClear, OperatingSystem: OperatingSystem,
        SourceLanguage: SupportedLanguages.TryGet(Language, out var language) ? language : null);
}

// a module of the project as the build read it: its name and its kind, which its source says and which the program cannot read where it runs.
internal sealed record class BuiltModule(string RelativeUri, string Name, ModuleType Kind);

// the load of the workspace as the build made it (Definitions: in order, every module's types, then declarations, then code), replayed when the program
// runs, so that it runs with no parser and no resolver, and with no source: Modules are what the build read of each. Root is what the identities of its
// symbols are relative to. NativeDialogs: whether the program shows the machine's own dialogs where it has them, as its project and the settings said when
// it was built (ProgramDialogs); an image that does not say asks at the console.
internal sealed record class BuiltProgramImage(
    RDCoreProject Project, Uri Root, BuiltEnvironment Environment, string EntryModule, string EntryPoint, IReadOnlyList<DefineSymbolsParams> Definitions,
    IReadOnlyList<BuiltModule> Modules, bool NativeDialogs = false)
{
    public const string ResourceName = "RDCore.Program.json";
}

// what Program.Main of a built program's assembly calls, with that assembly. Public: the built assembly can only call public members.
public static class BuiltProgram
{
    // the switch rdc run takes for the same: nobody answers a dialog, and the first one fails the run.
    private const string UnattendedSwitch = "--unattended";

    // the subfolder of the program's folder that holds the descriptions of the libraries its project references, named as the platform's is: the
    // program's session reads them there, and nowhere else.
    internal const string LibrariesFolder = PlatformEnvironment.SymbolsDirectory;

    // the assembly that hosts Visual Basic 6.0 forms in RDForms, and what only it references: the program's folder holds them when its project has such a form.
    // Named, and not read from its type, which would load it.
    internal const string FormsAssembly = "RDCore.External.Forms";

    public static int Run(Assembly program, string[] args)
    {
        ArgumentNullException.ThrowIfNull(program);

        // the process's main thread is served while the program runs on another, as rdc's is: on macOS, the RDForms windows of its external host run their loop on it.
        return ProcessMainThread.Serve(() => EntryAsync(program, args));
    }

    private static async Task<int> EntryAsync(Assembly program, string[] args)
    {
        // started by the program as its external host: it makes the calls the program makes to the outside world, and runs no program of its own.
        if (string.Equals(Environment.GetEnvironmentVariable(RDCoreServerProcess.ModeEnvironmentVariable), RDCoreServerProcess.ExternalMode, StringComparison.OrdinalIgnoreCase))
        {
            using var host = new MachineExternalHost(logFileName: null, hostsRDForms: File.Exists(Path.Combine(AppContext.BaseDirectory, $"{FormsAssembly}.dll")));
            return await host.RunAsync(args);
        }

        if (ExecutableOf(program) is { } executable)
        {
            MachineExternal.Executable = executable;
        }

        try
        {
            return await RunAsync(program, args, global::System.Console.Out, global::System.Console.Error, CancellationToken.None);
        }
        finally
        {
            MachineExternal.Shutdown();
        }
    }

    // the program's executable beside its assembly, which is its external host too; none when the program was built without one (and is run with dotnet).
    private static string? ExecutableOf(Assembly program)
    {
        var executable = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? $"{program.GetName().Name}.exe" : program.GetName().Name!);
        return File.Exists(executable) ? executable : null;
    }

    // the name every instance of an executable opens, which exists while one of them runs: of its full path, without regard to case where the file system
    // has none. A mutex's name holds no separator after its namespace, so the path is hashed.
    internal static string InstanceName(string executable)
    {
        var path = OperatingSystem.IsWindows() ? Path.GetFullPath(executable).ToUpperInvariant() : Path.GetFullPath(executable);
        return $@"Local\RDCore.App.{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path)))}";
    }

    // keys: the console's, unless a test types them; the dialogs are then drawn on output. libraries: the program's own folder's, unless a test runs the
    // program from a folder that is not the process's.
    internal static async Task<int> RunAsync(Assembly program, string[] args, TextWriter output, TextWriter error, CancellationToken token, IConsoleKeys? keys = null,
        ILibrarySource? libraries = null)
    {
        try
        {
            return await RunImageAsync(program, args, output, error, token, keys, libraries);
        }
        catch (InvalidDataException exception)
        {
            await error.WriteLineAsync(exception.Message);
            return ProgramRunner.Refused;
        }
    }

    // the image replayed, the assembly bound, and the entry point run on the runner rdc run runs a workspace with.
    private static async Task<int> RunImageAsync(Assembly program, string[] args, TextWriter output, TextWriter error, CancellationToken token, IConsoleKeys? keys,
        ILibrarySource? libraries)
    {
        var image = ImageOf(program);
        var errorDetails = new ErrorDetailsRecorder();

        var fileSystem = new FileSystem();
        using var provider = new EnvironmentSessionProvider(image.Environment.Profile, fileSystem, NullLogger<EnvironmentSessionProvider>.Instance,
            libraries ?? new DirectoryLibrarySource(fileSystem, Path.Combine(AppContext.BaseDirectory, LibrariesFolder)),
            DialogsOf(image.NativeDialogs, keys, output) is { } dialogs ? MachineExternal.World with { Interaction = dialogs } : null);
        provider.Compose(image.Project, image.Root, image.Modules.ToDictionary(module => module.RelativeUri, module => (module.Name, module.Kind), StringComparer.Ordinal));
        // App is the program's executable, or its assembly when it runs with dotnet; it is another instance when one of the same executable holds its name.
        var executable = ExecutableOf(program) ?? Path.Combine(AppContext.BaseDirectory, program.GetName().Name!);
        using var instance = new Mutex(initiallyOwned: false, InstanceName(executable), out var first);
        SessionApplication.Of(provider.Session).Built(image.Project.Name, executable, prevInstance: !first);
        var define = new DefineSymbolsHandler(provider, new ProjectAnalysisSessions(image.Environment.Profile, fileSystem, NullLogger<ProjectAnalysisSessions>.Instance), errorDetails, NullLogger<DefineSymbolsHandler>.Instance);
        foreach (var definition in image.Definitions)
        {
            var defined = await define.Handle(definition, token);
            if (defined.CodeErrors.Count > 0)
            {
                throw new InvalidDataException($"{definition.ModuleName}: {string.Join("; ", defined.CodeErrors)}");
            }
        }

        _ = ProgramAssembly.Bind(provider.Image, program);
        var build = new ProjectBuild { EntryPoint = $"{image.EntryModule}.{image.EntryPoint}" };
        if (ProgramRunner.EntryPointOf(provider.Session, [image.EntryModule], build, out var refusal) is not { } entryPoint)
        {
            throw new InvalidDataException(refusal);
        }

        // --unattended among the arguments says nobody answers the program's dialogs: the runtime honors it, and it stays the program's, in Command$ and Main's
        // arguments, as every other one does.
        var unattended = args.Contains(UnattendedSwitch, StringComparer.Ordinal);
        return await ProgramRunner.RunAsync(provider, entryPoint.Procedure, args, errorDetails, output, error, unattended, token);
    }

    // the dialogs of the program: asked at the console, as rdc run and the shell ask them, on every operating system; the machine's own where it has them,
    // when the program was built to show them (native). A terminal that is not redirected, and whose TERM names a terminal other than "dumb", understands
    // ANSI escape sequences.
    private static ConsoleUserInteraction? DialogsOf(bool native, IConsoleKeys? keys, TextWriter output)
        => ProgramDialogs.ShowsNative(native) ? null
            : keys is not null ? new ConsoleUserInteraction(keys, output)
            : ConsoleUserInteraction.OfTheConsole(new SystemConsoleKeys(),
                ansi: !global::System.Console.IsOutputRedirected && Environment.GetEnvironmentVariable("TERM") is { Length: > 0 } and not "dumb");

    internal static BuiltProgramImage ImageOf(Assembly program)
    {
        using var stream = program.GetManifestResourceStream(BuiltProgramImage.ResourceName)
            ?? throw new InvalidDataException($"{program.GetName().Name} is no program rdc built: it has no {BuiltProgramImage.ResourceName}");
        using var reader = new StreamReader(stream);
        return PlatformJson.Deserialize<BuiltProgramImage>(reader.ReadToEnd());
    }
}
