using static RDCore.Tests.Cli.CompiledArrayFieldTests;
using static RDCore.Tests.Cli.CompiledDirectCallTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// The <c>&amp;</c> of a Variant in a <c>String</c> Let or a <c>Debug.Print</c>, compiled: its value type is known when it is evaluated, and it is a
/// <c>String</c> unless both operands are <c>Null</c> (<strong>MS-VBAL §5.6.9.4</strong>). Each operand compiled code reads is a <c>String</c>, so the two
/// are joined as Strings are; a Variant call of the library that returns <c>Null</c> hands its activation to the interpreter, which runs the rest
/// of it. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledVariantConcatTests
{
    // a Variant form of the library joined to a String, in a loop, and printed between Strings.
    [TestMethod]
    public async Task AVariantFormJoinedToAString_IsTheTextTheyMake()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Pairs(\"abcdef\"); \"|\"; Wrapped(\" x \")",
            "End Sub",
            "Private Function Pairs(ByVal V As Variant) As String",
            "    Dim R As String",
            "    Dim I As Integer",
            "    For I = 1 To 3",
            "        R = R & Left(V, 2) & UCase(Mid(V, 3, 1))",
            "    Next",
            "    Pairs = R",
            "End Function",
            "Private Function Wrapped(ByVal V As Variant) As String",
            "    Debug.Print \"<\" & Trim(V) & \">\"",
            "    Wrapped = Right(V, 2) & \"]\"",
            "End Function"), [], "abCabCabC|<x> ; x ]", ("Pairs", 0), ("Wrapped", 0));

    // a Variant that holds a number gives the text the library writes of it.
    [TestMethod]
    public async Task AVariantFormOfANumber_IsItsText()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Firsts(12345)",
            "End Sub",
            "Private Function Firsts(ByVal V As Variant) As String",
            "    Dim R As String",
            "    R = \"#\" & Left(V, 3)",
            "    Firsts = R",
            "End Function"), [], "#123", ("Firsts", 0));

    // Null joined to a String is the String: the call that returns Null hands its activation to the interpreter, which joins what it returned; a call
    // given a String is compiled code's.
    [TestMethod]
    public async Task NullJoinedToAString_IsTheString()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Joined(Null); \"|\"; Joined(\"yz\")",
            "End Sub",
            "Private Function Joined(ByVal V As Variant) As String",
            "    Dim R As String",
            "    R = \"a\" & Left(V, 1)",
            "    Joined = R",
            "End Function"), [], "a|ay", [], ("Joined", 0, 1));

    // Null joined to Null is Null, which a String Let refuses with error 94 (MS-VBAL §5.5.1.2.10), raised where the interpreter raises it.
    [TestMethod]
    public async Task NullJoinedToNull_IsError94()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    On Error Resume Next",
            "    Debug.Print Joined(Null)",
            "    Debug.Print \"err\"; Err.Number",
            "End Sub",
            "Private Function Joined(ByVal V As Variant) As String",
            "    Dim R As String",
            "    R = Left(V, 1) & Left(V, 2)",
            "    Joined = R",
            "End Function"), [], "err 94", [], ("Joined", 0, 1));
}
