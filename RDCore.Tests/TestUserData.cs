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

    [AssemblyInitialize]
    public static void Initialize(TestContext _)
    {
        Environment.SetEnvironmentVariable(PlatformEnvironment.UserDataEnvironmentVariable, Directory);

        // the suite runs each way the code can run, to compare the two: RDCORE_ENGINE=Compiled compiles every procedure that can be.
        if (Environment.GetEnvironmentVariable("RDCORE_ENGINE") is { } engine
            && Enum.TryParse<RDCore.SDK.Runtime.Abstract.Execution.VBExecutionEngine>(engine, ignoreCase: true, out var parsed))
        {
            RDCore.Runtime.Compiled.CompiledExecution.Engine = parsed;
        }

        // RDCORE_WALKBACK=random hands every compiled activation to the interpreter at a safepoint of its own, to show the walk-back leaves nothing behind.
        RDCore.Runtime.Compiled.CompiledExecution.WalksBackAtRandom = string.Equals(Environment.GetEnvironmentVariable("RDCORE_WALKBACK"), "random", StringComparison.OrdinalIgnoreCase);
    }
}
