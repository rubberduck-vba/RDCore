using RDCore.External.Automation;
using RDCore.Runtime.Compiled;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Libraries;
using RDCore.Tests.Runtime.Libraries;
using System.Globalization;
using static RDCore.Tests.Cli.CompiledDirectCallTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// An array a server returns, as Excel returns the values of a block of cells (<c>Range.Value</c>): an array of <c>Variant</c>s, with the bounds it has, which
/// compiled code reads and writes an element of as it does an element of an array the program makes. The server here is a fake that holds plain arrays, of
/// the shapes Excel gives - two dimensions numbered from 1, a value or nothing in each cell - so what is under test is the runtime's side of it, on any
/// platform.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.13.1 Index Expressions")]
public sealed class CompiledMarshalledArrayTests
{
    private static readonly DateTime Day = new(2026, 11, 16, 6, 0, 0, DateTimeKind.Unspecified);

    // the values of a block of 4 rows and 3 columns, as Excel gives them: numbers, a blank cell (nothing), a text, a Boolean and a date.
    private static Array Block()
    {
        var block = (object?[,])Array.CreateInstance(typeof(object), [4, 3], [1, 1]);
        object?[] cells = [1.5, null, "7", true, 2.0, Day, 3.0, -4.25, null, 10.0, false, 6.0];
        for (var index = 0; index < cells.Length; index++)
        {
            block[1 + index / 3, 1 + index % 3] = cells[index];
        }

        return block;
    }

    // the server's side of it: a sheet that answers each name with the values it holds, and keeps what it is given.
    private sealed class Sheet
    {
        public Dictionary<string, Func<object?>> Values { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Block"] = Block,
            ["Numbers"] = () => new double[,] { { 1, 2 }, { 3, 4 }, { 5, 6 } },
            ["Row"] = () => new object?[] { 1.0, "a", null, 4 },
            ["Column"] = () => Array.CreateInstance(typeof(object), [3], [1]) is var column && Fill(column, 2.0, 3, 4.5) ? column : null,
            ["Cube"] = () => Array.CreateInstance(typeof(object), [2, 3, 2], [1, 1, 1]) is var cube && Fill(cube, 1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0, 10.0, 11.0, 12.0) ? cube : null,
        };

        public Dictionary<string, object?> Written { get; } = new(StringComparer.OrdinalIgnoreCase);

        // each element given in the order of the array's own enumeration, the last subscript varying fastest.
        private static bool Fill(Array array, params object?[] elements)
        {
            var index = 0;
            foreach (var subscripts in Subscripts(array))
            {
                array.SetValue(elements[index++], subscripts);
            }

            return true;
        }
    }

    // the subscripts of each element of array, the last varying fastest.
    private static IEnumerable<int[]> Subscripts(Array array)
    {
        var subscripts = Enumerable.Range(0, array.Rank).Select(array.GetLowerBound).ToArray();
        while (true)
        {
            yield return (int[])subscripts.Clone();
            var dimension = array.Rank - 1;
            while (dimension >= 0 && ++subscripts[dimension] > array.GetUpperBound(dimension))
            {
                subscripts[dimension] = array.GetLowerBound(dimension);
                dimension--;
            }

            if (dimension < 0)
            {
                yield break;
            }
        }
    }

    private sealed class FakeServer : IAutomationServer
    {
        public Sheet Sheet { get; } = new();

        public bool IsAvailable => true;

        public object CreateObject(string progId)
            => string.Equals(progId, "Lab.Sheet", StringComparison.OrdinalIgnoreCase)
                ? Sheet
                : throw new AutomationException(unchecked((int)0x80040154), $"'{progId}' is not registered.");

        public object? Invoke(object target, string member, AutomationInvocation invocation, object?[] arguments, bool[] byReference, CultureInfo culture)
        {
            if (invocation is AutomationInvocation.Let)
            {
                Sheet.Written[member] = arguments[^1];
                return null;
            }

            return Sheet.Values.TryGetValue(member, out var value) ? value() : throw new AutomationException(unchecked((int)0x80020006), "Unknown name.");
        }

        public string? ClassNameOf(object target) => target is Sheet ? "Lab.Sheet" : null;

        public void Release(object handle)
        {
        }

        public bool MoveNext(object enumerator, out object? current)
        {
            current = null;
            return false;
        }

        public void Reset(object enumerator)
        {
        }

        public void Advise(object source, IAutomationEventSink sink)
        {
        }

        public void Unadvise(object source)
        {
        }
    }

    // the program run both ways: what it printed, the error it raised and where, alike, which is returned with what the server was given in the compiled
    // run; and each procedure named compiled, with as many statements recovered as said.
    private static async Task<(string Observed, FakeServer Server)> AssertAlikeAsync(string program, params (string Name, long Recoveries)[] compiled)
    {
        CompiledExecutionTests.RequirePayloads();
        var interpreted = await RunAsync(program, VBExecutionEngine.Interpreter);
        var run = await RunAsync(program, VBExecutionEngine.Compiled);
        Assert.AreEqual(interpreted.Observed, run.Observed);

        foreach (var (name, recoveries) in compiled)
        {
            Assert.IsTrue(run.Compiled.TryGetValue(name, out var result), $"{name} was not called");
            Assert.IsNotNull(result.Body, $"{name}: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
            if (!CompiledExecution.HandsOverAtRandom)
            {
                Assert.AreEqual(recoveries, result.Body.Recoveries,
                    $"{name}'s recoveries; stepped: {string.Join("; ", result.Stepped.Select(reason => $"{reason.Kind} {reason.Detail} {reason.Location}"))}");
            }
        }

        return (run.Observed, run.Server);
    }

    private static async Task<(string Observed, IReadOnlyDictionary<string, CompilationResult> Compiled, FakeServer Server)> RunAsync(string program, VBExecutionEngine engine)
    {
        var server = new FakeServer();
        (string Observed, IReadOnlyDictionary<string, CompilationResult> Compiled) made = (string.Empty, new Dictionary<string, CompilationResult>());
        using var running = CompiledExecution.Running(engine);
        await ModuleWorkspace.InspectAsync([], program, (provider, run, _) =>
        {
            made = (Observed(run), CompiledExecutionTests.ByName(provider));
            return Task.CompletedTask;
        }, libraries: new ModuleWorkspace.WorkspaceLibraries([], new InMemoryLibrarySource(new LibraryDescription { Name = "Lab" }), server, true), engine: engine);
        return (made.Observed, made.Compiled, server);
    }

    // what Main prints of each element of a two-dimensional V, row by row: its type and its value.
    private static readonly string[] PrintTwoDimensions =
    [
        "Private Sub PrintAll(ByRef V As Variant)",
        "    Dim R As Long, C As Long, Shown As String",
        "    For R = LBound(V, 1) To UBound(V, 1)",
        "        Shown = \"\"",
        "        For C = LBound(V, 2) To UBound(V, 2)",
        "            Shown = Shown & TypeName(V(R, C)) & \":\" & V(R, C) & \" \"",
        "        Next",
        "        Debug.Print Shown",
        "    Next",
        "End Sub",
    ];

    // the pass of a block of cells read at once, as it is written against Excel: each cell that is not blank doubled, in a procedure of its own. Each element
    // is read and written by compiled code; the one statement recovered is the text's, which a Variant holds as no payload, so its product is the
    // interpreter's, as the product of any String is.
    [TestMethod]
    public async Task TheValuesOfABlockOfCells_AreDoubledByCompiledCode_AsTheInterpreterDoublesThem()
    {
        var (observed, _) = await AssertAlikeAsync(Program([
            "Public Sub Main()",
            "    Dim S As Object, V As Variant",
            "    Set S = CreateObject(\"Lab.Sheet\")",
            "    V = S.Block",
            "    Debug.Print LBound(V, 1) & \" \" & UBound(V, 1) & \" \" & LBound(V, 2) & \" \" & UBound(V, 2)",
            "    Doubled V",
            "    PrintAll V",
            "End Sub",
            "Private Sub Doubled(ByRef V As Variant)",
            "    Dim R As Long, C As Long",
            "    For R = 1 To UBound(V, 1)",
            "        For C = 1 To UBound(V, 2)",
            "            If Not IsEmpty(V(R, C)) Then V(R, C) = V(R, C) * 2",
            "        Next",
            "    Next",
            "End Sub",
            .. PrintTwoDimensions]),
            ("Doubled", 1));

        StringAssert.Contains(observed, "1 4 1 3 ; Double:3 Empty: Double:14 ; Integer:-2 Double:4 ");
        StringAssert.Contains(observed, "; Double:6 Double:-8.5 Empty: ; Double:20 Integer:0 Double:12 |");
    }

    // the read, the pass and the write in one procedure, as the macro that times it has them; what the server is given back is the block, doubled.
    [TestMethod]
    public async Task ABlockOfCellsReadWorkedOnAndWrittenBack_IsTheBlockTheServerIsGiven()
    {
        var (_, server) = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim S As Object, V As Variant, R As Long, C As Long",
            "    Set S = CreateObject(\"Lab.Sheet\")",
            "    V = S.Block",
            "    For R = 1 To 4",
            "        For C = 1 To 3",
            "            If Not IsEmpty(V(R, C)) Then V(R, C) = V(R, C) * 2",
            "        Next",
            "    Next",
            "    S.Target = V",
            "    Debug.Print \"written\"",
            "End Sub"));

        var written = (object?[,])server.Sheet.Written["Target"]!;
        Assert.AreEqual((1, 4, 1, 3), (written.GetLowerBound(0), written.GetUpperBound(0), written.GetLowerBound(1), written.GetUpperBound(1)));
        Assert.AreEqual(3.0, written[1, 1]);
        Assert.IsNull(written[1, 2], "a blank cell is still blank");
        Assert.AreEqual(14.0, written[1, 3], "the text of a number is doubled as a Double");
        Assert.AreEqual((short)-2, written[2, 1], "True doubled is the Integer -2");
        Assert.AreEqual(-8.5, written[3, 2]);
        Assert.AreEqual(12.0, written[4, 3]);
    }

    // an element of the block is a Variant: compiled code gives it a value of another type - a number where there was nothing, a text where there was a
    // date, nothing where there was a number - and reads back what it was given, the other elements as they were. The two statements recovered are the
    // copies of nothing and of a text, which no payload holds: a copy of such a Variant is the interpreter's, whatever holds it.
    [TestMethod]
    public async Task AnElementOfABlockOfCells_IsAVariant_ThatCompiledCodeGivesAValueOfAnyType()
    {
        var (observed, _) = await AssertAlikeAsync(Program([
            "Public Sub Main()",
            "    Dim S As Object, V As Variant",
            "    Set S = CreateObject(\"Lab.Sheet\")",
            "    V = S.Block",
            "    Assigned V",
            "    PrintAll V",
            "End Sub",
            "Private Sub Assigned(ByRef V As Variant)",
            "    V(4, 1) = V(3, 3)",
            "    V(3, 3) = V(1, 1) + V(3, 1)",
            "    V(1, 2) = 5",
            "    V(2, 3) = V(1, 3)",
            "End Sub",
            .. PrintTwoDimensions]),
            ("Assigned", 2));

        StringAssert.Contains(observed, "Double:1.5 Integer:5 String:7 ; Boolean:True Double:2 String:7 ; Double:3 Double:-4.25 Double:4.5 ; Empty: Boolean:False Double:6 |");
    }

    // an array of numbers the server returns is still an array of Variants, each holding its number.
    [TestMethod]
    public async Task AnArrayOfDoublesTheServerReturns_IsAnArrayOfVariants_EachADouble()
    {
        var (observed, _) = await AssertAlikeAsync(Program([
            "Public Sub Main()",
            "    Dim S As Object, V As Variant",
            "    Set S = CreateObject(\"Lab.Sheet\")",
            "    V = S.Numbers",
            "    Debug.Print TypeName(V)",
            "    Doubled V",
            "    PrintAll V",
            "End Sub",
            "Private Sub Doubled(ByRef V As Variant)",
            "    Dim R As Long, C As Long",
            "    For R = LBound(V, 1) To UBound(V, 1)",
            "        For C = LBound(V, 2) To UBound(V, 2)",
            "            V(R, C) = V(R, C) * 2",
            "        Next",
            "    Next",
            "End Sub",
            .. PrintTwoDimensions]),
            ("Doubled", 0));

        StringAssert.Contains(observed, "Variant() ; Double:2 Double:4 ; Double:6 Double:8 ; Double:10 Double:12 |");
    }

    // a row of values (numbered from 0), a column of them numbered from 1, and an array of three dimensions: each element where its subscripts say. The
    // sum weighs each element of the cube by its subscripts, so an element out of its place changes it.
    [TestMethod]
    public async Task AnArrayOfOneOrOfThreeDimensions_HasEachElementWhereItsSubscriptsSay()
    {
        var (observed, _) = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim S As Object, Row As Variant, Column As Variant, Cube As Variant",
            "    Set S = CreateObject(\"Lab.Sheet\")",
            "    Row = S.Row",
            "    Column = S.Column",
            "    Cube = S.Cube",
            "    Debug.Print LBound(Row) & \" \" & LBound(Column) & \" \" & TypeName(Row(1)) & \" \" & TypeName(Row(3))",
            "    Debug.Print Summed(Row, Column, Cube)",
            "    Debug.Print Column(3)",
            "End Sub",
            "Private Function Summed(ByRef Row As Variant, ByRef Column As Variant, ByRef Cube As Variant) As Double",
            "    Dim I As Long, J As Long, K As Long, Total As Double",
            "    For I = LBound(Row) To UBound(Row)",
            "        If IsEmpty(Row(I)) Then Total = Total + 1000",
            "    Next",
            "    Total = Total + Row(0) + Row(3)",
            "    For I = LBound(Column) To UBound(Column)",
            "        Column(I) = Column(I) + I",
            "        Total = Total + Column(I)",
            "    Next",
            "    For I = 1 To 2",
            "        For J = 1 To 3",
            "            For K = 1 To 2",
            "                Total = Total + Cube(I, J, K) * (I * 100 + J * 10 + K)",
            "            Next",
            "        Next",
            "    Next",
            "    Summed = Total",
            "End Function"),
            ("Summed", 0));

        // 1000 for the one blank, 1 + 4, 3 + 5 + 7.5, and the cube's 15340.
        StringAssert.Contains(observed, "0 1 String Long ; 16360.5 ; 7.5 |");
    }
}
