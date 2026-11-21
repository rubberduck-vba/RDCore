namespace RDCore.CLI.App.Mcp;

// under `rdc mcp`, the process's standard output carries the MCP messages and nothing else: it is taken before anything writes to the console, and from
// then on what is written to the console (a log, a fatal error, the help of a command line) goes to standard error.
internal static class McpStandardStreams
{
    private static Stream? _output;

    public static Stream Input { get; } = global::System.Console.OpenStandardInput();

    public static Stream Output => _output ?? throw new InvalidOperationException("the standard output was not claimed for MCP");

    public static void Claim()
    {
        if (_output is not null)
        {
            return;
        }

        _output = global::System.Console.OpenStandardOutput();
        global::System.Console.SetOut(global::System.Console.Error);
    }
}
