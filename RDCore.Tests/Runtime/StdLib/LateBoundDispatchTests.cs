using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// A member of an object of the standard library called late-bound - the object reached through an expression declared <c>Object</c> or <c>Variant</c> -
/// is called as MS-VBA calls it, through the object's <c>IDispatch</c>, whose errors are not those of a call bound to the member. Recorded in Excel
/// (oracle\s26: grid-lb to grid-lb6, early-bound controls in grid-lb2 and grid-lb4): each case is the probe as it ran there, and what it printed.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL 6 Standard library")]
public sealed class LateBoundDispatchTests
{
    // P x prints x; what the body prints is the printed values, each followed by |.
    private static string Run(string body)
    {
        var output = new RuntimeOutputBuffer();
        var lines = body.Split(" ;; ").Select(line => line.StartsWith("P ") ? "Debug.Print " + line[2..] : line).Prepend("On Error Resume Next");
        var (_, outcome) = RuntimeSourceHarness.Run(null, [], output, standardLibrary: true, [.. lines]);
        Assert.AreNotEqual(RDCore.Runtime.Execution.RuntimeExecutionOutcomeKind.Error, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreNotEqual(RDCore.Runtime.Execution.RuntimeExecutionOutcomeKind.InternalError, outcome.Kind);
        return string.Concat(output.Lines.Select(line => line.Trim() + "|"));
    }

    // the same, in a workspace's module: what declares a variable of a class of the standard library (Dim m As MatchCollection).
    private static async Task<string> RunInWorkspace(string body)
    {
        var lines = body.Split(" ;; ").Select(line => line.StartsWith("P ") ? "Debug.Print " + line[2..] : line);
        var output = await Cli.ModuleWorkspace.RunAsync([], $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nOn Error Resume Next\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n");
        return string.Concat(output.Select(line => line.Trim() + "|"));
    }

    private const string Matched = """Dim r As Object, m As Object ;; Set r = CreateObject("VBScript.RegExp") ;; r.Pattern = "(a)" ;; Set m = r.Execute("a") ;; """;

    // IDispatch converts each argument to the type of its parameter, a property's value to the type of the property: Null converts to none, a type mismatch.
    [TestMethod]
    [DataRow("""Dim e As Object ;; Set e = Err ;; Err.Clear ;; e.Description = Null ;; P Err.Number ;; Err.Clear ;; e.HelpContext = Null ;; P Err.Number ;; Err.Clear ;; e.Raise Null ;; P Err.Number ;; Err.Clear ;; e.Number = "x" ;; P Err.Number""",
        "13|13|13|13|", DisplayName = "L06: ErrObject, a String, a Long, a Sub's argument")]
    [DataRow("""Dim r As Variant ;; Set r = CreateObject("VBScript.RegExp") ;; Err.Clear ;; r.Pattern = Null ;; P Err.Number ;; Err.Clear ;; P r.Test(Null) ;; P Err.Number ;; Err.Clear ;; r.Global = Null ;; P Err.Number ;; Err.Clear ;; r.Execute = 1 ;; P Err.Number ;; Err.Clear ;; r.Nope = 1 ;; P Err.Number""",
        "13|13|13|438|438|", DisplayName = "L07: through a Variant")]
    [DataRow(Matched + """Err.Clear ;; P m.Item(Null).Value ;; P Err.Number ;; Err.Clear ;; P m(Null).Value ;; P Err.Number ;; Err.Clear ;; P m(0).SubMatches(Null) ;; P Err.Number""",
        "13|13|13|", DisplayName = "L08: an index, named and default")]
    [DataRow("""Dim r As Object ;; Set r = CreateObject("VBScript.RegExp") ;; With r ;; Err.Clear ;; .Pattern = Null ;; P Err.Number ;; Err.Clear ;; P .Test(Null) ;; P Err.Number ;; End With ;; With CreateObject("VBScript.RegExp") ;; Err.Clear ;; .Global = Null ;; P Err.Number ;; End With""",
        "13|13|13|", DisplayName = "M07: the target of a With block")]
    public void LateBound_NullIsATypeMismatch(string body, string expected)
        => Assert.AreEqual(expected, Run(body));

    // early-bound, the call is bound to the member, and coerces as any call does: Null is error 94.
    [TestMethod]
    public void EarlyBound_NullIsAnInvalidUseOfNull()
        => Assert.AreEqual("94|94|94|13|", Run(
            """Err.Clear ;; Err.Description = Null ;; P Err.Number ;; Err.Clear ;; Err.HelpContext = Null ;; P Err.Number ;; Err.Clear ;; Err.Raise Null ;; P Err.Number ;; Err.Clear ;; Err.Number = "x" ;; P Err.Number"""));

    // N01: a match is an Object, as MatchCollection.Item is declared: what is called on one is late-bound, however the collection is declared.
    [TestMethod]
    public async Task EarlyBound_AMatchIsAnObject()
        => Assert.AreEqual("94|13|94|", await RunInWorkspace(
            """Dim r As New RegExp, m As MatchCollection ;; r.Pattern = "(a)" ;; Set m = r.Execute("a") ;; Err.Clear ;; P m(Null).Value ;; P Err.Number ;; Err.Clear ;; P m(0).SubMatches(Null) ;; P Err.Number ;; Err.Clear ;; With r ;; .Pattern = Null ;; P Err.Number ;; End With"""));
}
