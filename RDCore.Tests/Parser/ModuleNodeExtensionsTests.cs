using RDCore.Parsing;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;

namespace RDCore.Tests.Parser;

[TestClass]
public sealed class ModuleNodeExtensionsTests
{
    private static ModuleNode Parse(string source)
    {
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), source);
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

    [TestMethod]
    public void HasOptionExplicit_True_WhenModuleDeclaresIt()
    {
        var module = Parse("Attribute VB_Name = \"M1\"\r\nOption Explicit\r\nPublic X As Long\r\n");

        Assert.IsTrue(module.HasOptionExplicit());
    }

    [TestMethod]
    public void HasOptionExplicit_False_WhenModuleDoesNotDeclareIt()
    {
        var module = Parse("Attribute VB_Name = \"M1\"\r\nPublic X As Long\r\n");

        Assert.IsFalse(module.HasOptionExplicit());
    }

    [TestMethod]
    [DataRow("Option Compare Text", OptionCompare.Text)]
    [DataRow("Option Compare Binary", OptionCompare.Binary)]
    [DataRow("Option Compare Database", OptionCompare.Database)]
    [DataRow("Option Explicit", OptionCompare.Binary, DisplayName = "no Option Compare directive: MS-VBAL 5.2.1.1 says binary")]
    public void GetOptionCompare_IsTheDeclaredComparisonMode(string directive, OptionCompare expected)
    {
        var module = Parse($"Attribute VB_Name = \"M1\"\r\n{directive}\r\nPublic X As Long\r\n");

        Assert.AreEqual(expected, module.GetOptionCompare());
    }

    [TestMethod]
    public void IsCreatable_True_WhenModuleDeclaresNoVB_Creatable()
        // VBE's own default: a class module that declares no Attribute VB_Creatable is creatable.
    {
        var module = Parse("Attribute VB_Name = \"M1\"\r\nPublic X As Long\r\n");

        Assert.IsTrue(module.IsCreatable());
    }

    [TestMethod]
    public void IsCreatable_False_WhenModuleDeclaresVB_CreatableFalse()
    {
        var module = Parse("Attribute VB_Name = \"M1\"\r\nAttribute VB_Creatable = False\r\nPublic X As Long\r\n");

        Assert.IsFalse(module.IsCreatable());
    }

    [TestMethod]
    public void IsCreatable_True_WhenModuleDeclaresVB_CreatableTrue()
    {
        var module = Parse("Attribute VB_Name = \"M1\"\r\nAttribute VB_Creatable = True\r\nPublic X As Long\r\n");

        Assert.IsTrue(module.IsCreatable());
    }

    [TestMethod]
    public void IsPredeclared_False_WhenModuleDeclaresNoVB_PredeclaredId()
        // VBE's own default: a class module that declares no Attribute VB_PredeclaredId is not predeclared.
    {
        var module = Parse("Attribute VB_Name = \"M1\"\r\nPublic X As Long\r\n");

        Assert.IsFalse(module.IsPredeclared());
    }

    [TestMethod]
    public void IsPredeclared_True_WhenModuleDeclaresVB_PredeclaredIdTrue()
    {
        var module = Parse("Attribute VB_Name = \"M1\"\r\nAttribute VB_PredeclaredId = True\r\nPublic X As Long\r\n");

        Assert.IsTrue(module.IsPredeclared());
    }

    [TestMethod]
    public void IsPredeclared_False_WhenModuleDeclaresVB_PredeclaredIdFalse()
    {
        var module = Parse("Attribute VB_Name = \"M1\"\r\nAttribute VB_PredeclaredId = False\r\nPublic X As Long\r\n");

        Assert.IsFalse(module.IsPredeclared());
    }

    [TestMethod]
    public void IsPredeclared_IsNotConfusedByAnotherAttribute()
    {
        var module = Parse("Attribute VB_Name = \"M1\"\r\nAttribute VB_Creatable = True\r\nPublic X As Long\r\n");

        Assert.IsFalse(module.IsPredeclared());
    }

    private static readonly string MembersWithAttributes = string.Join("\r\n",
        "Attribute VB_Name = \"M1\"",
        "Public X As Long",
        "Public Function Item(ByVal Index As Long) As Long",
        "Attribute Item.VB_UserMemId = 0",
        "Attribute Item.VB_Description = \"Gets an \"\"item\"\".\"",
        "End Function",
        "Public Sub Other()",
        "Attribute Item.VB_Description = \"Written in another member's body.\"",
        "End Sub",
        "");

    [TestMethod]
    public void AMembersAttributes_AreFoundByItsName_WithoutRegardToCase()
    {
        var module = Parse(MembersWithAttributes);

        Assert.AreEqual(0, module.GetMemberUserMemId("ITEM"));
        Assert.AreEqual("Gets an \"item\".", module.GetMemberDescription("item"));
    }

    [TestMethod]
    public void AnAttributeWrittenInAnotherMembersBody_IsNotTheMembers()
    {
        var module = Parse(MembersWithAttributes);

        Assert.IsNull(module.GetMemberDescription("Other"));
        Assert.IsNull(module.GetMemberUserMemId("Other"));
    }

    [TestMethod]
    public void AVariable_HasNoMemberAttribute()
        => Assert.IsNull(Parse(MembersWithAttributes).GetMemberDescription("X"));
}
