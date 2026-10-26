using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// Operations whose operands are of two different types, end to end: the table of effective types has a row for each pair VBA evaluates (MS-VBAL 5.6.9).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.9 Operators")]
public sealed class MixedTypeOperationTests
{
    private static Task<string[]> RunMainAsync(params string[] lines)
        => RunAsync([], $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n");

    // a Boolean is an operand wherever an integral value is: True is -1.
    [TestMethod]
    public async Task ABoolean_AndANumber_AreAddedComparedAndCombined()
        => CollectionAssert.AreEqual(new[] { "1.5", "False", "7" }, await RunMainAsync(
            "Dim B As Boolean",
            "B = True",
            "Debug.Print B + 2.5",
            "Debug.Print B = 2.5",
            "Debug.Print B And 7&"));

    // MS-VBAL 5.6.9.3.3: the difference of two dates is the number of days between them.
    [TestMethod]
    public async Task TheDifference_OfTwoDates_IsADouble()
        => CollectionAssert.AreEqual(new[] { "Double", "31" }, await RunMainAsync(
            "Dim A As Date",
            "Dim B As Date",
            "A = #1/1/2000#",
            "B = #2/1/2000#",
            "Debug.Print TypeName(B - A)",
            "Debug.Print B - A"));

    // MS-VBAL 5.6.9.3.6-7: an Integer divided by a floating-point value is a Long, and a LongLong is a LongLong; Mod is the same.
    [TestMethod]
    public async Task TheIntegerDivision_OfAnIntegerByAFloatingPointValue_IsALong()
        => CollectionAssert.AreEqual(new[] { "Long", "LongLong", "Long" }, await RunMainAsync(
            "Dim I As Integer",
            "Dim L As LongLong",
            "I = 7",
            "L = 7",
            "Debug.Print TypeName(I \\ 2.5)",
            "Debug.Print TypeName(L \\ 2.5)",
            "Debug.Print TypeName(I Mod 2.5)"));

    // MS-VBA: a Boolean and a LongLong are combined as LongLongs, whichever side the Boolean is on (MS-VBAL 5.6.9.8's table names no Boolean there).
    [TestMethod]
    public async Task ABoolean_AndALongLong_AreCombinedAsLongLongs()
        => CollectionAssert.AreEqual(new[] { "5", "5", "LongLong", "LongLong" }, await RunMainAsync(
            "Debug.Print True And 5^",
            "Debug.Print 5^ And True",
            "Debug.Print TypeName(True And 5^)",
            "Debug.Print TypeName(5^ Or False)"));

    // MS-VBAL 5.6.9.8.1: Not negates a Boolean in a Boolean.
    [TestMethod]
    public async Task TheNot_OfABoolean_IsABoolean()
        => CollectionAssert.AreEqual(new[] { "Boolean", "False", "True" }, await RunMainAsync(
            "Debug.Print TypeName(Not True)",
            "Debug.Print Not True",
            "Debug.Print Not False"));

    // MS-VBAL 2.2: LongPtr is an alias of LongLong in a 64-bit environment, and is operated on as one.
    [TestMethod]
    public async Task ALongPtr_IsOperatedOn_AsWhatItIsAnAliasOf()
        => CollectionAssert.AreEqual(new[] { "6", "LongLong", "True", "-5", "-6" }, await RunMainAsync(
            "Dim P As LongPtr",
            "P = 5",
            "Debug.Print P + 1",
            "Debug.Print TypeName(P + 1)",
            "Debug.Print P = 5",
            "Debug.Print -P",
            "Debug.Print Not P"));

    // MS-VBAL 5.6.9.5: a Single and a LongLong are compared as Doubles.
    [TestMethod]
    public async Task ASingle_ComparedToALongLong_IsCompared()
        => CollectionAssert.AreEqual(new[] { "True" }, await RunMainAsync(
            "Dim S As Single",
            "Dim L As LongLong",
            "S = 1",
            "L = 1",
            "Debug.Print S = L"));
}
