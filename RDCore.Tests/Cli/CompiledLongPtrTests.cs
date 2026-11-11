using NSubstitute;
using RDCore.Runtime.Compiled;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Support.Host;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Reflection;
using System.Runtime.Loader;
using static RDCore.Tests.Cli.CompiledDirectCallTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>LongPtr</c> in compiled code: the integer type it is an alias of (MS-VBAL §2.2), a <c>LongLong</c> in a 64-bit environment and a <c>Long</c> in a
/// 32-bit one, which the session's environment decides, never the process the runtime runs in. A variable declared as one is held in a cell of its declared
/// type, so that what reads it as a value (<c>VarType</c>, <c>LenB</c>, a <c>Declare</c>) sees a <c>LongPtr</c>; a <c>Variant</c> holds one as the type it is
/// an alias of. Each row runs in both environments, and a program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledLongPtrTests
{
    private static async Task<(string Observed, IReadOnlyDictionary<string, CompilationResult> Compiled)> RunAsync(string program, VBExecutionEngine engine,
        IReadOnlyList<(string Name, string Source)> classes, bool is64Bit)
    {
        (string Observed, IReadOnlyDictionary<string, CompilationResult> Compiled) made = default;
        using var running = CompiledExecution.Running(engine);
        await ModuleWorkspace.InspectAsync(classes, program, (provider, run, _) =>
        {
            made = (Observed(run), CompiledExecutionTests.ByName(provider));
            return Task.CompletedTask;
        }, engine: engine, environment: profile => profile with { Is64Bit = is64Bit });
        return made;
    }

    // the program run by both engines in the environment, printing what is expected; each procedure named compiled, none of its statements stepped, and
    // handed to the interpreter as often as its recoveries say.
    private static async Task AssertCompiledAsync(bool is64Bit, string program, IReadOnlyList<(string Name, string Source)> classes, string expected,
        params (string Name, long Recoveries)[] compiled)
    {
        CompiledExecutionTests.RequirePayloads();
        var interpreted = await RunAsync(program, VBExecutionEngine.Interpreter, classes, is64Bit);
        var run = await RunAsync(program, VBExecutionEngine.Compiled, classes, is64Bit);
        Assert.AreEqual(interpreted.Observed, run.Observed);
        StringAssert.Contains(run.Observed, expected);
        foreach (var (name, recoveries) in compiled)
        {
            Assert.IsTrue(run.Compiled.TryGetValue(name, out var result), $"{name} was not called");
            Assert.IsNotNull(result.Body, $"{name}: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
            Assert.IsEmpty(result.Stepped, $"{name}'s stepped statements: {string.Join("; ", result.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
            if (!CompiledExecution.HandsOverAtRandom)
            {
                Assert.AreEqual(recoveries, result.Body.Recoveries, $"{name}'s recoveries");
            }
        }
    }

    // a LongPtr local counted in a loop, as the integer it is an alias of.
    [TestMethod]
    [DataRow(true, DisplayName = "64-bit")]
    [DataRow(false, DisplayName = "32-bit")]
    public async Task ALongPtrLocal_IsCountedInCompiledCode(bool is64Bit)
        => await AssertCompiledAsync(is64Bit, Program(
            "Public Sub Main()",
            "    Debug.Print Advanced(100)",
            "End Sub",
            "Private Function Advanced(ByVal N As Long) As LongPtr",
            "    Dim P As LongPtr, I As Long",
            "    For I = 1 To N",
            "        P = P + 8",
            "    Next",
            "    Advanced = P \\ 2",
            "End Function"), [], "| 400 |", ("Advanced", 0));

    // a LongPtr parameter given by value, and a LongPtr result, which a compiled caller passes on.
    [TestMethod]
    [DataRow(true, DisplayName = "64-bit")]
    [DataRow(false, DisplayName = "32-bit")]
    public async Task ALongPtrParameterAndResult_AreCompiled(bool is64Bit)
        => await AssertCompiledAsync(is64Bit, Program(
            "Public Sub Main()",
            "    Debug.Print Offset(1000, 3); Twice(5)",
            "End Sub",
            "Private Function Offset(ByVal Base As LongPtr, ByVal Count As Long) As LongPtr",
            "    Offset = Base + Count * 4",
            "End Function",
            "Private Function Twice(ByVal Count As Long) As LongPtr",
            "    Twice = Offset(Offset(0, Count), Count)",
            "End Function"), [], "| 1012  40 |", ("Offset", 0), ("Twice", 0));

    private static (string Name, string Source)[] References()
        =>
        [
            ("Reference", ModuleWorkspace.ClassModule("Reference", ["Option Explicit",
                "Private Type TReference", "    Address As LongPtr", "End Type",
                "Private this As TReference",
                "Public Property Get Address() As LongPtr",
                "    Address = this.Address",
                "End Property",
                "Public Property Let Address(ByVal value As LongPtr)",
                "    this.Address = value",
                "End Property",
                "Public Function Size() As Long",
                "    Size = LenB(this.Address)",
                "End Function"])),
        ];

    // a LongPtr field of the record of Me's state, read and written by compiled code, holds a LongPtr: its VarType and its size are a pointer's in the
    // environment.
    [TestMethod]
    [DataRow(true, "| 4104  20  8 |", DisplayName = "64-bit")]
    [DataRow(false, "| 4104  3  4 |", DisplayName = "32-bit")]
    public async Task ALongPtrFieldOfARecord_IsReadAndWrittenInCompiledCode(bool is64Bit, string expected)
        => await AssertCompiledAsync(is64Bit, Program(
            "Public Sub Main()",
            "    Dim R As New Reference",
            "    R.Address = 4096",
            "    R.Address = R.Address + 8",
            "    Debug.Print R.Address; VarType(R.Address); R.Size()",
            "End Sub"), References(), expected, ("Address", 0), ("Let Address", 0));

    // a LongPtr variable, parameter and result is as wide as a pointer of the environment (LenB, Hex$), and a Variant assigned one holds the type it is
    // an alias of, in either engine: in a 32-bit environment a Long, not a LongLong.
    [TestMethod]
    [DataRow(true, "| 8  16 LongLong8/LongLong 16 |", DisplayName = "64-bit")]
    [DataRow(false, "| 4  8 Long4/Long 8 |", DisplayName = "32-bit")]
    public async Task ALongPtr_IsAsWideAsAPointerOfTheEnvironment(bool is64Bit, string expected)
        => await AssertCompiledAsync(is64Bit, Program(
            "Public Sub Main()",
            "    Dim P As LongPtr, V As Variant",
            "    P = -1",
            "    V = P",
            "    Debug.Print LenB(P); Len(Hex$(P)); TypeName(V); Par(P); Len(Hex$(Res(-1)))",
            "End Sub",
            "Private Function Par(ByVal Q As LongPtr) As String",
            "    Dim W As Variant",
            "    W = Q",
            "    Par = LenB(Q) & \"/\" & TypeName(W)",
            "End Function",
            "Private Function Res(ByVal Q As Long) As LongPtr",
            "    Res = Q",
            "End Function"), [], expected, ("Res", 0));

    // past the range of a Long, a LongPtr overflows in a 32-bit environment (error 6), and not in a 64-bit one.
    [TestMethod]
    [DataRow(true, "| 1 |", 0L, DisplayName = "64-bit")]
    [DataRow(false, "| 106 |", 1L, DisplayName = "32-bit")]
    public async Task ALongPtrPastTheRangeOfALong_OverflowsOnlyIn32Bit(bool is64Bit, string expected, long recoveries)
        => await AssertCompiledAsync(is64Bit, Program(
            "Public Sub Main()",
            "    Debug.Print Widened()",
            "End Sub",
            "Private Function Widened() As Long",
            "    Dim P As LongPtr",
            "    On Error GoTo Bad",
            "    P = 2147483647",
            "    P = P + 1",
            "    Widened = 1",
            "    Exit Function",
            "Bad:",
            "    Widened = 100 + Err.Number",
            "End Function"), [], expected, ("Widened", recoveries));

    // a Variant compiled code assigns a LongPtr holds the type it is an alias of: what IIf returns of one, and a Variant function result assigned one, which
    // is read back.
    [TestMethod]
    [DataRow(true, "| 20  20  7 |", DisplayName = "64-bit")]
    [DataRow(false, "| 3  3  7 |", DisplayName = "32-bit")]
    public async Task AVariantAssignedALongPtrInCompiledCode_HoldsTheTypeItIsAnAliasOf(bool is64Bit, string expected)
        => await AssertCompiledAsync(is64Bit, Program(
            "Public Sub Main()",
            "    Dim V As Variant, W As Variant",
            "    V = Converted(3)",
            "    W = Twice(3)",
            "    Debug.Print VarType(V); VarType(W); W",
            "End Sub",
            "Private Function Converted(ByVal N As Long) As Variant",
            "    Dim P As LongPtr, V As Variant",
            "    P = N",
            "    V = IIf(N > 0, P, 0)",
            "    Converted = V",
            "End Function",
            "Private Function Twice(ByVal N As Long) As Variant",
            "    Dim P As LongPtr",
            "    P = N * 2",
            "    Twice = P",
            "    Twice = Twice + 1",
            "End Function"), [], expected, ("Converted", 0), ("Twice", 0));

    // what ObjPtr gives is a LongPtr, passed to a compiled function and returned by it; a Variant a LongPtr is assigned to holds the type it is an alias of,
    // and Hex$ of a LongPtr is as wide as a pointer.
    [TestMethod]
    [DataRow(true, "| True 16  20 LongLong 20  16 |", DisplayName = "64-bit")]
    [DataRow(false, "| True 16  3 Long 3  8 |", DisplayName = "32-bit")]
    public async Task ALongPtrFromObjPtr_IsCarriedByCompiledCode(bool is64Bit, string expected)
        => await AssertCompiledAsync(is64Bit, Program(
            "Public Sub Main()",
            "    Dim R As New Reference, P As LongPtr, V As Variant",
            "    P = ObjPtr(R)",
            "    V = Moved(P)",
            "    Debug.Print P <> 0; Moved(P) - P; VarType(P); TypeName(V); VarType(Held(P)); Len(Hex$(Negated(1)))",
            "End Sub",
            "Private Function Moved(ByVal P As LongPtr) As LongPtr",
            "    Moved = P + 16",
            "End Function",
            "Private Function Negated(ByVal P As LongPtr) As LongPtr",
            "    Negated = -P",
            "End Function",
            "Private Function Held(ByVal P As LongPtr) As Variant",
            "    Held = P + 1",
            "End Function"), References(), expected, ("Moved", 0), ("Negated", 0), ("Held", 0));

    // a LongPtr passed by reference to a Declare is passed as a pointer, and what the function writes at its address is the variable's: memmove (the C
    // library's on Linux, RtlMoveMemory on Windows) copies one LongPtr into another. The external host is a 64-bit process, as the environment is.
    [TestMethod]
    [TestCategory("MS-VBAL 5.2.3.5 External Procedure Declaration")]
    [OSCondition(OperatingSystems.Windows | OperatingSystems.Linux)]
    public async Task ALongPtrPassedByRefToADeclare_IsPassedAsAPointer_AndWrittenByTheFunction()
    {
        var library = OperatingSystem.IsWindows() ? "\"kernel32\" Alias \"RtlMoveMemory\"" : "\"libc.so.6\" Alias \"memmove\"";
        var program = Program(
            $"Private Declare PtrSafe Sub Copy Lib {library} (ByRef Destination As LongPtr, ByRef Source As LongPtr, ByVal Length As LongPtr)",
            "Public Sub Main()",
            "    Dim A As LongPtr, B As LongPtr",
            "    B = 123456789",
            "    Copy A, B, LenB(B)",
            "    Debug.Print A; VarType(A); LenB(A)",
            "End Sub");

        var interpreted = await RunAsync(program, VBExecutionEngine.Interpreter, [], is64Bit: true);
        var run = await RunAsync(program, VBExecutionEngine.Compiled, [], is64Bit: true);

        Assert.AreEqual(interpreted.Observed, run.Observed);
        StringAssert.Contains(run.Observed, "| 123456789  20  8 |");
    }

    // every procedure of the Program module compiled into an assembly in an environment, as rdc build compiles a workspace.
    private static async Task<byte[]> BuildAsync(string program, bool is64Bit)
    {
        byte[] built = [];
        await ModuleWorkspace.InspectAsync([], program, async (provider, _, _) =>
        {
            await provider.Image.Semantics.WhenAllEvaluatedAsync();
            var session = provider.Session;
            var pipeline = RuntimeExecutionPipeline.Create(session, provider.Image, Substitute.For<IErrorDetailsRecorder>());
            Assert.IsTrue(session.Symbols.TryGetModule("Program", out var module));

            var builder = new ProgramAssemblyBuilder(new AssemblyName("Built"));
            foreach (var procedure in session.Symbols.MembersOf(module.Uri).OfType<VBTypeMemberSymbol>())
            {
                _ = builder.Add(provider.Image, session, pipeline.Executor, procedure);
            }

            using var stream = new MemoryStream();
            builder.Save(stream);
            built = stream.ToArray();
        }, engine: VBExecutionEngine.Interpreter, environment: profile => profile with { Is64Bit = is64Bit });

        return built;
    }

    // the program run compiled in an environment, in a session bound to the assembly: what it observed, and how many procedures ran the assembly's code.
    private static async Task<(string Observed, int Bound)> RunBoundAsync(string program, byte[] built, bool is64Bit)
    {
        (string Observed, int Bound) made = default;
        var context = new AssemblyLoadContext("built program", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(new MemoryStream(built));
            await ModuleWorkspace.InspectAsync([], program, async (provider, _, again) =>
            {
                await provider.Image.Semantics.WhenAllEvaluatedAsync();
                Assert.IsTrue(ProgramAssembly.Bind(provider.Image, assembly), "the assembly is of this build of the runtime");
                using var compiled = CompiledExecution.Running(VBExecutionEngine.Compiled);
                var bound = await again();
                made = (Observed(bound), provider.Image.Compiler.Bound);
            }, engine: VBExecutionEngine.Interpreter, environment: profile => profile with { Is64Bit = is64Bit });
        }
        finally
        {
            context.Unload();
        }

        return made;
    }

    // code built in a 64-bit environment runs in a session of one, and never in a 32-bit one, where each procedure is compiled again at its first call: the
    // pointer size is part of what a procedure is compiled from, even of one that has no LongPtr.
    [TestMethod]
    public async Task CodeBuiltFor64Bit_IsNotRunIn32Bit_AndIsCompiledAgain()
    {
        CompiledExecutionTests.RequirePayloads();
        var program = Program(
            "Public Sub Main()",
            "    Debug.Print Offset(1000, 3); Total(10)",
            "End Sub",
            "Private Function Offset(ByVal Base As LongPtr, ByVal Count As Long) As LongPtr",
            "    Offset = Base + Count * 4",
            "End Function",
            "Private Function Total(ByVal N As Long) As Long",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        Total = Total + I",
            "    Next",
            "End Function");
        var built = await BuildAsync(program, is64Bit: true);

        var same = await RunBoundAsync(program, built, is64Bit: true);
        var other = await RunBoundAsync(program, built, is64Bit: false);

        StringAssert.Contains(same.Observed, "| 1012  55 |");
        Assert.AreEqual(same.Observed, other.Observed);
        Assert.IsGreaterThanOrEqualTo(2, same.Bound, "both procedures run the code built for their environment");
        Assert.AreEqual(0, other.Bound, "no procedure runs code built for another environment");
    }
}
