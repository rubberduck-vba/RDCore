using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// The <c>LSet</c> and <c>RSet</c> statements (<strong>MS-VBAL §5.4.3.6</strong>, <strong>MS-VBAL §5.4.3.7</strong>) on a target that is not a plain
/// variable: a field of a record, an element of an array, a field of a record in an array. The target is a <c>bound-variable-expression</c> all the
/// same: a text is fitted into the length it already has, and a record copied over a record field or an array element fills it in place.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.6 LSet")]
[TestCategory("MS-VBAL 5.4.3.7 RSet")]
public sealed class FixedAssignmentTargetTests
{
    private const string Types =
        "Private Type TS\r\n    S As String\r\n    F As String * 5\r\n    V As Variant\r\nEnd Type\r\n" +
        "Private Type TOuter\r\n    Id As Long\r\n    Inner As TS\r\nEnd Type\r\n";

    private static string Program(params string[] body)
        => "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n" + Types +
           "Public Sub Main()\r\n    Dim T As TS, O As TOuter, R(1) As TS, A(1) As String\r\n" +
           string.Join("\r\n", body.Select(line => "    " + line)) + "\r\nEnd Sub\r\n";

    private static async Task AssertInBothEnginesAsync(string program, params string[] expected)
    {
        foreach (var engine in new[] { VBExecutionEngine.Interpreter, VBExecutionEngine.Compiled })
        {
            var output = await RunAsync([], program, engine);
            CollectionAssert.AreEqual(expected, output, $"{engine}: {string.Join(" | ", output)}");
        }
    }

    [TestMethod]
    [DataRow("LSet", "T.S = \"abcde\"", "T.S", "[x    ]", DisplayName = "LSet of a String field")]
    [DataRow("RSet", "T.S = \"abcde\"", "T.S", "[    x]", DisplayName = "RSet of a String field")]
    [DataRow("LSet", "T.F = \"abcde\"", "T.F", "[x    ]", DisplayName = "LSet of a fixed-length String field")]
    [DataRow("RSet", "T.F = \"abcde\"", "T.F", "[    x]", DisplayName = "RSet of a fixed-length String field")]
    [DataRow("LSet", "T.V = \"abcde\"", "T.V", "[x    ]", DisplayName = "LSet of a Variant field holding a String")]
    [DataRow("LSet", "O.Inner.S = \"abcde\"", "O.Inner.S", "[x    ]", DisplayName = "LSet of a field of a record field")]
    [DataRow("LSet", "A(1) = \"abcde\"", "A(1)", "[x    ]", DisplayName = "LSet of an array element")]
    [DataRow("RSet", "A(1) = \"abcde\"", "A(1)", "[    x]", DisplayName = "RSet of an array element")]
    [DataRow("LSet", "R(1).S = \"abcde\"", "R(1).S", "[x    ]", DisplayName = "LSet of a field of an array element")]
    public async Task ATargetThatIsNoPlainVariable_IsFittedIntoItsOwnLength(string statement, string fill, string target, string expected)
        => await AssertInBothEnginesAsync(Program(fill, $"{statement} {target} = \"x\"", $"Debug.Print \"[\" & {target} & \"]\""), expected);

    [TestMethod]
    public async Task ALongerText_IsCutToTheTargetsLength()
        => await AssertInBothEnginesAsync(Program("T.S = \"ab\"", "LSet T.S = \"wxyz\"", "Debug.Print \"[\" & T.S & \"]\"",
            "RSet T.S = \"wxyz\"", "Debug.Print \"[\" & T.S & \"]\""), "[wx]", "[wx]");

    [TestMethod]
    public async Task TheOtherFieldsOfTheRecord_AreLeftAsTheyWere()
        => await AssertInBothEnginesAsync(Program("O.Id = 7", "O.Inner.S = \"abc\"", "O.Inner.F = \"12345\"", "LSet O.Inner.S = \"x\"",
            "Debug.Print O.Id & \" [\" & O.Inner.S & \"] [\" & O.Inner.F & \"]\""), "7 [x  ] [12345]");

    // the byte copy of a record over a record that is a field, or an element of an array, fills that record in place.
    [TestMethod]
    [DataRow("O.Inner", "O.Id & \" \" & O.Inner.F", "7 12345", DisplayName = "a record field")]
    [DataRow("R(1)", "R(1).F", "12345", DisplayName = "an element of an array of records")]
    public async Task ARecordCopiedOverAFieldOrAnElement_IsCopiedInPlace(string target, string printed, string expected)
        => await AssertInBothEnginesAsync(Program("O.Id = 7", "T.F = \"12345\"", $"LSet {target} = T", $"Debug.Print {printed}"), expected);
}
