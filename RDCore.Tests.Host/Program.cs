using RDCore.Runtime.Hosting;
using RDCore.SDK.Runtime.Hosting;
using System.Text.Json.Nodes;

// started as the external host of its own machines, it is that and nothing else.
if (ProgramExternalHost.IsThisProcess)
{
    return ProgramExternalHost.Run(args);
}

// started by a test with a composition, the folder of its libraries, a module and a procedure: it says its own executable, which its machines' external host
// is started from, then runs the procedure in a machine and says what it returned, or how it ended.
var executable = ProgramExternalHost.Executable;
Console.WriteLine(executable ?? "(none)");
var options = new ComposedProgramOptions { LibrariesFolder = args[1], ExternalHost = executable };
await using var program = await ComposedProgramFile.LoadAsync(args[0], options);
await using var machine = await program.CreateMachineAsync(default);
var result = await machine.InvokeAsync(new ProgramInvocation(args[2], args[3], new Dictionary<string, JsonNode?>()), default);
Console.WriteLine(result.Value?.ToJsonString() ?? $"{result.Outcome} {result.ErrorMessage}");
return 0;
