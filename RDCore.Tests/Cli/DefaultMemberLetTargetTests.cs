using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Let</c> of a value to a target whose declared type is a class with a default property assigns that property (<strong>MS-VBAL §5.4.3.8</strong>):
/// a field of an object, a property that has a <c>Property Get</c> and no <c>Property Let</c>, a field of a record, an element of an array and a variable
/// of another module, as well as a variable of the procedure.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.8 Let Statement")]
public sealed class DefaultMemberLetTargetTests
{
    // a class whose default member is a property with a Let.
    private static readonly (string Name, string Source) Defaulted = ("Defaulted", ClassModule("Defaulted", "Option Explicit", "Private m As Long",
        "Public Property Get Value() As Long", "Attribute Value.VB_UserMemId = 0", "Value = m", "End Property",
        "Public Property Let Value(ByVal v As Long)", "Attribute Value.VB_UserMemId = 0", "m = v", "End Property"));

    // a class that holds one in a field, and gives one through a Property Get that has no Let.
    private static readonly (string Name, string Source) Holder = ("Holder", ClassModule("Holder", "Option Explicit",
        "Public Item As New Defaulted",
        "Private mHeld As New Defaulted",
        "Public Property Get Held() As Defaulted", "Set Held = mHeld", "End Property"));

    private static string Program(params string[] body)
        => "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n" +
           "Private Type TRecord\r\n    D As Defaulted\r\nEnd Type\r\n" +
           "Public Sub Main()\r\n    Dim Q As New Holder, T As TRecord, A(1) As Defaulted\r\n    Set T.D = New Defaulted\r\n    Set A(1) = New Defaulted\r\n" +
           string.Join("\r\n", body.Select(line => "    " + line)) + "\r\nEnd Sub\r\n";

    private static async Task AssertInBothEnginesAsync(string program, params string[] expected)
    {
        foreach (var engine in new[] { VBExecutionEngine.Interpreter, VBExecutionEngine.Compiled })
        {
            using var pinned = RDCore.Runtime.Compiled.CompiledExecution.Running(engine);
            var output = await RunAsync([Defaulted, Holder], [("Shared", StandardModule("Shared", "Option Explicit", "Public S As New Defaulted"))], program);
            CollectionAssert.AreEqual(expected, output, $"{engine}: {string.Join(" | ", output)}");
        }
    }

    [TestMethod]
    [DataRow("Q.Item", DisplayName = "a field of an object")]
    [DataRow("Q.Held", DisplayName = "a property with a Get and no Let")]
    [DataRow("T.D", DisplayName = "a field of a record")]
    [DataRow("A(1)", DisplayName = "an element of an array")]
    [DataRow("Shared.S", DisplayName = "a variable of another module, named through it")]
    [DataRow("S", DisplayName = "a variable of another module")]
    public async Task ALetOfAValueToAnObjectTarget_AssignsItsDefaultProperty(string target)
        => await AssertInBothEnginesAsync(Program($"{target} = 5", $"Debug.Print {target}.Value"), "5");
}
