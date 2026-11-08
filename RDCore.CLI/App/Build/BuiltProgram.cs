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

/// <summary>
/// The environment a program was built for: what its session is composed for when it runs, as the session it was built in was.
/// </summary>
internal sealed record class BuiltEnvironment(
    bool Is64Bit, int Lcid, int AnsiCodePage, bool SupportsOptionCompareDatabase, OptionCompare DatabaseCompare, VBErlLineNumbering ErlLineNumbering,
    bool AllowDllImports, bool AllowAutomation, bool LegacyRegistrySettings, VBBuildConfiguration Configuration, bool ReleaseDropsDebugStatements,
    VBErlAfterErrClear ErlAfterErrClear, VBOperatingSystem OperatingSystem)
{
    /// <summary>
    /// What is kept of <paramref name="environment"/>.
    /// </summary>
    public static BuiltEnvironment Of(IRuntimeEnvironmentProfile environment) => new(
        environment.Is64Bit, environment.Lcid, environment.AnsiCodePage, environment.SupportsOptionCompareDatabase, environment.DatabaseCompare,
        environment.ErlLineNumbering, environment.AllowDllImports, environment.AllowAutomation, environment.LegacyRegistrySettings, environment.Configuration,
        environment.ReleaseDropsDebugStatements, environment.ErlAfterErrClear, environment.OperatingSystem);

    /// <summary>
    /// The profile a built program's session is composed for: this environment, its procedures run compiled.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public RuntimeEnvironmentProfile Profile => new(
        Is64Bit, Lcid, AnsiCodePage, SupportsOptionCompareDatabase, DatabaseCompare, ErlLineNumbering, AllowDllImports, AllowAutomation: AllowAutomation,
        LegacyRegistrySettings: LegacyRegistrySettings, ExecutionEngine: VBExecutionEngine.Compiled, Configuration: Configuration,
        ReleaseDropsDebugStatements: ReleaseDropsDebugStatements, ErlAfterErrClear: ErlAfterErrClear, OperatingSystem: OperatingSystem);
}

/// <summary>
/// The program image of a built program: the load of its workspace as the build made it, replayed when the program runs, so that it runs with no parser
/// and no resolver.
/// </summary>
/// <param name="Project">The project, as its <c>.rdproj</c> said.</param>
/// <param name="Root">The root of the workspace the program was built from: what the identities of its symbols are relative to.</param>
/// <param name="Environment">The environment it was built for.</param>
/// <param name="EntryModule">The module that declares the entry point.</param>
/// <param name="EntryPoint">The name of the entry point.</param>
/// <param name="Definitions">What the build defined in its session, in order: the types of every module, then their declarations, then their code.</param>
internal sealed record class BuiltProgramImage(
    RDCoreProject Project, Uri Root, BuiltEnvironment Environment, string EntryModule, string EntryPoint, IReadOnlyList<DefineSymbolsParams> Definitions)
{
    /// <summary>
    /// The name of the manifest resource of the program's assembly the image is.
    /// </summary>
    public const string ResourceName = "RDCore.Program.json";
}

/// <summary>
/// What a built program runs: its entry point, <c>Program.Main</c>, calls <see cref="Run"/> with its own assembly.
/// </summary>
public static class BuiltProgram
{
    /// <summary>
    /// Runs the program <paramref name="program"/> is the assembly of: its session composed from its image, its procedures bound to the code the build
    /// compiled them to, and its entry point run.
    /// </summary>
    /// <param name="program">The program's assembly.</param>
    /// <param name="args">The arguments the program was started with.</param>
    /// <returns>The process's exit code, as <c>rdc run</c> ends: 0 when the program ran to its end, 1 when a run-time error stopped it, 2 when it could not
    /// be started.</returns>
    public static int Run(Assembly program, string[] args)
    {
        ArgumentNullException.ThrowIfNull(program);

        // the process's entry point: nothing awaits it, and the main thread has no context a continuation would wait for.
#pragma warning disable VSTHRD002
        return RunAsync(program, args, global::System.Console.Out, global::System.Console.Error, CancellationToken.None).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
    }

    /// <summary>
    /// Runs the program, as <see cref="Run"/> does, writing what it prints to <paramref name="output"/> and its error to <paramref name="error"/>.
    /// </summary>
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

        var provider = new EnvironmentSessionProvider(image.Environment.Profile, new FileSystem(), NullLogger<EnvironmentSessionProvider>.Instance);
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

    /// <summary>
    /// The image of the program <paramref name="program"/> is the assembly of.
    /// </summary>
    /// <exception cref="InvalidDataException">The assembly is no built program's.</exception>
    internal static BuiltProgramImage ImageOf(Assembly program)
    {
        using var stream = program.GetManifestResourceStream(BuiltProgramImage.ResourceName)
            ?? throw new InvalidDataException($"{program.GetName().Name} is no program rdc built: it has no {BuiltProgramImage.ResourceName}");
        using var reader = new StreamReader(stream);
        return PlatformJson.Deserialize<BuiltProgramImage>(reader.ReadToEnd());
    }
}
