using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Hosting;
using RDCore.Tests.Cli;
using System.Text.Json.Nodes;

namespace RDCore.Tests.Composer;

// the project the composer's tests compose, written to a folder: a .rdproj that lists a standard module, a class and the interface the class implements,
// each in its own file. Program.Bump counts what a machine ran, and Program.Total adds two numbers through the class.
internal static class ComposedProject
{
    // a cap on a hang, not a budget: each wait is on the invocation itself.
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(2);

    public static readonly string Program = ModuleWorkspace.StandardModule("Program",
        "Public Count As Long",
        "Public Sub Bump()",
        "Count = Count + 1",
        "End Sub",
        "Public Function Current() As Long",
        "Current = Count",
        "End Function",
        "Public Function Total(ByVal A As Long, ByVal B As Long) As Long",
        "Dim tally As ITally",
        "Set tally = New Counter",
        "tally.Add A",
        "tally.Add B",
        "Total = tally.Sum",
        "End Function");

    private static readonly string Counter = ModuleWorkspace.ClassModule("Counter",
        "Implements ITally",
        "Private sum As Long",
        "Private Sub ITally_Add(ByVal N As Long)",
        "sum = sum + N",
        "End Sub",
        "Private Property Get ITally_Sum() As Long",
        "ITally_Sum = sum",
        "End Property");

    private static readonly string Tally = ModuleWorkspace.ClassModule("ITally",
        "Public Sub Add(ByVal N As Long)",
        "End Sub",
        "Public Property Get Sum() As Long",
        "End Property");

    public static readonly string[] Files = ["Program.bas", "Counter.cls", "ITally.cls"];

    // a new folder of the temporary folder; the caller deletes it.
    public static string NewFolder() => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"rdcore-composer-{Guid.NewGuid():N}")).FullName;

    // the project written to root, with program as the source of Program.bas; the path of its .rdproj.
    public static string Write(string root, string? program = null)
    {
        File.WriteAllText(Path.Combine(root, "Program.bas"), program ?? Program);
        File.WriteAllText(Path.Combine(root, "Counter.cls"), Counter);
        File.WriteAllText(Path.Combine(root, "ITally.cls"), Tally);
        var project = Path.Combine(root, ".rdproj");
        File.WriteAllText(project, """
            { "Version": "0.1.0", "ProjectInfo": { "Name": "Composed", "Modules": [
              { "RelativeUri": "Program.bas" }, { "RelativeUri": "Counter.cls" }, { "RelativeUri": "ITally.cls" } ] } }
            """);
        return project;
    }

    public static Task<ProgramInvocationResult> Invoke(IProgramMachine machine, string procedure, string arguments = "{}")
        => machine.InvokeAsync(
            new ProgramInvocation("Program", procedure, JsonNode.Parse(arguments)!.AsObject().ToDictionary(
                pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.Ordinal)), default).WaitAsync(Patience);

    public static void AssertReturned(string expected, ProgramInvocationResult result)
    {
        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        Assert.AreEqual(expected, result.Value?.ToJsonString() ?? "null");
    }

    // what makes each machine its own: a count bumped in one is that machine's, and each runs the program's class through its interface.
    public static async Task AssertEachMachineHasItsOwnStateAsync(params IProgramMachine[] machines)
    {
        for (var index = 0; index < machines.Length; index++)
        {
            for (var bump = 0; bump < index; bump++)
            {
                Assert.AreEqual(ExecutionOutcome.Completed, (await Invoke(machines[index], "Bump")).Outcome);
            }
        }

        for (var index = 0; index < machines.Length; index++)
        {
            AssertReturned($"{index}", await Invoke(machines[index], "Current"));
            AssertReturned("42", await Invoke(machines[index], "Total", """{"A":40,"B":2}"""));
        }
    }
}
