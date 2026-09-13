using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using System.Text.Json;

namespace RDCore.Tests.Model.AST;

/// <summary>
/// Adversarial review, PRs #208-224, "worth knowing" section: AST JSON is exponential in expression
/// depth. Most node types stored the same children under 2-3 differently-named properties (the generic
/// <c>Children</c>/<c>Inputs</c> spine plus typed properties like <c>ConditionExpression</c>), and
/// <see cref="System.Text.Json"/> serialized all of them — so each nesting level multiplied the payload
/// instead of adding to it. Measured before this fix: an ordinary `1 + 1 + ... + 1` at depth 16 (not an
/// adversarial case — an everyday chained condition or concatenation) serialized to 339,958,068 bytes,
/// sent over a named pipe on every keystroke-triggered reparse (<c>PlatformJson</c>/<c>
/// ParsingClientService</c>). <see cref="SyntaxNodeJson"/> is the fix; these tests both prove it closes
/// the blowup and that it never drops data doing so.
/// </summary>
[TestClass]
public sealed class SyntaxNodeJsonRedundancyTests
{
    private static readonly Uri Uri = TestUri.TestModuleUri();

    // deliberately no resolver/modifier: every property gets written, so comparing against this is a
    // reliable, shrinking-independent content check.
    private static readonly JsonSerializerOptions VerboseOptions = new() { PropertyNameCaseInsensitive = true };

    private const string RichModuleSource = """
        Option Explicit
        Declare PtrSafe Function GetTickCount Lib "kernel32" () As Long
        Public Field1 As Long
        Public Const K = 1

        Sub Alpha(ByVal a As Long, Optional ByVal b As Long)
            Dim total As Long
            Dim arr() As Long
            ReDim arr(1 To 5)
            total = a + b - 1 * 2 / 3
            If Not total > 0 Then total = 0 Else total = 1
            If total > 0 Then
                total = total - 1
            ElseIf total < 0 Then
                total = total + 1
            Else
                total = 0
            End If
            For i = 1 To 10
                total = total + i
            Next i
            Dim item As Variant
            For Each item In arr
                total = total + item
            Next item
            Do While total < 100
                total = total + 1
            Loop
            Do
                total = total - 1
            Loop Until total = 0
            Select Case total
                Case 1
                    total = 1
                Case 2 To 5
                    total = 2
                Case Is > 10
                    total = 3
                Case Else
                    total = 0
            End Select
            With Nothing
                x = .Foo
                y = .Bar(1, 2)
            End With
            Call Beta(1, 2)
            Beta 3, 4
            x = Beta(1)(2)
            On Error GoTo ErrHandler
            Print #1, "x"; "y", "z"
            Open "file.txt" For Input Access Read As #1
            Name "old.txt" As "new.txt"
            GoTo Finish
        Finish:
            Exit Sub
        ErrHandler:
            Resume Next
        End Sub

        Function Beta(ByVal a As Long, Optional ByVal b As Long) As Long
            Beta = a + b
        End Function
        """;

    [TestMethod]
    public void RichModule_RoundTripsThroughTheSharedOptions_WithNoDataLoss()
    {
        var module = ParseSuccessfully(RichModuleSource);

        var shrunkJson = JsonSerializer.Serialize<SyntaxNode>(module, SyntaxNodeJson.Options);
        var rehydrated = JsonSerializer.Deserialize<SyntaxNode>(shrunkJson, SyntaxNodeJson.Options);

        // re-serialize BOTH sides with the verbose (non-shrinking) options: ImmutableArray<T> compares
        // by reference, not by element, so record equality can't be trusted here - comparing the full
        // JSON text sidesteps that and catches any property the round trip left null/default/wrong.
        var verboseOriginal = JsonSerializer.Serialize<SyntaxNode>(module, VerboseOptions);
        var verboseRehydrated = JsonSerializer.Serialize<SyntaxNode>(rehydrated!, VerboseOptions);

        Assert.AreEqual(verboseOriginal, verboseRehydrated);
    }

    [TestMethod]
    public void RichModule_SharedOptions_ProduceSmallerJsonThanVerbose()
    {
        var module = ParseSuccessfully(RichModuleSource);

        var shrunkJson = JsonSerializer.Serialize<SyntaxNode>(module, SyntaxNodeJson.Options);
        var verboseJson = JsonSerializer.Serialize<SyntaxNode>(module, VerboseOptions);

        Assert.IsLessThan(verboseJson.Length, shrunkJson.Length);
    }

    [TestMethod]
    // the headline repro: before this fix, depth 16 alone was 339,958,068 bytes.
    [DataRow(1)]
    [DataRow(4)]
    [DataRow(8)]
    [DataRow(16)]
    [DataRow(32)]
    public void NestedBinaryExpression_JsonSizeStaysLinearInDepth(int depth)
    {
        var expression = "1";
        for (var i = 0; i < depth; i++)
        {
            expression += " + 1";
        }
        var source = $"Sub Test()\r\n    x = {expression}\r\nEnd Sub\r\n";

        var module = ParseSuccessfully(source);
        var json = JsonSerializer.Serialize<SyntaxNode>(module, SyntaxNodeJson.Options);

        // generous linear bound (a fixed per-node overhead times depth, plus slack) - the point isn't
        // the exact constant, it's ruling out exponential growth: this would need 500MB+ at depth 16
        // pre-fix (measured 339,958,068 bytes before this fix; 15,270 after), and grow ~2x for every
        // +1 in depth beyond that.
        Assert.IsLessThan(2_000 * (depth + 1), json.Length);
    }

    [TestMethod]
    public void NestedBinaryExpression_RoundTripsWithNoDataLoss()
    {
        var source = "Sub Test()\r\n    x = 1 + 2 - 3 * 4 / 5 Mod 6 \\ 7 ^ 8 And 9 Or 10\r\nEnd Sub\r\n";
        var module = ParseSuccessfully(source);

        var shrunkJson = JsonSerializer.Serialize<SyntaxNode>(module, SyntaxNodeJson.Options);
        var rehydrated = JsonSerializer.Deserialize<SyntaxNode>(shrunkJson, SyntaxNodeJson.Options);

        var verboseOriginal = JsonSerializer.Serialize<SyntaxNode>(module, VerboseOptions);
        var verboseRehydrated = JsonSerializer.Serialize<SyntaxNode>(rehydrated!, VerboseOptions);

        Assert.AreEqual(verboseOriginal, verboseRehydrated);
    }

    private static ModuleNode ParseSuccessfully(string source)
    {
        var result = new ModuleParser().Parse(Uri, source);
        Assert.IsTrue(result.IsSuccess, string.Join(", ", result.SyntaxErrors.Select(e => e.Verbose)));
        return result.SyntaxTree!;
    }
}
