using RDCore.SDK.Workspace;

namespace RDCore.Tests.Workspace;

[TestClass]
public sealed class ModuleNameTests
{
    [TestMethod]
    [DataRow("Attribute VB_Name = \"Foo\"\r\nOption Explicit\r\n", "Foo")]
    [DataRow("\r\nAttribute VB_Name = \"Foo\"\r\n", "Foo")]
    [DataRow("   Attribute   VB_Name   =   \"Spaced\"   \r\n", "Spaced")]
    [DataRow("attribute vb_name = \"LowerKeywords\"\r\n", "LowerKeywords")]
    [DataRow("Attribute VB_Name = \"Say \"\"Hi\"\"\"\r\n", "Say \"Hi\"")]
    public void FromSource_ReturnsDeclaredName(string source, string expected)
        => Assert.AreEqual(expected, ModuleName.FromSource(source));

    [TestMethod]
    [DataRow("Option Explicit\r\nPublic X As Long\r\n")]
    [DataRow("")]
    [DataRow(null)]
    [DataRow("Attribute Foo.VB_Name = \"Qualified\"\r\n")] // member-qualified: not a module name
    public void FromSource_NoModuleLevelAttribute_ReturnsNull(string? source)
        => Assert.IsNull(ModuleName.FromSource(source));

    [TestMethod]
    [DataRow("src/Mod1.bas", "Mod1")]
    [DataRow("src\\legacy\\oldName.bas", "oldName")]
    [DataRow("Class1.cls", "Class1")]
    [DataRow("NoExtension", "NoExtension")]
    public void FromFileName_StripsDirectoryAndExtension(string relativeUri, string expected)
        => Assert.AreEqual(expected, ModuleName.FromFileName(relativeUri));

    [TestMethod]
    public void Resolve_PrefersVBName_OverFileName()
        => Assert.AreEqual("RealName", ModuleName.Resolve("Attribute VB_Name = \"RealName\"\r\n", "src/whatever.bas"));

    [TestMethod]
    public void Resolve_FallsBackToFileName_WhenNoAttribute()
        => Assert.AreEqual("whatever", ModuleName.Resolve("Option Explicit\r\n", "src/whatever.bas"));

    [TestMethod]
    public void Resolve_FallsBackToFileName_WhenSourceIsNull()
        => Assert.AreEqual("whatever", ModuleName.Resolve(null, "src/whatever.bas"));
}
