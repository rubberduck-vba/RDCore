using RDCore.Runtime.Compiled;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.Tests.External;
using RDCore.Tests.Runtime.Libraries;
using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A call of a procedure declared with <c>Declare</c> in a compiled procedure: the interpreter makes it, as a support call when it is a statement and as
/// a stepped statement when the function is called in an expression, and the procedure goes on compiled after it. What it writes through an argument
/// passed by reference, the ANSI copy of a <c>String</c>, <c>Err.LastDllError</c> and the errors a call raises are the interpreter's, as when the
/// procedure is interpreted. Each row calls the C library (msvcrt on Windows, libc on Linux).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.2.3.5 External Procedure Declaration")]
[OSCondition(OperatingSystems.Windows | OperatingSystems.Linux)]
public sealed class CompiledDeclareCallTests
{
    private static string Library => CompiledMemberEntryTests.CLibrary;

    private static string CopyLong => $"Private Declare PtrSafe Sub CopyLong Lib \"{Library}\" Alias \"memmove\" (ByRef Destination As Long, ByRef Source As Long, ByVal Length As LongPtr)";

    private static async Task<(string Observed, IReadOnlyDictionary<string, CompilationResult> Compiled)> RunAsync(string program, VBExecutionEngine engine,
        Func<WorkspaceLibraries>? libraries)
    {
        (string Observed, IReadOnlyDictionary<string, CompilationResult> Compiled) made = default;
        using var running = CompiledExecution.Running(engine);
        await InspectAsync([], program, (provider, run, _) =>
        {
            made = (Observed(run), CompiledExecutionTests.ByName(provider));
            return Task.CompletedTask;
        }, libraries: libraries?.Invoke(), engine: engine);
        return made;
    }

    // the program run both ways, printing what is expected: each procedure named compiled, never handed to the interpreter for the rest of its body, and
    // the Declare it calls as a statement made by the interpreter as a support call, or, when it is called in an expression, the statement stepped.
    private static async Task AssertGoesOnCompiledAsync(string program, string expected, (string Name, string Called, bool InAnExpression)[] procedures,
        Func<WorkspaceLibraries>? libraries = null)
    {
        CompiledExecutionTests.RequirePayloads();
        var interpreted = await RunAsync(program, VBExecutionEngine.Interpreter, libraries);
        var run = await RunAsync(program, VBExecutionEngine.Compiled, libraries);
        Assert.AreEqual(interpreted.Observed, run.Observed);
        StringAssert.Contains(run.Observed, expected);
        foreach (var (name, called, inAnExpression) in procedures)
        {
            Assert.IsTrue(run.Compiled.TryGetValue(name, out var result), $"{name} was not called");
            Assert.IsNotNull(result.Body, $"{name}: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
            var report = $"{name}: stepped [{string.Join("; ", result.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}"))}], support calls "
                + $"[{string.Join("; ", result.SupportCalls.Select(reason => $"{reason.Kind} {reason.Detail}"))}]";

            // under the random walk-back every activation may be handed to the interpreter, which is what that run is for.
            if (!CompiledExecution.HandsOverAtRandom)
            {
                Assert.AreEqual(0, result.Body.WalkBacks, $"{name}'s walk-backs (last at {result.Body.LastWalkedBackAt?.ToString() ?? "-"}); {report}");
            }

            var made = inAnExpression ? result.Stepped : result.SupportCalls;
            Assert.IsTrue(made.Any(reason => reason is { Kind: IneligibilityKind.Call } && reason.Detail == called), report);
        }
    }

    // memmove writes what it copies into the variable passed by reference, whatever it is: a local, a field of the module, an element of an array, a field
    // of a record; a property's value is a temporary, which it writes and nothing reads. A call statement with Call, and one naming the module, is the same
    // call.
    [TestMethod]
    public async Task WhatADeclareWritesThroughAnArgumentPassedByReference_IsTheVariables_AndTheProcedureGoesOnCompiled()
        => await AssertGoesOnCompiledAsync(Program(
            CopyLong,
            "Private Type Pair",
            "    Low As Long",
            "    High As Long",
            "End Type",
            "Private Field As Long",
            "Public Sub Main()",
            "    Debug.Print Local(1); Module(3); Element(3); Record(3); Temporary(3)",
            "End Sub",
            "Private Property Get Seven() As Long",
            "    Seven = 7",
            "End Property",
            "Private Function Local(ByVal N As Long) As Long",
            "    Dim I As Long, A As Long, B As Long",
            "    For I = 1 To N",
            "        B = I * 7",
            "        CopyLong A, B, 4",
            "        Local = Local * 100 + A",
            "        B = B + 1",
            "        Call CopyLong(A, B, 4)",
            "        Local = Local * 100 + A",
            "        B = B + 1",
            "        Program.CopyLong A, B, 4",
            "        Local = Local * 100 + A",
            "    Next",
            "End Function",
            "Private Function Module(ByVal N As Long) As Long",
            "    Dim I As Long, B As Long",
            "    For I = 1 To N",
            "        B = I * 11",
            "        CopyLong Field, B, 4",
            "        Module = Module * 100 + Field",
            "    Next",
            "End Function",
            "Private Function Element(ByVal N As Long) As Long",
            "    Dim I As Long, B As Long, Values(1 To 3) As Long",
            "    For I = 1 To N",
            "        B = I * 13",
            "        CopyLong Values(I), B, 4",
            "        Element = Element * 100 + Values(I)",
            "    Next",
            "End Function",
            "Private Function Record(ByVal N As Long) As Long",
            "    Dim I As Long, B As Long, P As Pair",
            "    For I = 1 To N",
            "        B = I * 17",
            "        CopyLong P.High, B, 4",
            "        Record = Record * 100 + P.High + P.Low",
            "    Next",
            "End Function",
            "Private Function Temporary(ByVal N As Long) As Long",
            "    Dim I As Long, B As Long",
            "    For I = 1 To N",
            "        B = I * 19",
            "        CopyLong Seven, B, 4",
            "        Temporary = Temporary * 100 + Seven",
            "    Next",
            "End Function"),
            "| 70809  112233  132639  173451  70707 |",
            [("Local", "CopyLong", false), ("Module", "CopyLong", false), ("Element", "CopyLong", false), ("Record", "CopyLong", false), ("Temporary", "CopyLong", false)]);

    // a String passed by value to a Declare is passed as an ANSI copy, and what the function writes into the copy is the variable's after the call: memset
    // fills its first characters, of a local and of a field of the module.
    [TestMethod]
    public async Task WhatADeclareWritesIntoTheAnsiCopyOfAString_IsTheVariables_AndTheProcedureGoesOnCompiled()
        => await AssertGoesOnCompiledAsync(Program(
            $"Private Declare PtrSafe Sub Fill Lib \"{Library}\" Alias \"memset\" (ByVal Text As String, ByVal Value As Long, ByVal Length As LongPtr)",
            "Private Label As String",
            "Public Sub Main()",
            "    Debug.Print Filled(3)",
            "End Sub",
            "Private Function Filled(ByVal N As Long) As String",
            "    Dim I As Long, S As String",
            "    For I = 1 To N",
            "        S = \"abc\"",
            "        Label = \"xyz\"",
            "        Fill S, 64 + I, I",
            "        Fill Label, 90, 1",
            "        Filled = Filled & S & Label & \" \"",
            "    Next",
            "End Function"),
            "| AbcZyz BBcZyz CCCZyz |",
            [("Filled", "Fill", false)]);

    // Err.LastDllError is the code the function left, read after the call in compiled code: closing a handle that is none is ERROR_INVALID_HANDLE (6) on
    // Windows, EBADF (9) on Linux.
    [TestMethod]
    public async Task LastDllErrorAfterADeclare_IsTheCodeTheFunctionLeft_AndTheProcedureGoesOnCompiled()
        => await AssertGoesOnCompiledAsync(Program(
            OperatingSystem.IsWindows()
                ? "Private Declare PtrSafe Function CloseHandle Lib \"kernel32\" (ByVal Handle As LongPtr) As Long"
                : "Private Declare PtrSafe Function CloseHandle Lib \"libc.so.6\" Alias \"close\" (ByVal Handle As Long) As Long",
            "Public Sub Main()",
            "    Debug.Print Closed(3)",
            "End Sub",
            "Private Function Closed(ByVal N As Long) As Long",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        CloseHandle 12345",
            "        Closed = Closed * 100 + Err.LastDllError",
            "    Next",
            "End Function"),
            OperatingSystem.IsWindows() ? "| 60606 |" : "| 90909 |",
            [("Closed", "CloseHandle", false)]);

    // a library that is not there is error 53, and a function the library does not export is error 453, raised where the call is: On Error Resume Next goes
    // past it, and the variable passed by reference holds what it held; a handler is given it, with Erl the line of the call, and Resume Next goes on after
    // it.
    [TestMethod]
    public async Task AnErrorADeclareRaises_IsRaisedWhereTheCallIs_AndTheProcedureGoesOnCompiled()
        => await AssertGoesOnCompiledAsync(Program(
            "Private Declare PtrSafe Sub Missing Lib \"no-such-library-quack\" (ByRef N As Long)",
            $"Private Declare PtrSafe Sub NoEntry Lib \"{Library}\" Alias \"no_such_function_quack\" (ByRef N As Long)",
            "Public Sub Main()",
            "    Debug.Print GoneOn(2); Handled(2)",
            "End Sub",
            "Private Function GoneOn(ByVal N As Long) As String",
            "    Dim I As Long, B As Long",
            "    On Error Resume Next",
            "    For I = 1 To N",
            "        B = I",
            "        Missing B",
            "        GoneOn = GoneOn & Err.Number & \":\" & B & \" \"",
            "        Err.Clear",
            "        NoEntry B",
            "        GoneOn = GoneOn & Err.Number & \":\" & B & \" \"",
            "        Err.Clear",
            "    Next",
            "End Function",
            "Private Function Handled(ByVal N As Long) As String",
            "    Dim I As Long",
            "    On Error GoTo Failed",
            "    For I = 1 To N",
            "        NoEntry I",
            "        Handled = Handled & I & \" \"",
            "    Next",
            "    Exit Function",
            "Failed:",
            "    Handled = Handled & Err.Number & \"@\" & Erl & \" \"",
            "    Resume Next",
            "End Function"),
            "| 53:1 453:1 53:2 453:2 453@23 1 453@23 2 |",
            [("GoneOn", "Missing", false), ("GoneOn", "NoEntry", false), ("Handled", "NoEntry", false)]);

    // a function that takes down the process that called it (the external host) is error 49, raised where the call is, and the next call starts another
    // host. Each run has a host of its own.
    [TestMethod]
    public async Task ADeclareThatTakesDownTheExternalHost_IsError49_AndTheProcedureGoesOnCompiled()
    {
        var hosts = new List<IDisposable>();
        try
        {
            await AssertGoesOnCompiledAsync(Program(
                $"Private Declare PtrSafe Sub Poke Lib \"{Library}\" Alias \"memmove\" (ByVal Destination As LongPtr, ByVal Source As LongPtr, ByVal Length As LongPtr)",
                $"Private Declare PtrSafe Function CAbs Lib \"{Library}\" Alias \"abs\" (ByVal N As Long) As Long",
                "Public Sub Main()",
                "    Debug.Print Crashed()",
                "End Sub",
                "Private Function Crashed() As String",
                "    Dim B As Long",
                "    On Error Resume Next",
                "    B = 5",
                "    Poke 0, 8, 8",
                "    Crashed = Err.Number & \" \" & B & \" \"",
                "    Err.Clear",
                "    B = CAbs(-B)",
                "    Crashed = Crashed & Err.Number & \" \" & B",
                "End Function"),
                "| 49 5 0 5 |",
                [("Crashed", "Poke", false)],
                () =>
                {
                    var host = ExternalHosts.New();
                    hosts.Add(host);
                    return new WorkspaceLibraries([], new InMemoryLibrarySource(), Outside: ExternalHosts.WorldOf(host));
                });
        }
        finally
        {
            hosts.ForEach(host => host.Dispose());
        }
    }

    // a function declared with Declare called in an expression, or for the whole value assigned: the interpreter runs the statement (stepped), and the
    // procedure goes on compiled after it.
    [TestMethod]
    public async Task ADeclaredFunctionCalledInAnExpression_IsSteppedByTheInterpreter_AndTheProcedureGoesOnCompiled()
        => await AssertGoesOnCompiledAsync(Program(
            $"Private Declare PtrSafe Function CAbs Lib \"{Library}\" Alias \"abs\" (ByVal N As Long) As Long",
            "Public Sub Main()",
            "    Debug.Print Summed(3); Assigned(3)",
            "End Sub",
            "Private Function Summed(ByVal N As Long) As Long",
            "    Dim I As Long, T As Long",
            "    For I = 1 To N",
            "        T = T + CAbs(-I) * 10",
            "    Next",
            "    Summed = T",
            "End Function",
            "Private Function Assigned(ByVal N As Long) As Long",
            "    Dim I As Long, T As Long",
            "    For I = 1 To N",
            "        T = CAbs(-I - T)",
            "    Next",
            "    Assigned = T",
            "End Function"),
            "| 60  6 |",
            [("Summed", "CAbs", true), ("Assigned", "CAbs", true)]);
}
