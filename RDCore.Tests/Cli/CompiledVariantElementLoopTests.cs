using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// The loops of the bench's two-dimensional <c>Variant</c> array cases: each element of an array of <c>Variant</c>s, or of the array a <c>Variant</c> holds,
/// assigned a number, then read and assigned again (MS-VBAL §5.4.3.8, §5.6.9.3). Compiled code runs them from start to end, with no statement stepped and
/// none recovered. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledVariantElementLoopTests
{
    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    // the procedure compiled, with nothing stepped and nothing recovered, printing what MS-VBA prints.
    private static async Task AssertRunByCompiledCodeAsync(string program, string procedure, string expected)
    {
        var result = await AssertAlikeAsync(program, procedure, recoveries: 0, expected: expected);
        Assert.IsEmpty(result.Stepped, string.Join("; ", result.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}")));
    }

    [TestMethod]
    public async Task AnArrayOfVariants_InTwoDimensions_IsFilledAndDoubledInCompiledCode()
        => await AssertRunByCompiledCodeAsync(Program(
            "Private Function Doubled() As Long",
            "    Dim Values() As Variant",
            "    ReDim Values(1 To 30, 1 To 10)",
            "    Dim R As Long",
            "    Dim C As Long",
            "    For R = 1 To 30",
            "        For C = 1 To 10",
            "            Values(R, C) = R + C",
            "        Next",
            "    Next",
            "    For R = 1 To 30",
            "        For C = 1 To 10",
            "            Values(R, C) = Values(R, C) * 2",
            "        Next",
            "    Next",
            "    Doubled = Values(30, 10) + Values(1, 1)",
            "End Function",
            "Public Sub Main()",
            "    Debug.Print Doubled()",
            "End Sub"), "Doubled", "84");

    [TestMethod]
    public async Task AnArrayAVariantHolds_InTwoDimensions_IsDoubledInCompiledCode()
        => await AssertRunByCompiledCodeAsync(Program(
            "Private Function Doubled() As Long",
            "    Dim Values() As Variant",
            "    ReDim Values(1 To 30, 1 To 10)",
            "    Dim R As Long",
            "    Dim C As Long",
            "    For R = 1 To 30",
            "        For C = 1 To 10",
            "            Values(R, C) = R + C",
            "        Next",
            "    Next",
            "    Dim V As Variant",
            "    V = Values",
            "    For R = 1 To 30",
            "        For C = 1 To 10",
            "            V(R, C) = V(R, C) * 2",
            "        Next",
            "    Next",
            "    Doubled = V(30, 10) + V(1, 1)",
            "End Function",
            "Public Sub Main()",
            "    Debug.Print Doubled()",
            "End Sub"), "Doubled", "84");
}
