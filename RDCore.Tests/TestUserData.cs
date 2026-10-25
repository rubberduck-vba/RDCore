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
    public static void Initialize(TestContext _) => Environment.SetEnvironmentVariable(PlatformEnvironment.UserDataEnvironmentVariable, Directory);
}
