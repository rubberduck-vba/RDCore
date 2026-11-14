using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Semantics;

/// <summary>
/// Whether the static pass states that an argument's value is taken and Let-coerced to the parameter's type (MS-VBAL 5.5.1.1): a variable of the parameter's
/// own type given to a <c>ByRef</c> parameter is passed by reference (MS-VBAL 5.3.1.11), and its value is not taken.
/// </summary>
[TestClass]
public sealed class LetCoercedFactsTests
{
    private static async Task<ExpressionFactDto> ArgumentAsync(string call)
    {
        var program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n"
            + "Private Sub ByLng(ByVal x As Long)\r\nEnd Sub\r\nPrivate Sub RefLng(ByRef x As Long)\r\nEnd Sub\r\n"
            + $"Public Sub Main()\r\nDim n As Long\r\n{call}\r\nEnd Sub\r\n";
        var payload = await ModuleWorkspace.SemanticsAsync([], program);
        var facts = payload.Modules.Single().Procedures.SelectMany(procedure => procedure.Expressions);
        return facts.Single(fact => fact is { Classification: ExpressionClassification.Variable, DeclaredType: "Long" });
    }

    [TestMethod]
    [DataRow("ByLng n", true, DisplayName = "a ByVal parameter: the value is taken")]
    [DataRow("RefLng n", false, DisplayName = "a ByRef parameter of its own type: the variable is passed")]
    public async Task AnArgument_IsLetCoerced_UnlessTheVariableIsPassed(string call, bool expected)
        => Assert.AreEqual(expected, (await ArgumentAsync(call)).IsLetCoerced);
}
