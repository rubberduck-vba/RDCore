using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// The files a program leaves open belong to its session (<strong>MS-VBAL §5.4.5.1</strong>, <strong>§5.4.5.2</strong>): they close with it, whichever way
/// the program ended.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.4.5.1 Open")]
public sealed class SessionFilesTests
{
    // body: the lines of Main, ";;" between them; F is the file's path, opened so that nothing else may open it while it is open.
    private static string Program(string file, string body)
        => "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n" +
           "Public Sub Main()\r\n" +
           $"    Dim F As String: F = \"{file}\"\r\n" +
           "    Open F For Binary Lock Read Write As #1\r\n" +
           string.Join("\r\n", body.Split(";;").Select(line => "    " + line.Trim())) + "\r\n" +
           "End Sub\r\n";

    [TestMethod]
    [DataRow("Debug.Print \"open\"", ExecutionOutcome.Completed, DisplayName = "the program ends")]
    [DataRow("Err.Raise 5", ExecutionOutcome.RuntimeError, DisplayName = "an unhandled error ends the program")]
    public async Task AFileTheProgramLeavesOpen_ClosesWithTheSession(string body, ExecutionOutcome expected)
    {
        foreach (var engine in new[] { VBExecutionEngine.Interpreter, VBExecutionEngine.Compiled })
        {
            var path = Path.Combine(Path.GetTempPath(), $"rdcore-session-files-{Guid.NewGuid():N}.bin");
            try
            {
                await InspectAsync([], Program(path, body), (_, result, _) =>
                {
                    Assert.AreEqual(expected, result.Outcome, $"{engine}: {result.ErrorMessage}");
                    return Task.CompletedTask;
                }, engine: engine);

                // the program's Lock Read Write denies every other open of the file for as long as its channel is open.
                using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            finally
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}
