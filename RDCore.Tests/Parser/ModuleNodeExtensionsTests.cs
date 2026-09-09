using RDCore.Parsing;
using RDCore.SDK.Model.AST.Declarations;

namespace RDCore.Tests.Parser;

[TestClass]
public sealed class ModuleNodeExtensionsTests
{
    private static ModuleNode Parse(string source, ModuleType moduleType = ModuleType.StdModule)
    {
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), moduleType, source);
        Assert.IsNotNull(result.SyntaxTree, "expected a parsed module");
        return result.SyntaxTree;
    }

    [TestMethod]
    public void GetDeclaredName_ReturnsVBName_WhenDeclared()
    {
        // the file this would live in is irrelevant; the name is the attribute value.
        var module = Parse("Attribute VB_Name = \"AcmeWidget\"\r\nOption Explicit\r\nPublic Sub Go()\r\nEnd Sub\r\n");

        Assert.AreEqual("AcmeWidget", module.GetDeclaredName());
    }

    [TestMethod]
    public void GetDeclaredName_ReturnsNull_WhenModuleDeclaresNoVBName()
    {
        var module = Parse("Option Explicit\r\nPublic Sub Go()\r\nEnd Sub\r\n");

        Assert.IsNull(module.GetDeclaredName());
    }

    [TestMethod]
    public void GetDeclaredName_UnescapesDoubledQuotes()
    {
        var module = Parse("Attribute VB_Name = \"a\"\"b\"\r\n");

        Assert.AreEqual("a\"b", module.GetDeclaredName());
    }
}
