using RDCore.Runtime.Compiled;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.4.2.4</strong>: what a <c>For Each</c> leaves in its control variable. The specification says the last element; MS-VBA empties the
/// variable when the loop runs out (<c>Empty</c>, or <c>Nothing</c> for an object variable), when it has no element at all, and when a <c>Next</c> finds no loop
/// running and the collection is not declared as an array. A loop left by <c>Exit For</c>, a jump or an error keeps the element it was at. RD-VBA does what
/// MS-VBA does (<strong>RD-VBAL §5.4.2.4</strong>), in the interpreter and in compiled code.
/// </summary>
/// <remarks>
/// Each row is a program and what MS-VBA prints for it, recorded in Excel. The program runs under <c>On Error Resume Next</c>; <c>{CARD:c}</c> stands for a loop
/// over <c>c</c> into the module's <c>x</c>, which was <c>"pre"</c> before it, that prints how many times the body ran, what it saw, then <c>x</c>, the last
/// error, and <c>TypeName(x)</c>.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.4.2.4 For Each Statement")]
public sealed class ForEachControlAfterLoopTests
{
    private const string Card = """x = "pre" ;; For Each x In {0} ;;     n = n + 1 ;;     trail = trail & " [" & x & "]" ;;     If n >= 4 Then Exit For ;; Next ;; """
        + """acc = acc & n & ":" & trail & ":" & x & "|" & Err.Number & " ~" & TypeName(x)""";

    private static readonly (string Name, string Source)[] Source =
        [("Source", ModuleWorkspace.ClassModule("Source", "Option Explicit", "Public Items As New Collection"))];

    // the program of a row: "<module declarations> ^^ <procedures> @@ <lines of Probe>", lines split on " ;; ".
    private static string Program(string row)
    {
        var body = System.Text.RegularExpressions.Regex.Replace(row, @"\{CARD:(.*)\}", match => string.Format(Card, match.Groups[1].Value));
        string declarations = string.Empty, procedures = string.Empty, probe = body;
        if (body.Contains(" @@ "))
        {
            var head = body[..body.IndexOf(" @@ ", StringComparison.Ordinal)];
            probe = body[(head.Length + 4)..];
            (declarations, procedures) = head.Contains(" ^^ ") ? (head[..head.IndexOf(" ^^ ", StringComparison.Ordinal)], head[(head.IndexOf(" ^^ ", StringComparison.Ordinal) + 4)..]) : (head, string.Empty);
        }

        static string Lines(string text, string indent)
            => string.Join("\r\n", text.Split(" ;; ").Select(line => System.Text.RegularExpressions.Regex.IsMatch(line, @"^\w+:$") ? line : indent + line));

        return string.Join("\r\n",
            "Attribute VB_Name = \"Program\"", "Option Explicit", "Private acc As String", "Dim n As Long, trail", Lines(declarations, string.Empty),
            "Public Sub Main()", "    Debug.Print Probe()", "End Sub",
            "Public Function Probe() As String", "    acc = \"\"", "    On Error Resume Next", Lines(probe, "    "), "    Probe = acc", "End Function",
            "Function BoomV()", "    Dim d As Double", "    d = 1 / 0", "End Function",
            "Function BoomArr() As Long()", "    Dim d As Double", "    d = 1 / 0", "End Function",
            Lines(procedures, string.Empty)) + "\r\n";
    }

    private static async Task<string> RunAsync(string row, VBExecutionEngine engine)
    {
        var printed = string.Empty;
        using var running = CompiledExecution.Running(engine);
        await ModuleWorkspace.InspectAsync(Source, Program(row), (provider, run, _) =>
        {
            Assert.AreEqual(SDK.Platform.Protocol.ExecutionOutcome.Completed, run.Outcome, $"{run.ErrorNumber} {run.ErrorMessage}");
            printed = string.Join(" ; ", run.Output.Select(line => line.Trim()));
            if (engine is VBExecutionEngine.Compiled)
            {
                // the loop is stepped by the interpreter inside compiled code: Probe itself is compiled.
                Assert.IsNotNull(CompiledDirectCallTests.BodyOf(provider, "Probe"));
            }

            return Task.CompletedTask;
        }, engine: engine);

        return printed;
    }

    public static IEnumerable<object[]> Rows => from row in MeasuredRows
                                                 from engine in new[] { VBExecutionEngine.Interpreter, VBExecutionEngine.Compiled }
                                                 select new object[] { row.Name, row.Program, row.Printed, engine };

    public static string NameOf(System.Reflection.MethodInfo _, object[] data) => $"{data[0]} ({data[3]})";

    [TestMethod]
    [DynamicData(nameof(Rows), DynamicDataDisplayName = nameof(NameOf))]
    public async Task TheControlVariable_AfterTheLoop_IsWhatMSVBALeavesInIt(string name, string row, string printed, VBExecutionEngine engine)
    {
        if (engine is VBExecutionEngine.Compiled)
        {
            CompiledExecutionTests.RequirePayloads();
        }

        Assert.AreEqual(printed, await RunAsync(row, engine), name);
    }

    private static readonly (string Name, string Program, string Printed)[] MeasuredRows =
    [
        ("Array(1, 2), run out", "Dim x @@ {CARD:Array(1, 2)}", "2: [1] [2]:|0 ~Empty"),
        ("Split(\"a b\"), run out", "Dim x @@ {CARD:Split(\"a b\")}", "2: [a] [b]:|0 ~Empty"),
        ("a Long array, run out", "Dim x @@ Dim al(1) As Long ;; {CARD:al}", "2: [0] [0]:|0 ~Empty"),
        ("an array held in a Collection, run out", "Dim x @@ Dim c As Collection ;; Set c = New Collection ;; c.Add Array(5, 6), \"k\" ;; {CARD:c!k}", "2: [5] [6]:|0 ~Empty"),
        ("the Collection of an object, run out", "Dim x @@ Dim o ;; Set o = New Source ;; o.Items.Add \"a\" ;; o.Items.Add \"b\" ;; {CARD:o.Items}", "2: [a] [b]:|0 ~Empty"),
        ("an Empty Variant: Next raises 92", "Dim x @@ Dim v ;; {CARD:v}", "1: [pre]:|92 ~Empty"),
        ("a Variant Function that raises: Next raises 92", "Dim x @@ {CARD:BoomV()}", "1: [pre]:|92 ~Empty"),
        ("a Function As Long() that raises: Next raises 92 and keeps the variable", "Dim x @@ {CARD:BoomArr()}", "1: [pre]:pre|92 ~String"),
        ("an Object variable over objects, run out", "Dim src As New Collection ;; Dim s1 As New Source ;; Dim s2 As New Source ;; s1.Items.Add \"a\" ;; s2.Items.Add \"b\" ;; src.Add s1 ;; src.Add s2 ;; Dim o As Object ;; Set o = New Source ;; o.Items.Add \"pre\" ;; For Each o In src ;;     n = n + 1 ;;     trail = trail & \" [\" & o.Items(1) & \"]\" ;; Next ;; acc = acc & n & \":\" & trail & \":\" & (o Is Nothing) & \"|\" & Err.Number ;; If Not o Is Nothing Then acc = acc & \" item=\" & o.Items(1)", "2: [a] [b]:True|0"),
        ("a variable of a class over objects, run out", "Dim src As New Collection ;; Dim s1 As New Source ;; Dim s2 As New Source ;; s1.Items.Add \"a\" ;; s2.Items.Add \"b\" ;; src.Add s1 ;; src.Add s2 ;; Dim o As Source ;; Set o = New Source ;; o.Items.Add \"pre\" ;; For Each o In src ;;     n = n + 1 ;;     trail = trail & \" [\" & o.Items(1) & \"]\" ;; Next ;; acc = acc & n & \":\" & trail & \":\" & (o Is Nothing) & \"|\" & Err.Number ;; If Not o Is Nothing Then acc = acc & \" item=\" & o.Items(1)", "2: [a] [b]:True|0"),
        ("a Variant over objects, run out", "Dim src As New Collection ;; Dim s1 As New Source ;; Dim s2 As New Source ;; s1.Items.Add \"a\" ;; s2.Items.Add \"b\" ;; src.Add s1 ;; src.Add s2 ;; Dim y ;; y = \"pre\" ;; For Each y In src ;;     n = n + 1 ;;     trail = trail & \" [\" & y.Items(1) & \"]\" ;; Next ;; acc = acc & n & \":\" & trail & \":\" & TypeName(y) & \"|\" & Err.Number ;; If IsObject(y) Then If Not y Is Nothing Then acc = acc & \" item=\" & y.Items(1)", "2: [a] [b]:Empty|0"),
        ("an Object variable over an empty Collection", "Dim s1 As New Source ;; s1.Items.Add \"pre\" ;; Dim ec As New Collection ;; Dim o As Object ;; Set o = s1 ;; For Each o In ec ;;     n = n + 1 ;; Next ;; acc = acc & n & \":\" & (o Is Nothing) & \"|\" & Err.Number ;; If Not o Is Nothing Then acc = acc & \" item=\" & o.Items(1)", "0:True|0"),
        ("a Variant over an array of Object, run out", "Dim os(1) As Object ;; Set os(0) = New Collection ;; Set os(1) = New Collection ;; os(0).Add \"a\" ;; os(1).Add \"b\" ;; Dim y ;; y = \"pre\" ;; For Each y In os ;;     n = n + 1 ;;     trail = trail & \" [\" & y(1) & \"]\" ;; Next ;; acc = acc & n & \":\" & trail & \":\" & TypeName(y) & \"|\" & Err.Number ;; If IsObject(y) Then If Not y Is Nothing Then acc = acc & \" item=\" & y(1)", "2: [a] [b]:Empty|0"),
    ];
}
