using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.4.3.3</strong> <c>ReDim Preserve</c>: the last dimension's upper bound moves, and every element whose subscripts are still in the
/// bounds keeps its value; an element the array gains holds its type's default value. The expected text is computed here from the subscripts, in either
/// engine, for each rank and for an element type held as a number, as text and as a <c>Variant</c>.
/// </summary>
[TestClass]
public sealed class RedimPreserveElementTests
{
    private static readonly string[] Lowers = ["1", "0", "-1"];

    // the bounds of each dimension, the last given its upper bound.
    private static string Bounds(int rank, int lastUpper)
        => string.Join(", ", Enumerable.Range(0, rank).Select(at => at == rank - 1 ? $"1 To {lastUpper}" : $"{Lowers[at]} To {int.Parse(Lowers[at]) + 1}"));

    private static string Subscripts(int rank) => string.Join(", ", Enumerable.Range(0, rank).Select(at => $"I{at}"));

    // nested For loops over each dimension, the last outermost, around the body.
    private static IEnumerable<string> Loops(int rank, string body)
    {
        for (var at = rank - 1; at >= 0; at--)
        {
            yield return $"    For I{at} = LBound(A, {at + 1}) To UBound(A, {at + 1})";
        }

        yield return $"        {body}";
        for (var at = 0; at < rank; at++)
        {
            yield return "    Next";
        }
    }

    // what an element at the subscripts holds: a number from them, as a Long, as text or as a Variant holding the Long.
    private static string ValueExpression(int rank, string type)
    {
        var number = string.Join(" + ", Enumerable.Range(0, rank).Select(at => $"I{at} * {(int)Math.Pow(10, rank - 1 - at)}"));
        return type == "String" ? $"CStr({number})" : number;
    }

    private static string ProgramOf(int rank, string type)
    {
        var declared = string.Join(", ", Enumerable.Range(0, rank).Select(at => $"I{at} As Long"));
        string[] lines =
        [
            "Attribute VB_Name = \"Program\"",
            "Option Explicit",
            "Public Sub Main()",
            $"    Dim A() As {type}, {declared}, S As String",
            $"    ReDim A({Bounds(rank, 2)})",
            .. Loops(rank, $"A({Subscripts(rank)}) = {ValueExpression(rank, type)}"),
            $"    ReDim Preserve A({Bounds(rank, 3)})",
            .. Loops(rank, $"S = S & \" \" & A({Subscripts(rank)})"),
            "    Debug.Print \"[\" & S & \"]\"",
            "    S = \"\"",
            $"    ReDim Preserve A({Bounds(rank, 1)})",
            .. Loops(rank, $"S = S & \" \" & A({Subscripts(rank)})"),
            "    Debug.Print \"[\" & S & \"]\"",
            "End Sub",
        ];
        return string.Join("\r\n", lines) + "\r\n";
    }

    // the text the program prints after a Preserve to lastUpper: each element in loop order, a kept one its number, a gained one its default.
    private static string Expected(int rank, string type, int lastUpper)
    {
        var ranges = Enumerable.Range(0, rank)
            .Select(at => at == rank - 1 ? (Lower: 1, Upper: lastUpper) : (Lower: int.Parse(Lowers[at]), Upper: int.Parse(Lowers[at]) + 1))
            .ToArray();
        var text = new System.Text.StringBuilder();
        var subscripts = ranges.Select(range => range.Lower).ToArray();
        while (true)
        {
            var kept = subscripts[rank - 1] <= 2;
            var number = Enumerable.Range(0, rank).Sum(at => subscripts[at] * (int)Math.Pow(10, rank - 1 - at));
            text.Append(' ').Append(kept ? number.ToString(System.Globalization.CultureInfo.InvariantCulture) : type == "Long" ? "0" : "");

            // the first dimension is the innermost loop: it varies fastest.
            var at = 0;
            while (at < rank && ++subscripts[at] > ranges[at].Upper)
            {
                subscripts[at] = ranges[at].Lower;
                at++;
            }

            if (at == rank)
            {
                return $"[{text}]";
            }
        }
    }

    [TestMethod]
    [DataRow(1, "Long")]
    [DataRow(2, "Long")]
    [DataRow(3, "Long")]
    [DataRow(3, "String")]
    [DataRow(3, "Variant")]
    public async Task ReDimPreserve_KeepsEveryElementStillInItsBounds_AndAnElementGainedIsTheDefault(int rank, string type)
    {
        var program = ProgramOf(rank, type);
        string[] expected = [Expected(rank, type, 3), Expected(rank, type, 1)];
        Assert.AreEqual(3 << (rank - 1), expected[0].Split(' ').Length - 1, "the premise: the grown array's element count");

        foreach (var engine in new[] { VBExecutionEngine.Interpreter, VBExecutionEngine.Compiled })
        {
            CollectionAssert.AreEqual(expected, await ModuleWorkspace.RunAsync([], program, engine), $"{engine}: {program}");
        }
    }

    // an element given by reference after the Preserve is the element the array kept: it reads what was kept, and what it is assigned the array holds.
    [TestMethod]
    public async Task AnElementGivenByReferenceAfterReDimPreserve_IsTheElementKept()
    {
        const string program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n"
            + "Public Sub Main()\r\n"
            + "    Dim A() As Long\r\n"
            + "    ReDim A(0 To 1, 1 To 2)\r\n"
            + "    A(1, 2) = 12\r\n"
            + "    A(0, 2) = 2\r\n"
            + "    ReDim Preserve A(0 To 1, 1 To 4)\r\n"
            + "    Bump A(1, 2)\r\n"
            + "    Debug.Print A(1, 2); A(0, 2); A(1, 4)\r\n"
            + "End Sub\r\n"
            + "Private Sub Bump(ByRef N As Long)\r\n"
            + "    Debug.Print N\r\n"
            + "    N = N + 1000\r\n"
            + "End Sub\r\n";

        foreach (var engine in new[] { VBExecutionEngine.Interpreter, VBExecutionEngine.Compiled })
        {
            CollectionAssert.AreEqual(new[] { "12", "1012  2  0" }, await ModuleWorkspace.RunAsync([], program, engine), engine.ToString());
        }
    }
}
