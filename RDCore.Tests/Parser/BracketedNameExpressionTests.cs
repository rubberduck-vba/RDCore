using RDCore.Parsing;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Parser;

/// <summary>
/// A name written in brackets is a <em>foreign name</em> (<strong>MS-VBAL §3.3.5.3</strong>): the brackets are how it is written, and not part of it. In an
/// expression, as in a declaration, the name is the one between them, and the node says it was written in them.
/// </summary>
[TestClass]
public sealed class BracketedNameExpressionTests
{
    private static SimpleNameExpressionNode[] NamesIn(string statement)
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), $"Public Sub S()\r\n    {statement}\r\nEnd Sub\r\n");
        Assert.IsEmpty(parse.SyntaxErrors, string.Join(" | ", parse.SyntaxErrors.Select(error => $"{error.Location.Range}: {error.Verbose}")));
        return [.. parse.SyntaxTree!.Descendants().OfType<SimpleNameExpressionNode>()];
    }

    [TestMethod]
    [DataRow("x = [My Field]", "My Field", DisplayName = "a simple name")]
    [DataRow("x = [A1]", "A1", DisplayName = "a name a host could evaluate")]
    [DataRow("x = [A1].Value", "A1", DisplayName = "the owner of a member access")]
    [DataRow("x = o.[My Field]", "My Field", DisplayName = "the member of a member access")]
    [DataRow("o.[My Field] = 1", "My Field", DisplayName = "the member of a member access assigned")]
    [DataRow("Call o.[Do It]", "Do It", DisplayName = "the member of a member access called")]
    public void ANameInBrackets_IsTheNameBetweenThem_AndSaysItWasWrittenInThem(string statement, string name)
    {
        var node = Assert.ContainsSingle(NamesIn(statement).Where(node => node.IdentifierName == name), "the name, without its brackets");

        Assert.IsTrue(node.IsBracketed);
    }

    [TestMethod]
    [DataRow("x = Field", DisplayName = "a simple name")]
    [DataRow("x = o.Field", DisplayName = "a member access")]
    [DataRow("x = Field$", DisplayName = "a name with a type-declaration character")]
    public void ANameWithoutBrackets_SaysSo(string statement)
    {
        Assert.IsTrue(NamesIn(statement).All(node => !node.IsBracketed));
    }

    // the name the module declares in brackets is the name an expression in brackets refers to.
    [TestMethod]
    public async Task AVariableDeclaredInBrackets_IsTheVariableANameInBracketsRefersTo()
    {
        var output = await ModuleWorkspace.RunAsync([], string.Join("\r\n",
            "Attribute VB_Name = \"Program\"",
            "Private [My Field] As Long",
            "Public Sub Main()",
            "    [My Field] = 3",
            "    Debug.Print [My Field] + 1",
            "End Sub",
            ""));

        Assert.AreEqual("4", Assert.ContainsSingle(output));
    }
}
