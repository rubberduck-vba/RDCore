using RDCore.Runtime.Support.Host;
using RDCore.Runtime.Compiled;
using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.CompiledDirectCallTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>VarType</c>, <c>TypeName</c>, <c>IsEmpty</c> and <c>IsNull</c> of a Variant compiled code reads in its cell: what the cell holds answers them, with
/// no call of the library's function, unless it holds what the cell says nothing of (an object, an array), which the library is called for. A program
/// runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledVariantInformationTests
{
    // the program run both ways, with the classes given: what it printed, the error it raised and where, alike; and each procedure named compiled, with no
    // statement recovered, and as many calls of the library's functions made as said (CompiledBody.LibraryCallsMade). A run that hands over to the
    // interpreter at random makes fewer of them: it is held to none only where none is said.
    private static async Task AssertAlikeAsync(
        string program, (string Name, long LibraryCalls)[] compiled, IReadOnlyList<(string Name, string Source)>? classes = null)
    {
        CompiledExecutionTests.RequirePayloads();
        var interpreted = await RunAsync(program, VBExecutionEngine.Interpreter, classes ?? []);
        var run = await RunAsync(program, VBExecutionEngine.Compiled, classes ?? []);
        Assert.AreEqual(interpreted.Observed, run.Observed);

        foreach (var (name, libraryCalls) in compiled)
        {
            Assert.IsTrue(run.Compiled.TryGetValue(name, out var result), $"{name} was not called");
            Assert.IsNotNull(result.Body, $"{name}: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
            if (!CompiledExecution.HandsOverAtRandom)
            {
                Assert.AreEqual(0, result.Body.Recoveries,
                    $"{name}'s recoveries; stepped: {string.Join("; ", result.Stepped.Select(reason => $"{reason.Kind} {reason.Detail} {reason.Location}"))}");
                Assert.AreEqual(libraryCalls, result.Body.LibraryCallsMade, $"{name}'s calls of the library");
            }
            else if (libraryCalls == 0)
            {
                Assert.AreEqual(0, result.Body.LibraryCallsMade, $"{name}'s calls of the library");
            }
        }
    }

    private static async Task<(string Observed, IReadOnlyDictionary<string, CompilationResult> Compiled)> RunAsync(
        string program, VBExecutionEngine engine, IReadOnlyList<(string Name, string Source)> classes)
    {
        (string Observed, IReadOnlyDictionary<string, CompilationResult> Compiled) made = default;
        using var running = CompiledExecution.Running(engine);
        await ModuleWorkspace.InspectAsync(classes, program, (provider, run, _) =>
        {
            made = (Observed(run), CompiledExecutionTests.ByName(provider));
            return Task.CompletedTask;
        }, engine: engine);
        return made;
    }

    // MS-VBAL §6.1.2.7.1.13, §6.1.2.7.1.4, §6.1.2.7.1.7 and §6.1.2.7.1.12: VarType, IsEmpty and IsNull (Described), and TypeName (Named), of a Variant
    // parameter that holds a value of each scalar subtype, Empty, Null, a String, an error and Missing. A parameter passed a String, an error or Missing is
    // held in no Variant's cell, which leaves each function of it to the library: three of them, in each of two turns of the loop.
    [TestMethod]
    public async Task TheInformationOfAVariant_IsReadInItsCell()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim V As Variant",
            "    Debug.Print Described(CByte(7)), Described(CInt(3)), Described(3&), Described(CLngLng(4)), Described(CSng(1.5)), Described(2.5)",
            "    Debug.Print Described(CCur(1.5)), Described(#1/2/2000#), Described(True), Described(V), Described(Null), Described(\"x\")",
            "    Debug.Print Described(CVErr(5)), Described()",
            "    Debug.Print Named(CByte(7)); Named(CInt(3)); Named(3&); Named(CLngLng(4)); Named(CSng(1.5)); Named(2.5); Named(CCur(1.5))",
            "    Debug.Print Named(#1/2/2000#); Named(True); Named(V); Named(Null); Named(\"x\"); Named(CVErr(5)); Named()",
            "End Sub",
            "Private Function Described(Optional ByVal V As Variant) As Long",
            "    Dim I As Long, T As Long, E As Boolean, U As Boolean",
            "    For I = 1 To 2",
            "        T = VarType(V)",
            "        E = IsEmpty(V)",
            "        U = IsNull(V)",
            "    Next",
            "    Described = T * 100 - E * 10 - U",
            "End Function",
            "Private Function Named(Optional ByVal V As Variant) As String",
            "    Dim I As Long, S As String",
            "    For I = 1 To 2",
            "        S = TypeName(V)",
            "    Next",
            "    Named = S & \" \"",
            "End Function"), [("Described", 3 * 2 * 3), ("Named", 3 * 2)]);

    // a Variant variable assigned a String, then Null, in its cell: VarType and TypeName joined to a String, IsEmpty and IsNull added up.
    [TestMethod]
    public async Task TheInformationOfAVariantVariable_IsReadInItsCell()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Locals()",
            "End Sub",
            "Private Function Locals() As String",
            "    Dim V As Variant, I As Long, S As String, N As Long",
            "    For I = 1 To 2",
            "        V = \"x\"",
            "        S = S & VarType(V) & \" \" & TypeName(V) & \" \"",
            "        N = N - IsEmpty(V) - IsNull(V) * 2",
            "        V = Null",
            "        S = S & VarType(V) & \" \" & TypeName(V) & \" \"",
            "        N = N - IsEmpty(V) * 10 - IsNull(V) * 20",
            "    Next",
            "    Locals = S & N",
            "End Function"), [("Locals", 0)]);

    // an element of an array of Variants, read in its cell: a Double, an Integer, a Boolean and Empty.
    [TestMethod]
    public async Task TheInformationOfAVariantElement_IsReadInItsCell()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Elements(); ElementNames()",
            "End Sub",
            "Private Function Elements() As Long",
            "    Dim A(3) As Variant, I As Long",
            "    A(0) = 2.5",
            "    A(1) = CInt(3)",
            "    A(2) = True",
            "    For I = 0 To 3",
            "        Elements = Elements * 100 + VarType(A(I)) - IsEmpty(A(I)) * 50 - IsNull(A(I)) * 60",
            "    Next",
            "End Function",
            "Private Function ElementNames() As String",
            "    Dim A(3) As Variant, I As Long, S As String",
            "    A(0) = 2.5",
            "    A(1) = CInt(3)",
            "    A(2) = True",
            "    For I = 0 To 3",
            "        S = S & TypeName(A(I)) & \" \"",
            "    Next",
            "    ElementNames = S",
            "End Function"), [("Elements", 0), ("ElementNames", 0)]);

    // a Variant that holds an object: the cell says nothing of it, and each function is the library's, called by compiled code. VarType, IsEmpty and IsNull
    // look at its default member's value, TypeName names its class.
    [TestMethod]
    public async Task TheInformationOfAnObject_IsTheLibrarys()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Described(New Valued), Described(New Collection); Named(New Valued); Named(New Collection)",
            "End Sub",
            "Private Function Described(ByVal V As Variant) As Long",
            "    Dim I As Long, T As Long, E As Boolean, U As Boolean",
            "    For I = 1 To 2",
            "        T = VarType(V)",
            "        E = IsEmpty(V)",
            "        U = IsNull(V)",
            "    Next",
            "    Described = T * 100 - E * 10 - U",
            "End Function",
            "Private Function Named(ByVal V As Variant) As String",
            "    Dim I As Long, S As String",
            "    For I = 1 To 2",
            "        S = TypeName(V)",
            "    Next",
            "    Named = S & \" \"",
            "End Function"),
            [("Described", 2 * 2 * 3), ("Named", 2 * 2)],
            classes:
            [
                ("Valued", ModuleWorkspace.ClassModule("Valued",
                    "Public Property Get Value() As Long", "Attribute Value.VB_UserMemId = 0", "    Value = 100", "End Property")),
            ]);
}
