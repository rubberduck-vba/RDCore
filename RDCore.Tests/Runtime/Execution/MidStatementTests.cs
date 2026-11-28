using RDCore.Runtime.Execution;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// <strong>MS-VBAL §5.4.3.5</strong> the <c>Mid</c>, <c>MidB</c>, <c>Mid$</c> and <c>MidB$</c> statements.
/// </summary>
/// <remarks>
/// A replacement never changes the length of the string: it is the least of the length asked for, what is left of the
/// target, and what the value has to give. The <c>B</c> forms count in bytes, two to a character.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.5 Mid Statement")]
public sealed class MidStatementTests
{
    private static readonly Uri Workspace = TestUri.WorkspaceRoot();

    private static VBModuleFieldVariableMemberSymbol Variable(string name, VBType type)
        => new(Workspace, RuntimeSourceHarness.ModuleUri, name, ScopeKind.Module, type,
            SourceRange.Empty, SourceRange.Empty, AccessModifier.Implicit);

    // the value of S after `S = <initial>` and the statement, with S a String.
    private static string Mid(string initial, string statement, VBType? type = null)
    {
        var (outcome, values) = Run(type ?? VBStringType.TypeInfo, initial, statement);
        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        return (string)values["S"]!;
    }

    private static (RuntimeExecutionOutcome Outcome, Dictionary<string, object?> Values) Run(VBType type, string initial, string statement)
    {
        var symbol = Variable("S", type);
        var (session, outcome) = RuntimeSourceHarness.Run(fileSystem: null, [symbol], $"S = \"{initial}\"", statement);
        return (outcome, new() { ["S"] = session.Symbols.Resolver.GetValue(symbol).Value.BoxedValue });
    }

    [TestMethod]
    public void ASpanOfTheTarget_IsReplaced()
        => Assert.AreEqual("aXYZef", Mid("abcdef", "Mid(S, 2, 3) = \"XYZ\""));

    [TestMethod]
    public void WithNoLength_TheReplacementRunsThroughTheValue()
        => Assert.AreEqual("ab12ef", Mid("abcdef", "Mid(S, 3) = \"12\""));

    [TestMethod]
    public void ALongerValue_IsCutToTheLength()
        => Assert.AreEqual("aXYdef", Mid("abcdef", "Mid(S, 2, 2) = \"XYZ\""));

    [TestMethod]
    public void AShorterValue_ReplacesOnlyWhatItHas()
        => Assert.AreEqual("aXcdef", Mid("abcdef", "Mid(S, 2, 3) = \"X\""));

    [TestMethod]
    public void TheReplacement_StopsAtTheEndOfTheTarget_AndNeverLengthensIt()
        => Assert.AreEqual("abcdWX", Mid("abcdef", "Mid(S, 5) = \"WXYZ\""));

    [TestMethod]
    public void AnEmptyValue_ChangesNothing()
        => Assert.AreEqual("abcdef", Mid("abcdef", "Mid(S, 2) = \"\""));

    [TestMethod]
    public void ALengthOfZero_ChangesNothing()
        => Assert.AreEqual("abcdef", Mid("abcdef", "Mid(S, 2, 0) = \"XYZ\""));

    [TestMethod]
    public void TheDollarForm_IsTheSameStatement()
        => Assert.AreEqual("aXYZef", Mid("abcdef", "Mid$(S, 2, 3) = \"XYZ\""));

    [TestMethod]
    public void TheKeyword_IsCaseInsensitive()
        => Assert.AreEqual("aXYZef", Mid("abcdef", "MID(S, 2, 3) = \"XYZ\""));

    [TestMethod]
    public void AValueThatIsNotAString_IsLetCoercedToOne()
        => Assert.AreEqual("19945", Mid("12345", "Mid(S, 2, 2) = 99"));

    [TestMethod]
    public void AVariantTarget_HoldingAString_IsReplacedInPlace()
        => Assert.AreEqual("aXcdef", Mid("abcdef", "Mid(S, 2, 1) = \"X\"", VBVariantType.TypeInfo));

    [TestMethod]
    public void AFixedLengthTarget_KeepsItsWidth()
        => Assert.AreEqual("aXYdef", Mid("abcdef", "Mid(S, 2, 2) = \"XYZ\"", new VBFixedStringType(6)));

    [TestMethod]
    public void MidB_CountsBytes_TwoToACharacter()
    {
        // "abcd" is 61 00 62 00 63 00 64 00: the two bytes from byte 3 are the whole of "b", and "X" is two bytes.
        Assert.AreEqual("aXcd", Mid("abcd", "MidB(S, 3, 2) = \"X\""));
    }

    [TestMethod]
    public void MidB_StartingOnAnOddByte_SplitsACharacter()
    {
        // bytes 2-3 of 61 00 62 00 63 00 64 00 become 58 00: 61 58 | 00 00 | 63 00 | 64 00.
        Assert.AreEqual("塡\0cd", Mid("abcd", "MidB(S, 2, 2) = \"X\""));
    }

    [TestMethod]
    public void MidB_WithNoLength_StopsAtTheEndOfTheTargetInBytes()
        => Assert.AreEqual("abXY", Mid("abcd", "MidB$(S, 5) = \"XYZ\""));

    [TestMethod]
    [DataRow("Mid(S, 0, 1) = \"X\"", DisplayName = "a start of 0")]
    [DataRow("Mid(S, -1, 1) = \"X\"", DisplayName = "a negative start")]
    [DataRow("Mid(S, 7, 1) = \"X\"", DisplayName = "a start past the end of the string")]
    [DataRow("Mid(S, 2, -1) = \"X\"", DisplayName = "a negative length")]
    [DataRow("MidB(S, 13, 1) = \"X\"", DisplayName = "a byte start past the end of the string, in bytes")]
    public void ASpanThatIsNotInTheString_IsError5(string statement)
    {
        var (outcome, _) = Run(VBStringType.TypeInfo, "abcdef", statement);

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void TheLastCharacter_IsInTheString()
        => Assert.AreEqual("abcdeX", Mid("abcdef", "Mid(S, 6, 1) = \"X\""));

    [TestMethod]
    public void MidB_AtTheLastCharactersFirstByte_ReplacesIt()
        => Assert.AreEqual("abcdeX", Mid("abcdef", "MidB(S, 11, 2) = \"X\""));

    // every Start and Length of Mid and MidB, and each left out, over targets and values of an even and an odd number of bytes: the String the
    // statement leaves is the one a Byte() of the target's data, with the value's bytes written over the span, makes. A count of mismatches each.
    [TestMethod]
    public async Task EveryStartAndLength_LeavesTheBytesAByteArrayWouldHold()
    {
        var printed = await Cli.ModuleWorkspace.RunAsync([], Cli.ModuleWorkspace.StandardModule("Program",
            "Option Explicit",
            "Private Checked As Long",
            "Public Sub Main()",
            "    Debug.Print Mismatches(\"ABCD\", \"xy\"); Mismatches(LeftB$(\"ABCD\", 7), \"xy\"); Mismatches(\"ABCD\", LeftB$(\"xyz\", 3))",
            "    Debug.Print Mismatches(LeftB$(\"ABCD\", 7), LeftB$(\"xyz\", 5)); Mismatches(LeftB$(\"A\", 1), LeftB$(\"x\", 1)); Checked",
            "End Sub",
            "Private Function Mismatches(ByVal S As String, ByVal V As String) As Long",
            "    Dim St As Long, Ln As Long, N As Long, T As String",
            "    For St = 1 To LenB(S)",
            "        For Ln = -1 To LenB(S) + 1",
            "            T = S",
            "            If Ln < 0 Then MidB(T, St) = V Else MidB(T, St, Ln) = V",
            "            If Not Alike(T, S, V, St, Ln) Then N = N + 1",
            "            If St <= Len(S) Then",
            "                T = S",
            "                If Ln < 0 Then Mid(T, St) = V Else Mid(T, St, Ln) = V",
            "                If Not Alike(T, S, V, 2 * St - 1, 2 * Fewest(IIf(Ln < 0, Len(S), Ln), Len(S) - St + 1, Len(V))) Then N = N + 1",
            "            End If",
            "        Next",
            "    Next",
            "    Mismatches = N",
            "End Function",
            "Private Function Fewest(ByVal A As Long, ByVal B As Long, ByVal C As Long) As Long",
            "    Fewest = A",
            "    If B < Fewest Then Fewest = B",
            "    If C < Fewest Then Fewest = C",
            "End Function",
            "Private Function Alike(ByVal T As String, ByVal S As String, ByVal V As String, ByVal St As Long, ByVal Ln As Long) As Boolean",
            "    Dim B() As Byte, BV() As Byte, BT() As Byte, K As Long, X As Long",
            "    B = S: BV = V: BT = T",
            "    X = Fewest(IIf(Ln < 0, LenB(S), Ln), LenB(S) - St + 1, LenB(V))",
            "    For K = 0 To X - 1",
            "        B(St - 1 + K) = BV(K)",
            "    Next",
            "    Checked = Checked + 1",
            "    If UBound(BT) <> UBound(B) Then Exit Function",
            "    For K = 0 To UBound(B)",
            "        If BT(K) <> B(K) Then Exit Function",
            "    Next",
            "    Alike = True",
            "End Function"));

        // each pair checks LenB + 3 lengths (one left out) at LenB starts of MidB and Len starts of Mid: 8, 7, 8, 7 and 1 bytes; 4, 3, 4, 3 and 0 characters.
        var checkedStatements = new[] { (8, 4), (7, 3), (8, 4), (7, 3), (1, 0) }.Sum(target => (target.Item1 + 3) * (target.Item1 + target.Item2));
        CollectionAssert.AreEqual(new[] { "0  0  0", $"0  0  {checkedStatements}" }, printed, string.Join(" | ", printed));
    }

    // a Mid statement of a 10,000-character String makes the one String it leaves: what it allocates is that String's 20,000 bytes, not a copy of the
    // target's data besides. What the loop allocates is read where it runs, between two Debug.Print, on the thread that runs it: the parsing and the
    // session around it, which earlier tests in the process change, are outside it.
    [TestMethod]
    [DataRow("Mid$(S, I, 1) = \"x\"", DisplayName = "Mid$")]
    [DataRow("MidB$(S, 2 * I - 1, 2) = \"x\"", DisplayName = "MidB$")]
    public void AStatementOfALongString_MakesOnlyTheStringItLeaves(string statement)
    {
        const int Length = 10_000;
        const int Statements = 100;
        var symbols = new Symbol[] { Variable("S", VBStringType.TypeInfo), Variable("I", VBLongType.TypeInfo) };
        var marks = new AllocationMarks();
        var (session, outcome) = RuntimeSourceHarness.Run(null, symbols, marks, standardLibrary: true,
            $"S = Space$({Length})", "Debug.Print", $"For I = 1 To {Statements}", statement, "Next", "Debug.Print");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        var text = (string)session.Symbols.Resolver.GetValue(symbols[0]).Value.BoxedValue!;
        Assert.AreEqual(Statements, text.Count(character => character == 'x'), "the statement ran each time");
        Assert.HasCount(2, marks.Allocated, "a mark before the loop and one after it");

        // the String a statement leaves holds its Length characters, two bytes each.
        var each = (marks.Allocated[1] - marks.Allocated[0]) / Statements;
        Assert.IsGreaterThanOrEqualTo(2L * Length, each, $"{statement} allocated {each:N0} bytes each: less than the String it leaves");
        Assert.IsLessThan(3L * Length, each, $"{statement} allocated {each:N0} bytes each: the String it leaves is {2 * Length:N0} and more");
    }

    // the bytes the thread that runs a body has allocated, read at each Debug.Print line it ends.
    private sealed class AllocationMarks : IRuntimeOutput
    {
        public List<long> Allocated { get; } = new(2);

        public int LinePosition => 1;

        public void Write(string text) { }

        public void WriteLine() => Allocated.Add(GC.GetAllocatedBytesForCurrentThread());
    }
}
