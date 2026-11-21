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

    // MS-VBAL §5.4.3.8: a Let of an object is a Let of its default member, which is given the value Let-coerced to what it takes, in code that runs or not.
    [TestMethod]
    [DataRow("Dim o As Object", "", true, DisplayName = "an Object, whose default member is known when it runs")]
    [DataRow("Dim o As New Holder", "", true, DisplayName = "a class with a default member")]
    [DataRow("Dim o As Object", "Error 5", true, DisplayName = "an Object, after an error the procedure raises")]
    [DataRow("Dim o As New Plain", "", false, DisplayName = "a class with no default member, which raises 438 and takes no value")]
    public async Task TheValueOfALetOfAnObject_IsLetCoerced_UnlessItsClassHasNoDefaultMember(string declaration, string before, bool expected)
    {
        var holder = ModuleWorkspace.ClassModule("Holder", "Public Property Get Value() As Long", "Attribute Value.VB_UserMemId = 0", "Value = 1",
            "End Property", "Public Property Let Value(ByVal v As Long)", "Attribute Value.VB_UserMemId = 0", "End Property");
        var plain = ModuleWorkspace.ClassModule("Plain", "Public Tag As Long");
        var program = ModuleWorkspace.StandardModule("Program", "Option Explicit", "Public Sub Main()", declaration, "Dim n As Long", before, "o = n",
            "End Sub");

        var payload = await ModuleWorkspace.SemanticsAsync([("Holder", holder), ("Plain", plain)], program);
        var module = payload.Modules.Single(semantics => semantics.Module.Fragment.TrimStart('#') == "Program");
        Assert.IsEmpty(module.Procedures.SelectMany(procedure => procedure.CompileErrors));
        var value = module.Procedures.SelectMany(procedure => procedure.Expressions)
            .Single(fact => fact is { Classification: ExpressionClassification.Variable, DeclaredType: "Long" });

        Assert.AreEqual(expected, value.IsLetCoerced);
    }
}
