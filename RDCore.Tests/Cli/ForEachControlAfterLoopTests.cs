using RDCore.Runtime.Compiled;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.4.2.4</strong>: what a <c>For Each</c> leaves in its control variable. The specification says the last element; MS-VBA empties the
/// variable when the loop runs out (<c>Empty</c>, or <c>Nothing</c> for an object variable), when it has no element at all, and when a <c>Next</c> finds no loop
/// running, the control variable is a <c>Variant</c> and the collection is not declared as an array. A loop left by <c>Exit For</c>, a jump or an error keeps
/// the element it was at. RD-VBA does what
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

    // the procedure a row's loop is written in: a procedure of its own when one of the row's procedures has it, else Probe.
    private static string LoopProcedureOf(string row)
        => System.Text.RegularExpressions.Regex.Match(row, @"(?:Sub|Function) (\w+)\([^)]*\)[^@]*?For Each") is { Success: true } match && row.IndexOf(" @@ ", StringComparison.Ordinal) > match.Index
            ? match.Groups[1].Value
            : "Probe";

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
                // the loop is stepped by the interpreter inside compiled code: the procedure the loop is written in is itself compiled.
                Assert.IsNotNull(CompiledDirectCallTests.BodyOf(provider, LoopProcedureOf(row)));
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
        ("Exit For over an array keeps the element", "Dim x @@ x = \"pre\" ;; For Each x In Array(1, 2, 3) ;;     n = n + 1 ;;     If x = 2 Then Exit For ;; Next ;; acc = acc & n & \":\" & x & \"|\" & Err.Number & \" ~\" & TypeName(x)", "2:2|0 ~Integer"),
        ("Exit For over a Collection keeps the element", "Dim x @@ Dim c As New Collection ;; c.Add \"a\" ;; c.Add \"b\" ;; c.Add \"c\" ;; x = \"pre\" ;; For Each x In c ;;     n = n + 1 ;;     If x = \"b\" Then Exit For ;; Next ;; acc = acc & n & \":\" & x & \"|\" & Err.Number & \" ~\" & TypeName(x)", "2:b|0 ~String"),
        ("Exit For with an Object variable keeps the object", "Dim src As New Collection ;; Dim s1 As New Source ;; Dim s2 As New Source ;; Dim s3 As New Source ;; s1.Items.Add \"a\" ;; s2.Items.Add \"b\" ;; s3.Items.Add \"c\" ;; src.Add s1 ;; src.Add s2 ;; src.Add s3 ;; Dim o As Object ;; For Each o In src ;;     n = n + 1 ;;     If o.Items(1) = \"b\" Then Exit For ;; Next ;; acc = acc & n & \":\" & (o Is Nothing) & \"|\" & Err.Number ;; If Not o Is Nothing Then acc = acc & \" item=\" & o.Items(1)", "2:False|0 item=b"),
        ("GoTo out of the loop keeps the element", "Dim x @@ x = \"pre\" ;; For Each x In Array(1, 2, 3) ;;     n = n + 1 ;;     If x = 2 Then GoTo bail ;; Next ;; bail: ;; acc = acc & n & \":\" & x & \"|\" & Err.Number & \" ~\" & TypeName(x)", "2:2|0 ~Integer"),
        ("Exit Sub out of the loop keeps the element", "Dim x ^^ Sub Walk() ;;     For Each x In Array(1, 2, 3) ;;         n = n + 1 ;;         If x = 2 Then Exit Sub ;;     Next ;; End Sub @@ x = \"pre\" ;; Walk ;; acc = acc & n & \":\" & x & \"|\" & Err.Number & \" ~\" & TypeName(x)", "2:2|0 ~Integer"),
        ("an error handled outside the loop keeps the element", "Dim x ^^ Sub WalkHandled() ;;     On Error GoTo h ;;     For Each x In Array(1, 2, 3) ;;         n = n + 1 ;;         If x = 2 Then Err.Raise 5 ;;     Next ;; h: ;; End Sub @@ x = \"pre\" ;; WalkHandled ;; acc = acc & n & \":\" & x & \"|\" & Err.Number & \" ~\" & TypeName(x)", "2:2|0 ~Integer"),
        ("an error raised to the caller keeps the element", "Dim x ^^ Sub WalkRaise() ;;     For Each x In Array(1, 2, 3) ;;         n = n + 1 ;;         If x = 2 Then Err.Raise 5 ;;     Next ;; End Sub @@ x = \"pre\" ;; WalkRaise ;; acc = acc & n & \":\" & x & \"|\" & Err.Number & \" ~\" & TypeName(x)", "2:2|5 ~Integer"),
        ("a loop in a callee that runs out empties the module variable", "Dim x ^^ Sub WalkAll() ;;     For Each x In Array(1, 2, 3) ;;         n = n + 1 ;;     Next ;; End Sub @@ x = \"pre\" ;; WalkAll ;; acc = acc & n & \":\" & x & \"|\" & Err.Number & \" ~\" & TypeName(x)", "3:|0 ~Empty"),
        ("an empty Collection empties the variable", "Dim x @@ Dim ec As New Collection ;; {CARD:ec}", "0::|0 ~Empty"),
        ("Array() empties the variable", "Dim x @@ {CARD:Array()}", "0::|0 ~Empty"),
        ("Split(\"\") empties the variable", "Dim x @@ {CARD:Split(\"\")}", "0::|0 ~Empty"),
        ("an unallocated Long array: Next raises 92 and keeps the variable", "Dim x @@ Dim ua() As Long ;; {CARD:ua}", "1: [pre]:pre|92 ~String"),
        ("a local Variant over an array, run out", "Dim y ;; y = \"pre\" ;; For Each y In Array(1, 2) ;;     n = n + 1 ;;     trail = trail & \" [\" & y & \"]\" ;; Next ;; acc = acc & n & \":\" & trail & \":\" & y & \"|\" & Err.Number & \" ~\" & TypeName(y)", "2: [1] [2]:|0 ~Empty"),
        ("a local Variant over a Collection, run out", "Dim c As New Collection ;; c.Add \"a\" ;; c.Add \"b\" ;; Dim y ;; y = \"pre\" ;; For Each y In c ;;     n = n + 1 ;;     trail = trail & \" [\" & y & \"]\" ;; Next ;; acc = acc & n & \":\" & trail & \":\" & y & \"|\" & Err.Number & \" ~\" & TypeName(y)", "2: [a] [b]:|0 ~Empty"),
        ("a local Variant over an Empty Variant: Next raises 92", "Dim v ;; Dim y ;; y = \"pre\" ;; For Each y In v ;;     n = n + 1 ;;     trail = trail & \" [\" & y & \"]\" ;; Next ;; acc = acc & n & \":\" & trail & \":\" & y & \"|\" & Err.Number & \" ~\" & TypeName(y)", "1: [pre]:|92 ~Empty"),
        ("a Static local over an array, run out", "Static z ;; z = \"pre\" ;; For Each z In Array(1, 2) ;;     n = n + 1 ;;     trail = trail & \" [\" & z & \"]\" ;; Next ;; acc = acc & n & \":\" & trail & \":\" & z & \"|\" & Err.Number & \" ~\" & TypeName(z)", "2: [1] [2]:|0 ~Empty"),
        ("a module As Variant over an array, run out", "Dim x As Variant @@ {CARD:Array(1, 2)}", "2: [1] [2]:|0 ~Empty"),
        ("a local As Variant over an array, run out", "Dim y As Variant ;; y = \"pre\" ;; For Each y In Array(1, 2) ;;     n = n + 1 ;;     trail = trail & \" [\" & y & \"]\" ;; Next ;; acc = acc & n & \":\" & trail & \":\" & y & \"|\" & Err.Number & \" ~\" & TypeName(y)", "2: [1] [2]:|0 ~Empty"),
        ("an Empty Variant: the opener raises 13, Next 92", "Dim x @@ Dim v ;; x = \"pre\" ;; For Each x In v ;;     n = n + 1 ;;     trail = trail & \" [\" & x & \" e\" & Err.Number & \"]\" ;;     Err.Clear ;;     If n >= 4 Then Exit For ;; Next ;; acc = acc & n & \":\" & trail & \":\" & x & \"|\" & Err.Number & \" ~\" & TypeName(x)", "1: [pre e13]:|92 ~Empty"),
        ("an assignment in the body is lost when the array runs out", "Dim x @@ x = \"pre\" ;; For Each x In Array(1, 2) ;;     n = n + 1 ;;     x = \"mod\" & n ;; Next ;; acc = acc & n & \":\" & x & \"|\" & Err.Number & \" ~\" & TypeName(x)", "2:|0 ~Empty"),
        ("Exit For on the last element keeps it", "Dim x @@ x = \"pre\" ;; For Each x In Array(1, 2) ;;     n = n + 1 ;;     If x = 2 Then Exit For ;; Next ;; acc = acc & n & \":\" & x & \"|\" & Err.Number & \" ~\" & TypeName(x)", "2:2|0 ~Integer"),
        ("a Collection that is Nothing: Next raises 92", "Dim x @@ Dim nc As Collection ;; {CARD:nc}", "1: [pre]:|92 ~Empty"),
        ("an Object that is Nothing: Next raises 92", "Dim x @@ Dim oo As Object ;; {CARD:oo}", "1: [pre]:|92 ~Empty"),
        ("an object with no enumeration member: Next raises 92", "Dim x @@ Dim so As New Source ;; {CARD:so}", "1: [pre]:|92 ~Empty"),
        ("a Function As Variant() that raises: Next raises 92 and keeps the variable", "Dim x ^^ Function BoomVArr() As Variant() ;;     Dim d As Double ;;     d = 1 / 0 ;; End Function @@ {CARD:BoomVArr()}", "1: [pre]:pre|92 ~String"),
        ("a ByRef parameter over an array empties the argument", "Dim x ^^ Sub WalkParam(ByRef p2 As Variant) ;;     For Each p2 In Array(1, 2) ;;         n = n + 1 ;;     Next ;; End Sub @@ x = \"pre\" ;; WalkParam x ;; acc = acc & n & \":\" & x & \"|\" & Err.Number & \" ~\" & TypeName(x)", "2:|0 ~Empty"),
        ("a Variant holding an array, run out", "Dim x @@ Dim va ;; va = Array(1, 2) ;; {CARD:va}", "2: [1] [2]:|0 ~Empty"),
        ("an assignment in the body is lost when the Collection runs out", "Dim x @@ Dim c As New Collection ;; c.Add \"a\" ;; c.Add \"b\" ;; x = \"pre\" ;; For Each x In c ;;     n = n + 1 ;;     x = \"mod\" & n ;; Next ;; acc = acc & n & \":\" & x & \"|\" & Err.Number & \" ~\" & TypeName(x)", "2:|0 ~Empty"),
        ("an unallocated Variant array: Next raises 92 and keeps the variable", "Dim x @@ Dim ua() As Variant ;; {CARD:ua}", "1: [pre]:pre|92 ~String"),
        ("Exit For on the last member of a Collection keeps it", "Dim x @@ Dim c As New Collection ;; c.Add \"a\" ;; c.Add \"b\" ;; x = \"pre\" ;; For Each x In c ;;     n = n + 1 ;;     If x = \"b\" Then Exit For ;; Next ;; acc = acc & n & \":\" & x & \"|\" & Err.Number & \" ~\" & TypeName(x)", "2:b|0 ~String"),
        ("GoTo into the body over Array(): Next raises 92 and empties the variable", "Dim x @@ x = \"pre\" ;; GoTo inside ;; For Each x In Array(1, 2) ;; inside: ;;     n = n + 1 ;;     If n >= 4 Then Exit For ;; Next ;; acc = acc & n & \":\" & x & \"|\" & Err.Number & \" ~\" & TypeName(x)", "1:|92 ~Empty"),
        ("GoTo into the body over a declared array: Next raises 92 and keeps the variable", "Dim x @@ Dim al(1) As Long ;; x = \"pre\" ;; GoTo inside ;; For Each x In al ;; inside: ;;     n = n + 1 ;;     If n >= 4 Then Exit For ;; Next ;; acc = acc & n & \":\" & x & \"|\" & Err.Number & \" ~\" & TypeName(x)", "1:pre|92 ~String"),
        ("GoTo into the body with an Object variable: Next raises 92 and keeps the object", "Dim c As New Collection ;; c.Add \"a\" ;; Dim s1 As New Source ;; s1.Items.Add \"pre\" ;; Dim o As Object ;; Set o = s1 ;; GoTo inside ;; For Each o In c ;; inside: ;;     n = n + 1 ;;     If n >= 4 Then Exit For ;; Next ;; acc = acc & n & \":\" & (o Is Nothing) & \"|\" & Err.Number", "1:False|92"),
        ("GoTo into the body over a Collection: Next raises 92 and empties the variable", "Dim x @@ Dim c As New Collection ;; c.Add \"a\" ;; x = \"pre\" ;; GoTo inside ;; For Each x In c ;; inside: ;;     n = n + 1 ;;     If n >= 4 Then Exit For ;; Next ;; acc = acc & n & \":\" & x & \"|\" & Err.Number & \" ~\" & TypeName(x)", "1:|92 ~Empty"),
        ("an Object variable over a Collection that is Nothing: Next raises 92 and keeps the object", "Dim nc As Collection ;; Dim s1 As New Source ;; s1.Items.Add \"pre\" ;; Dim o As Object ;; Set o = s1 ;; For Each o In nc ;;     n = n + 1 ;;     If n >= 4 Then Exit For ;; Next ;; acc = acc & n & \":\" & (o Is Nothing) & \"|\" & Err.Number", "1:False|92"),
        ("GoTo into a second loop over a declared array keeps the variable", "Dim x @@ Dim al(1) As Long ;; x = \"pre\" ;; For Each x In al ;;     n = n + 1 ;;     If n >= 4 Then Exit For ;; Next ;; x = \"again\" ;; GoTo inside2 ;; For Each x In al ;; inside2: ;;     n = n + 1 ;;     If n >= 9 Then Exit For ;; Next ;; acc = acc & n & \":\" & x & \"|\" & Err.Number & \" ~\" & TypeName(x)", "3:again|92 ~String"),
    ];
}
