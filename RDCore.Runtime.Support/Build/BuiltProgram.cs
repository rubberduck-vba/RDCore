using Microsoft.Extensions.Logging.Abstractions;
using RDCore.External.Interaction;
using RDCore.Runtime.Support.Host;
using RDCore.Runtime.Support.Host.Handlers;
using RDCore.Runtime.Compiled;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Platform.Components;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Workspace;
using System.IO.Abstractions;
using System.Reflection;

namespace RDCore.Runtime.Support.Build;

internal sealed record class BuiltEnvironment(
    bool Is64Bit, int Lcid, int AnsiCodePage, bool SupportsOptionCompareDatabase, OptionCompare DatabaseCompare, VBErlLineNumbering ErlLineNumbering,
    bool AllowDllImports, bool AllowAutomation, bool LegacyRegistrySettings, VBBuildConfiguration Configuration, bool ReleaseDropsDebugStatements,
    VBErlAfterErrClear ErlAfterErrClear, VBOperatingSystem OperatingSystem)
{
    public static BuiltEnvironment Of(IRuntimeEnvironmentProfile environment) => new(
        environment.Is64Bit, environment.Lcid, environment.AnsiCodePage, environment.SupportsOptionCompareDatabase, environment.DatabaseCompare,
        environment.ErlLineNumbering, environment.AllowDllImports, environment.AllowAutomation, environment.LegacyRegistrySettings, environment.Configuration,
        environment.ReleaseDropsDebugStatements, environment.ErlAfterErrClear, environment.OperatingSystem);

    // the procedures of a built program run compiled. Not serialized: the profile's encoding is no JSON.
    [System.Text.Json.Serialization.JsonIgnore]
    public RuntimeEnvironmentProfile Profile => new(
        Is64Bit, Lcid, AnsiCodePage, SupportsOptionCompareDatabase, DatabaseCompare, ErlLineNumbering, AllowDllImports, AllowAutomation: AllowAutomation,
        LegacyRegistrySettings: LegacyRegistrySettings, ExecutionEngine: VBExecutionEngine.Compiled, Configuration: Configuration,
        ReleaseDropsDebugStatements: ReleaseDropsDebugStatements, ErlAfterErrClear: ErlAfterErrClear, OperatingSystem: OperatingSystem);
}

// the load of the workspace as the build made it (Definitions: in order, every module's types, then declarations, then code), replayed when the program
// runs, so that it runs with no parser and no resolver. Root is what the identities of its symbols are relative to.
internal sealed record class BuiltProgramImage(
    RDCoreProject Project, Uri Root, BuiltEnvironment Environment, string EntryModule, string EntryPoint, IReadOnlyList<DefineSymbolsParams> Definitions)
{
    public const string ResourceName = "RDCore.Program.json";
}

// what Program.Main of a built program's assembly calls, with that assembly. Public: the built assembly can only call public members.
public static class BuiltProgram
{
    // the switch rdc run takes for the same: nobody answers a dialog, and the first one fails the run.
    private const string UnattendedSwitch = "--unattended";

    public static int Run(Assembly program, string[] args)
    {
        ArgumentNullException.ThrowIfNull(program);

        // the process's entry point: nothing awaits it, and the main thread has no context a continuation would wait for.
#pragma warning disable VSTHRD002
        return RunAsync(program, args, global::System.Console.Out, global::System.Console.Error, CancellationToken.None).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
    }

    // keys: the console's, unless a test types them; the dialogs are then drawn on output.
    internal static async Task<int> RunAsync(Assembly program, string[] args, TextWriter output, TextWriter error, CancellationToken token, IConsoleKeys? keys = null)
    {
        try
        {
            return await RunImageAsync(program, args, output, error, token, keys);
        }
        catch (InvalidDataException exception)
        {
            await error.WriteLineAsync(exception.Message);
            return ProgramRunner.Refused;
        }
    }

    // the image replayed, the assembly bound, and the entry point run on the runner rdc run runs a workspace with.
    private static async Task<int> RunImageAsync(Assembly program, string[] args, TextWriter output, TextWriter error, CancellationToken token, IConsoleKeys? keys)
    {
        var image = ImageOf(program);
        var errorDetails = new ErrorDetailsRecorder();

        using var provider = new EnvironmentSessionProvider(image.Environment.Profile, new FileSystem(), NullLogger<EnvironmentSessionProvider>.Instance,
            outside: DialogsOf(keys, output) is { } dialogs ? MachineExternal.World with { Interaction = dialogs } : null);
        provider.Compose(image.Project, image.Root);
        var define = new DefineSymbolsHandler(provider, errorDetails, NullLogger<DefineSymbolsHandler>.Instance);
        foreach (var definition in image.Definitions)
        {
            var defined = await define.Handle(definition, token);
            if (defined.CodeErrors.Count > 0)
            {
                throw new InvalidDataException($"{definition.ModuleName}: {string.Join("; ", defined.CodeErrors)}");
            }
        }

        _ = ProgramAssembly.Bind(provider.Image, program);
        var settings = new ProjectBuildSettings($"{image.EntryModule}.{image.EntryPoint}", IsLibrary: false);
        if (ProgramRunner.EntryPointOf(provider.Session, [image.EntryModule], settings, out var refusal) is not { } entryPoint)
        {
            throw new InvalidDataException(refusal);
        }

        // --unattended among the arguments says nobody answers the program's dialogs: the runtime honors it, and it stays the program's, in Command$ and Main's
        // arguments, as every other one does.
        var unattended = args.Contains(UnattendedSwitch, StringComparer.Ordinal);
        return await ProgramRunner.RunAsync(provider, entryPoint.Procedure, args, errorDetails, output, error, unattended, token);
    }

    // the dialogs of the program: Windows shows its own, and elsewhere they are asked at the console, as rdc run and the shell ask them. A terminal that is not
    // redirected, and whose TERM names a terminal other than "dumb", understands ANSI escape sequences.
    private static ConsoleUserInteraction? DialogsOf(IConsoleKeys? keys, TextWriter output)
        => OperatingSystem.IsWindows() ? null
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
