using RDCore.External;
using RDCore.Runtime.StdLib;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.2.8.1.14</strong> <c>Partition</c> and <strong>§6.1.2.8.1.3</strong> <c>Command</c>, as MS-VBA has them, recorded in Excel
/// (oracle\s27 grid-p). Each case is the probe as it ran there, and what it printed.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.8.1.14 Partition")]
[TestCategory("MS-VBAL 6.1.2.8.1.3 Command")]
public sealed class PartitionAndCommandTests
{
    // P x prints x; what the body prints is the printed values, each followed by |.
    private static string Run(string body)
    {
        var output = new RuntimeOutputBuffer();
        var lines = body.Split(" ;; ").Select(line => line.StartsWith("P ") ? "Debug.Print " + line[2..] : line).Prepend("On Error Resume Next");
        var (_, outcome) = RuntimeSourceHarness.Run(null, [], output, standardLibrary: true, [.. lines]);
        Assert.AreNotEqual(RDCore.Runtime.Execution.RuntimeExecutionOutcomeKind.Error, outcome.Kind, outcome.ErrorInfo?.Verbose);
        return string.Concat(output.Lines.Select(line => line.Trim() + "|"));
    }

    [TestMethod]
    [DataRow("""P "[" & Partition(-1, 0, 99, 5) & "]" ;; P "[" & Partition(0, 0, 99, 5) & "]" ;; P "[" & Partition(97, 0, 99, 5) & "]" ;; P "[" & Partition(100, 0, 99, 5) & "]" ;; P "[" & Partition(19, 20, 199, 10) & "]" ;; P "[" & Partition(20, 20, 199, 10) & "]" ;; P "[" & Partition(195, 20, 199, 10) & "]" ;; P "[" & Partition(200, 20, 199, 10) & "]" ;; P "[" & Partition(99, 100, 1010, 20) & "]" ;; P "[" & Partition(100, 100, 1010, 20) & "]" ;; P "[" & Partition(1005, 100, 1010, 20) & "]" ;; P "[" & Partition(1011, 100, 1010, 20) & "]" """,
        "[   : -1]|[  0:  4]|[ 95: 99]|[100:   ]|[   : 19]|[ 20: 29]|[190:199]|[200:   ]|[    :  99]|[ 100: 119]|[1000:1010]|[1011:    ]|", DisplayName = "P01 the spec's table")]
    [DataRow("""P "[" & Partition(100, 0, 1000, 1) & "]" ;; P "[" & Partition(5, 0, 9, 1) & "]" ;; P "[" & Partition(-3, 0, 9, 1) & "]" ;; P "[" & Partition(12, 0, 9, 1) & "]" ;; P "[" & Partition(0, 0, 9, 1) & "]" ;; P "[" & Partition(9, 0, 9, 1) & "]" """,
        "[ 100: 100]|[ 5: 5]|[  :-1]|[10:  ]|[ 0: 0]|[ 9: 9]|", DisplayName = "P02 an interval of one")]
    [DataRow("""P TypeName(Partition(Null, 0, 9, 2)) ;; P TypeName(Partition(1, Null, 9, 2)) ;; P TypeName(Partition(1, 0, Null, 2)) ;; P TypeName(Partition(1, 0, 9, Null)) ;; Err.Clear ;; P TypeName(Partition(Null, -1, 9, 2)) ;; P Err.Number ;; Err.Clear ;; P TypeName(Partition(Null, 5, 5, 0)) ;; P Err.Number ;; P TypeName(Partition(1, 0, 9, 2))""",
        "Null|Null|Null|Null|Null|0|Null|0|String|", DisplayName = "P03 Null, before anything else")]
    [DataRow("""Err.Clear ;; P Partition(1, -1, 9, 2) ;; P Err.Number & ":" & Err.Description ;; Err.Clear ;; P Partition(5, 5, 5, 1) ;; P Err.Number ;; Err.Clear ;; P Partition(5, 6, 5, 1) ;; P Err.Number ;; Err.Clear ;; P Partition(5, 0, 9, 0) ;; P Err.Number ;; Err.Clear ;; P Partition(5, 0, 9, -1) ;; P Err.Number ;; Err.Clear ;; P "[" & Partition(-5, 0, 9, 2) & "]" ;; P Err.Number""",
        "5:Invalid procedure call or argument|5|5|5|5|[  :-1]|0|", DisplayName = "P04 what is no range")]
    [DataRow("""Err.Clear ;; P "[" & Partition(2.5, 0, 9, 2) & "]" ;; P "[" & Partition(3.5, 0, 9, 2) & "]" ;; P "[" & Partition("7", "0", "9", "2") & "]" ;; P "[" & Partition(True, 0, 9, 2) & "]" ;; P "[" & Partition(5, 0.5, 9.5, 2.5) & "]" ;; P "[" & Partition(Empty, 0, 9, 2) & "]" ;; P Err.Number ;; Err.Clear ;; P "[" & Partition(1E10, 0, 9, 2) & "]" ;; P Err.Number ;; Err.Clear ;; P Partition("x", 0, 9, 2) ;; P Err.Number ;; Err.Clear ;; P "[" & Partition(5, 0, 2147483647, 1) & "]" ;; P Err.Number""",
        "[ 2: 3]|[ 4: 5]|[ 6: 7]|[  :-1]|[ 4: 5]|[ 0: 1]|0|[10:  ]|0|13|[         5:         5]|0|", DisplayName = "P05 what each part is read as")]
    [DataRow("""P "[" & Partition(5, 0, 9, 2) & "]" ;; P "[" & Partition(5, 0, 10, 2) & "]" ;; P "[" & Partition(50, 0, 99, 100) & "]" ;; P "[" & Partition(150, 0, 99, 100) & "]" ;; P "[" & Partition(-50, 0, 99, 100) & "]" ;; P "[" & Partition(0, 0, 1, 1) & "]" ;; P "[" & Partition(9, 1, 10, 3) & "]" ;; P "[" & Partition(10, 1, 10, 3) & "]" ;; P "[" & Partition(-1000, 0, 9, 2) & "]" ;; P "[" & Partition(123456, 0, 9, 2) & "]" """,
        "[ 4: 5]|[ 4: 5]|[  0: 99]|[100:   ]|[   : -1]|[ 0: 0]|[ 7: 9]|[10:10]|[  :-1]|[10:  ]|", DisplayName = "P06 widths")]
    [DataRow("""Err.Clear ;; P "[" & Partition(5, 0, 9, 2) & "]" ;; P "[" & Partition(-2147483648#, 0, 9, 2) & "]" ;; P Err.Number ;; Err.Clear ;; P "[" & Partition(2147483647, 0, 9, 2) & "]" ;; P Err.Number ;; Err.Clear ;; P "[" & Partition(5, 0, 9, 2147483647) & "]" ;; P Err.Number ;; Err.Clear ;; P "[" & Partition(2147483647, 0, 2147483646, 1) & "]" ;; P Err.Number""",
        "[ 4: 5]|[  :-1]|0|[10:  ]|0|[ 0: 9]|0|[2147483647:          ]|0|", DisplayName = "P07 the ends of a Long")]
    public void Partition_AsMSVBA(string body, string printed) => Assert.AreEqual(printed, Run(body));

    // C01: in a program an Office application runs, there are none.
    [TestMethod]
    public async Task Command_IsAString_EmptyUnlessTheHostGivesOne()
        => CollectionAssert.AreEqual(new[] { "String", "String", "[]", "0", "String" }, await ModuleWorkspace.RunAsync([],
            "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nDebug.Print TypeName(Command)\r\nDebug.Print TypeName(Command$)\r\nDebug.Print \"[\" & Command & \"]\"\r\nDebug.Print Len(Command())\r\nDebug.Print TypeName(VBA.Interaction.Command)\r\nEnd Sub\r\n"));

    [TestMethod]
    public void Command_IsWhatTheHostSaysTheProgramWasStartedWith()
    {
        var module = new StdInteraction(NSubstitute.Substitute.For<IRuntimeSession>(), ExternalWorld.None with { CommandLine = "/a 1" });

        Assert.AreEqual("/a 1", ((VBStringValue)module.Command().Result!.TypedValue).Value);
        Assert.AreEqual("/a 1", module.CommandString().Result!.Value);
    }
}
