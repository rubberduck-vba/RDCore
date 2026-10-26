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

    // a member with no Property Let is assigned through IDispatch: written with arguments, it is invoked and what it returns has its default member assigned
    // (451 when it is no object); with none, a property that returns no object is 450, a method 438. A Set is 450 for a property, 438 for a method.
    [TestMethod]
    [DataRow("""Dim c As Object ;; Set c = New Collection ;; c.Add 1, "k" ;; Err.Clear ;; c.Count = 3 ;; P Err.Number ;; Err.Clear ;; c.Item(1) = 5 ;; P Err.Number ;; Err.Clear ;; c.Add = 5 ;; P Err.Number ;; Err.Clear ;; Set c.Item(1) = c ;; P Err.Number ;; Err.Clear ;; Set c.Count = c ;; P Err.Number ;; Err.Clear ;; c.Nope = 1 ;; P Err.Number""",
        "438|451|438|438|438|438|", DisplayName = "L01: Collection, whose Count and Item are methods")]
    [DataRow("""Dim c As Object ;; Set c = New Collection ;; c.Add 1 ;; c.Add 2 ;; Err.Clear ;; c.Remove(1) = 5 ;; P Err.Number ;; P c.Count ;; Err.Clear ;; c.Add(3) = 5 ;; P Err.Number ;; P c.Count""",
        "451|1|451|2|", DisplayName = "M05: the method is invoked")]
    [DataRow("""Dim c As Variant ;; Set c = New Collection ;; c.Add 1 ;; c.Add 2 ;; Err.Clear ;; c.Item(1) = 5 ;; P Err.Number ;; P c(1)""",
        "451|1|", DisplayName = "M06: through a Variant")]
    [DataRow("""Dim c As Object, o As Object ;; Set c = New Collection ;; Set o = New Collection ;; c.Add o ;; Err.Clear ;; c.Item(1) = 5 ;; P Err.Number""",
        "438|", DisplayName = "M08: what it returns is an object whose default member takes an argument")]
    [DataRow("""Dim e As Object ;; Set e = Err ;; Err.Clear ;; e.LastDllError = 1 ;; P Err.Number ;; Err.Clear ;; e.Clear = 1 ;; P Err.Number ;; Err.Clear ;; e.Raise(5) = 1 ;; P Err.Number ;; Err.Clear ;; Set e.Clear = e ;; P Err.Number ;; Err.Clear ;; Set e.LastDllError = e ;; P Err.Number""",
        "450|438|5|438|450|", DisplayName = "L06, M02: ErrObject")]
    [DataRow("""Dim r As Object ;; Set r = CreateObject("VBScript.RegExp") ;; Err.Clear ;; r.Test("a") = True ;; P Err.Number ;; Err.Clear ;; Set r.Test("a") = r ;; P Err.Number ;; Err.Clear ;; Set r.Execute = r ;; P Err.Number ;; Err.Clear ;; r.Replace("a", "b") = 1 ;; P Err.Number""",
        "451|438|438|451|", DisplayName = "M01: RegExp")]
    [DataRow(Matched + """Err.Clear ;; m(0).SubMatches(0) = "x" ;; P Err.Number ;; Err.Clear ;; m(0).SubMatches.Count = 2 ;; P Err.Number ;; Err.Clear ;; m(0).Length = 2 ;; P Err.Number ;; Err.Clear ;; Set m.Item(0) = r ;; P Err.Number ;; Err.Clear ;; m.Item(0) = 1 ;; P Err.Number ;; Err.Clear ;; Set m.Count = r ;; P Err.Number""",
        "451|450|450|450|450|450|", DisplayName = "M03, L08: MatchCollection, Match, SubMatches")]
    [DataRow("""Dim r As Variant, m As Variant ;; Set r = CreateObject("VBScript.RegExp") ;; r.Pattern = "(a)" ;; Set m = r.Execute("a") ;; Err.Clear ;; m.Item(0) = 1 ;; P Err.Number ;; Err.Clear ;; m.Count = 2 ;; P Err.Number ;; Err.Clear ;; r.Test("a") = True ;; P Err.Number""",
        "450|450|451|", DisplayName = "M04: through a Variant")]
    [DataRow(Matched + """Err.Clear ;; m(0).SubMatches = 1 ;; P Err.Number ;; Err.Clear ;; Set m(0).SubMatches = r ;; P Err.Number ;; Err.Clear ;; Set m(0).SubMatches(0) = r ;; P Err.Number""",
        "438|438|450|", DisplayName = "P01: a property that returns an object")]
    [DataRow(Matched + """Dim c As Object, v As Variant ;; Set c = New Collection ;; c.Add m(0) ;; c.Add r ;; Err.Clear ;; c.Item(1) = "x" ;; P Err.Number ;; Err.Clear ;; c.Item(2) = "x" ;; P Err.Number ;; Set v = c ;; Err.Clear ;; v.Item(1) = "x" ;; P Err.Number""",
        "450|438|450|", DisplayName = "P02: the default member of what is returned")]
    [DataRow(Matched + """Dim mt As Object, co As Object ;; Set mt = m(0) ;; Err.Clear ;; mt = "x" ;; P Err.Number ;; Err.Clear ;; m = 1 ;; P Err.Number ;; Set co = New Collection ;; Err.Clear ;; co = 1 ;; P Err.Number ;; Err.Clear ;; r = 1 ;; P Err.Number""",
        "450|438|438|438|", DisplayName = "P03: an object variable, assigned its default member")]
    [DataRow(Matched + """Err.Clear ;; m(0).SubMatches(Null) = 1 ;; P Err.Number ;; Err.Clear ;; r.Test(Null) = 1 ;; P Err.Number ;; Err.Clear ;; m.Item(Null) = 1 ;; P Err.Number""",
        "13|13|13|", DisplayName = "P04: the arguments of the member invoked")]
    [DataRow(Matched + """Dim c As Object ;; Set c = New Collection ;; c.Add 1 ;; Err.Clear ;; m.Item = 1 ;; P Err.Number ;; Err.Clear ;; Set m.Item = r ;; P Err.Number ;; Err.Clear ;; c.Item = 1 ;; P Err.Number ;; Err.Clear ;; m(0).SubMatches.Item = 1 ;; P Err.Number ;; Err.Clear ;; r.Replace = 1 ;; P Err.Number""",
        "438|438|438|438|438|", DisplayName = "Q02: a member that takes arguments, given none")]
    [DataRow(Matched + """Err.Clear ;; m.Item(0, 1) = 1 ;; P Err.Number""",
        "450|", DisplayName = "Q03: a member given too many arguments")]
    public void LateBound_AMemberWithNoPropertyLet_IsAssignedThroughIDispatch(string body, string expected)
        => Assert.AreEqual(expected, Run(body));

    // what is indexed, assigned, is the default member given the arguments: through an Object it is assigned as when it is named; through a Variant it is not
    // invoked to have what it returns assigned, and is 450 for a property, 438 for a method.
    [TestMethod]
    [DataRow("""Dim c As Object, v As Variant ;; Set c = New Collection ;; c.Add 1 ;; c.Add 2 ;; Err.Clear ;; c(1) = 5 ;; P Err.Number ;; P c(1) ;; Set v = c ;; Err.Clear ;; v(1) = 5 ;; P Err.Number ;; P v(1)""",
        "451|1|438|1|", DisplayName = "L01, L02, M05, M06: Collection, through an Object and a Variant")]
    [DataRow("""Dim c As Object, o As Object ;; Set c = New Collection ;; Set o = New Collection ;; c.Add o ;; Err.Clear ;; c(1) = 5 ;; P Err.Number""",
        "438|", DisplayName = "M08: what it returns is an object whose default member takes an argument")]
    [DataRow(Matched + """Dim v As Variant ;; Err.Clear ;; m(0) = 1 ;; P Err.Number ;; Set v = m ;; Err.Clear ;; v(0) = 1 ;; P Err.Number""",
        "450|450|", DisplayName = "M03, M04: MatchCollection, through an Object and a Variant")]
    [DataRow(Matched + """Dim s As Object, v As Variant ;; Set s = m(0).SubMatches ;; Err.Clear ;; s(0) = "x" ;; P Err.Number ;; Set v = m(0).SubMatches ;; Err.Clear ;; v(0) = "x" ;; P Err.Number ;; Set v = m ;; Err.Clear ;; v(0) = "x" ;; P Err.Number""",
        "451|450|450|", DisplayName = "P01: SubMatches, through an Object and a Variant")]
    [DataRow(Matched + """Dim c As Object, v As Variant ;; Set c = New Collection ;; c.Add m(0) ;; c.Add r ;; Err.Clear ;; c(1) = "x" ;; P Err.Number ;; Err.Clear ;; c(2) = "x" ;; P Err.Number ;; Set v = c ;; Err.Clear ;; v(1) = "x" ;; P Err.Number""",
        "450|438|438|", DisplayName = "P02: the default member of what is returned")]
    [DataRow(Matched + """Dim c As Object, v As Variant ;; Set c = New Collection ;; c.Add r ;; Err.Clear ;; Set c(1) = r ;; P Err.Number ;; Err.Clear ;; Set m(0) = r ;; P Err.Number ;; Set v = c ;; Err.Clear ;; Set v(1) = r ;; P Err.Number ;; Set v = m ;; Err.Clear ;; Set v(0) = r ;; P Err.Number""",
        "438|450|438|450|", DisplayName = "Q01: a Set")]
    [DataRow(Matched + """Dim v As Variant ;; Err.Clear ;; m(0, 1) = 1 ;; P Err.Number ;; Err.Clear ;; m.Item(0, 1) = 1 ;; P Err.Number ;; Set v = m ;; Err.Clear ;; v(0, 1) = 1 ;; P Err.Number ;; Err.Clear ;; m(Null) = 1 ;; P Err.Number ;; Err.Clear ;; v(Null) = 1 ;; P Err.Number""",
        "450|450|438|13|13|", DisplayName = "Q03: too many arguments, and a Null one")]
    public void LateBound_TheDefaultMemberOfWhatIsIndexed_IsAssignedThroughIDispatch(string body, string expected)
        => Assert.AreEqual(expected, Run(body));

    // E04: early-bound, MS-VBA calls Item(1) and assigns what it returns, which is no object: 424 (ObjectMemberTargetTests).
    [TestMethod]
    public async Task EarlyBound_AnIndexedCollection_IsAssignedWhatItsItemReturns()
        => Assert.AreEqual("424|", await RunInWorkspace("""Dim c As New Collection ;; c.Add 1 ;; Err.Clear ;; c(1) = 5 ;; P Err.Number"""));

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
