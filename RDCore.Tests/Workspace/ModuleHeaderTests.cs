using RDCore.SDK.Workspace;

namespace RDCore.Tests.Workspace;

[TestClass]
public sealed class ModuleHeaderTests
{
    private const string ClassModule =
        "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\nAttribute VB_Name = \"C1\"\r\n";

    private const string DesignerModule =
        "VERSION 5.00\r\nBegin {C62A69F0-16DC-11CE-9E98-00AA00574A4F} Form1 \r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n";

    private const string StandardModule =
        "Attribute VB_Name = \"M1\"\r\nOption Explicit\r\nPublic X As Long\r\n";

    [TestMethod]
    [DataRow(ClassModule)]
    [DataRow(DesignerModule)]
    [DataRow("version 1.0 class\r\n")]          // keywords are case-insensitive
    [DataRow("  VERSION 1.0 CLASS  \r\n")]      // leading/trailing whitespace
    [DataRow("\r\nVERSION 1.0 CLASS\r\n")]      // a leading blank line
    public void IsClassModule_True_WhenSourceHasAVersionHeader(string source)
        => Assert.AreEqual(true, ModuleHeader.IsClassModule(source));

    [TestMethod]
    [DataRow(StandardModule)]
    [DataRow("Option Explicit\r\n' VERSION 1.0 CLASS was here once\r\n")] // not on its own line
    [DataRow("Attribute VB_Name = \"NotAClass\"\r\n")]
    public void IsClassModule_False_WhenSourceHasNoVersionHeader(string source)
        => Assert.AreEqual(false, ModuleHeader.IsClassModule(source));

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    public void IsClassModule_Null_WhenThereIsNoSource(string? source)
        => Assert.IsNull(ModuleHeader.IsClassModule(source));
}
