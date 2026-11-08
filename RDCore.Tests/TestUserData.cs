using RDCore.SDK.Platform;

namespace RDCore.Tests;

/// <summary>
/// Gives the test run a folder of the platform's user of its own (<see cref="PlatformEnvironment.UserDataEnvironmentVariable"/>): no test reads or writes the
/// settings of whoever runs it, and no platform a test starts does either - a child process inherits the variable.
/// </summary>
[TestClass]
public static class TestUserData
{
    /// <summary>The folder, which is empty: a test that wants user settings writes its own, somewhere else.</summary>
    public static string Directory { get; } = Path.Combine(Path.GetTempPath(), "rdcore-tests-user-" + Environment.ProcessId);

    /// <summary>The folder the platforms a test starts log to, unless the test gives them one of its own: never the logs of whoever runs the suite.</summary>
    public static string LogsDirectory { get; } = Path.Combine(Path.GetTempPath(), "rdcore-tests-logs-" + Environment.ProcessId);

    [AssemblyInitialize]
    public static void Initialize(TestContext context)
    {
        Environment.SetEnvironmentVariable(PlatformEnvironment.UserDataEnvironmentVariable, Directory);
        Environment.SetEnvironmentVariable(PlatformEnvironment.LogsEnvironmentVariable, LogsDirectory);

        // the suite runs each way the code can run, to compare the two: by default it compiles every procedure that can be, as the platform does, and
        // RDCORE_ENGINE=Interpreter interprets them all.
        if (Environment.GetEnvironmentVariable("RDCORE_ENGINE") is { } engine
            && Enum.TryParse<RDCore.SDK.Runtime.Abstract.Execution.VBExecutionEngine>(engine, ignoreCase: true, out var parsed))
        {
            RDCore.Runtime.Compiled.CompiledExecution.Engine = parsed;
        }

        // RDCORE_WALKBACK=random hands every compiled activation to the interpreter at a safepoint of its own, to show the walk-back leaves nothing behind.
        RDCore.Runtime.Compiled.CompiledExecution.WalksBackAtRandom = string.Equals(Environment.GetEnvironmentVariable("RDCORE_WALKBACK"), "random", StringComparison.OrdinalIgnoreCase);

        // RDCORE_RELOADS=fail-at-random fails one access of the frame of every compiled activation, to show each hand-over it makes leaves nothing behind.
        RDCore.Runtime.Compiled.CompiledExecution.FailsReloadsAtRandom = string.Equals(Environment.GetEnvironmentVariable("RDCORE_RELOADS"), "fail-at-random", StringComparison.OrdinalIgnoreCase);

        // the seed its safepoints, and the failed accesses, are chosen with: a run that failed is replayed with the one it ran with.
        if (int.TryParse(Environment.GetEnvironmentVariable("RDCORE_WALKBACK_SEED"), out var seed))
        {
            RDCore.Runtime.Compiled.CompiledExecution.WalkBackSeed = seed;
        }

        // the run says how it ran, beside its results: what runs a program whose test does not ask for an engine, how values are held, and what hands compiled
        // code to the interpreter where nothing asks it to. A message below a warning is not shown at the console's default verbosity.
        context.DisplayMessage(MessageLevel.Warning, $"RDCore.Tests: {Mode}");
    }

    /// <summary>
    /// How the suite runs, as the environment variables it reads say: what a run's results are the results of.
    /// </summary>
    public static string Mode
    {
        get
        {
            var engine = RDCore.Runtime.Compiled.CompiledExecution.Engine is { } forced
                ? $"{forced} (RDCORE_ENGINE)"
                : $"{RDCore.SDK.Server.Configuration.SdkEnvironmentOptions.DefaultExecutionEngine} (the platform's default)";
            var values = RDCore.Runtime.Execution.Frames.UnboxedEvaluation.IsEnabled ? "unboxed" : "boxed (RDCORE_UNBOXED=0)";
            var handOvers = (RDCore.Runtime.Compiled.CompiledExecution.WalksBackAtRandom, RDCore.Runtime.Compiled.CompiledExecution.FailsReloadsAtRandom) switch
            {
                (false, false) => "when asked",
                (true, false) => "walk-back at random",
                (false, true) => "failed reloads at random",
                _ => "walk-back and failed reloads at random",
            };
            var seed = RDCore.Runtime.Compiled.CompiledExecution.HandsOverAtRandom ? $", seed {RDCore.Runtime.Compiled.CompiledExecution.WalkBackSeed}" : string.Empty;
            return $"engine {engine}, values {values}, hand-overs {handOvers}{seed}; a test that asks for an engine runs with it";
        }
    }
}
