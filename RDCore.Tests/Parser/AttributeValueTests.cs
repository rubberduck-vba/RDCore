using RDCore.Parsing;
using RDCore.SDK.Model.AST.Directives;

namespace RDCore.Tests.Parser;

/// <summary>
/// The value of an <c>Attribute</c> statement is its text as written, quotes included: a character that looks like another (a non-breaking space in a
/// <c>VB_Name</c>) is kept, so that a check of the name sees it.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §4.2 Modules")]
public sealed class AttributeValueTests
{
    [TestMethod]
    [DataRow("Module\u00A01", DisplayName = "a non-breaking space")]
    [DataRow("Module1", DisplayName = "a name as VBA writes it")]
    public void TheValueOfAnAttribute_IsItsTextAsWritten(string name)
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Module1.bas"), $"Attribute VB_Name = \"{name}\"\r\nOption Explicit\r\n");

        var attribute = Assert.ContainsSingle(parse.SyntaxTree!.Children.OfType<AttributeDirectiveNode>());
        Assert.AreEqual("VB_Name", attribute.Name);
        Assert.AreEqual($"\"{name}\"", attribute.Value);
    }
}
