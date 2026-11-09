using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.Runtime.Compiled;
using RDCore.Runtime.Execution;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Services.VerboseMessages;
using RDCore.SDK.Workspace;
using System.IO.Abstractions;
using System.Reflection;

namespace RDCore.CLI.App.Build;

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
    public static int Run(Assembly program, string[] args)
    {
        ArgumentNullException.ThrowIfNull(program);

        // the process's entry point: nothing awaits it, and the main thread has no context a continuation would wait for.
#pragma warning disable VSTHRD002
        return RunAsync(program, args, global::System.Console.Out, global::System.Console.Error, CancellationToken.None).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
    }

    internal static async Task<int> RunAsync(Assembly program, string[] args, TextWriter output, TextWriter error, CancellationToken token)
    {
        try
        {
            return await RunImageAsync(program, args, output, error, token);
        }
        catch (InvalidDataException exception)
        {
            await error.WriteLineAsync(exception.Message);
            return ProgramRunner.Refused;
        }
    }

    // the image replayed, the assembly bound, and the entry point run on the runner rdc run runs a workspace with.
    private static async Task<int> RunImageAsync(Assembly program, string[] args, TextWriter output, TextWriter error, CancellationToken token)
    {
        var image = ImageOf(program);
        var messages = new ServiceCollection()
            .AddSingleton(Options.Create(new VerboseMessageOptions()))
            .AddVerboseMessages()
            .BuildServiceProvider()
            .GetRequiredService<IVerboseMessageBuilder>();

        using var provider = new EnvironmentSessionProvider(image.Environment.Profile, new FileSystem(), NullLogger<EnvironmentSessionProvider>.Instance);
        provider.Compose(image.Project, image.Root);
        var define = new DefineSymbolsHandler(provider, messages, NullLogger<DefineSymbolsHandler>.Instance);
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

        return await ProgramRunner.RunAsync(provider, entryPoint.Procedure, args, messages, output, error, unattended: false, token);
    }

    internal static BuiltProgramImage ImageOf(Assembly program)
    {
        using var stream = program.GetManifestResourceStream(BuiltProgramImage.ResourceName)
            ?? throw new InvalidDataException($"{program.GetName().Name} is no program rdc built: it has no {BuiltProgramImage.ResourceName}");
        using var reader = new StreamReader(stream);
        return PlatformJson.Deserialize<BuiltProgramImage>(reader.ReadToEnd());
    }
}
