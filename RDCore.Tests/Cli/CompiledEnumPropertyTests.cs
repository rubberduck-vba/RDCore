using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A value of an <c>Enum</c> type, compiled: an <c>Enum</c> holds a <c>Long</c> (<strong>MS-VBAL §5.2.3.4</strong>), so compiled code holds a variable, a
/// parameter, a result and a record field of one as it holds a <c>Long</c>: the <c>Property Get</c> that returns one, the <c>Property Let</c> given one, and
/// the procedure that computes one. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledEnumPropertyTests
{
    private static readonly string[] Shapes =
    [
        "Public Enum Shape",
        "    Round = 1",
        "    Square = 2",
        "    Triangle = 4",
        "End Enum",
    ];

    private static readonly (string Name, string Source)[] Classes =
    [
        ("IPiece", ModuleWorkspace.ClassModule("IPiece", "Option Explicit",
            "Public Property Get Kind() As Shape",
            "End Property")),
        ("Piece", ModuleWorkspace.ClassModule("Piece", "Option Explicit",
            "Implements IPiece",
            "Private Type TPiece",
            "    Kind As Shape",
            "    Count As Long",
            "End Type",
            "Private this As TPiece",
            "Public Property Get Kind() As Shape",
            "    Kind = this.Kind",
            "End Property",
            "Public Property Let Kind(ByVal Value As Shape)",
            "    this.Kind = Value",
            "End Property",
            "Private Property Get IPiece_Kind() As Shape",
            "    IPiece_Kind = this.Kind",
            "End Property")),
    ];

    private static string Module(params string[] lines) => Program([.. Shapes, .. lines]);

    private static void AssertCompiled(IReadOnlyDictionary<string, RDCore.Runtime.Compiled.CompilationResult> compiled, params string[] names)
    {
        foreach (var name in names)
        {
            Assert.IsTrue(compiled.TryGetValue(name, out var result), $"{name} was not called");
            Assert.IsNotNull(result.Body, $"{name}: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
            Assert.IsEmpty(result.Stepped, $"{name} steps: {string.Join("; ", result.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
        }
    }

    // a Property Get of an Enum returns the record field it reads, a Property Let of one writes it, and so does the Property Get an interface is read through.
    [TestMethod]
    public async Task APropertyOfAnEnum_IsReadAndWrittenAsALongIs()
        => AssertCompiled(await AssertAlikeAsync(Module(
            "Public Sub Main()",
            "    Dim P As New Piece",
            "    Dim I As IPiece",
            "    Dim N As Long",
            "    For N = 1 To 3",
            "        P.Kind = Square * N",
            "        Set I = P",
            "        Debug.Print P.Kind; I.Kind",
            "    Next",
            "End Sub"), classes: Classes, expected: " 6  6"), "Kind", "Let Kind", "IPiece_Kind");

    // an Enum parameter, variable and result are Longs: compared with a member of the Enum, summed, and returned, a Long that is no member included.
    [TestMethod]
    public async Task AnEnumParameterVariableAndResult_AreLongs()
        => AssertCompiled(await AssertAlikeAsync(Module(
            "Public Sub Main()",
            "    Debug.Print Weigh(Square, 4); Weigh(Round, 3); TypeName(Weigh(Triangle, 1)); VarType(Weigh(Square, 1))",
            "End Sub",
            "Private Function Weigh(ByVal S As Shape, ByVal N As Long) As Shape",
            "    Dim Total As Shape",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        If S = Square Then",
            "            Total = Total + I",
            "        Else",
            "            Total = Total + 2 * I",
            "        End If",
            "    Next",
            "    Weigh = Total",
            "End Function"), expected: " 10  12 Long 3"), "Weigh");

    // an Enum holds no more than a Long: a value past it is error 6, as it is of a Long.
    [TestMethod]
    public async Task AnEnumPastALong_IsError6()
        => AssertCompiled(await AssertAlikeAsync(Module(
            "Public Sub Main()",
            "    On Error Resume Next",
            "    Debug.Print Grown(Round)",
            "    Debug.Print \"err\"; Err.Number",
            "End Sub",
            "Private Function Grown(ByVal S As Shape) As Shape",
            "    Dim I As Long",
            "    For I = 1 To 40",
            "        S = S * 2",
            "    Next",
            "    Grown = S",
            "End Function"), expected: "err 6"), "Grown");
}
