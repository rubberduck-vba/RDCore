using RDCore.SDK.Workspace;

namespace RDCore.Tests.Workspace;

[TestClass]
public sealed class ModuleAnnotationsTests
{
    [TestMethod]
    [DataRow("'@OptionStrict\r\nAttribute VB_Name = \"M1\"\r\n")]
    [DataRow("Attribute VB_Name = \"M1\"\r\n'@OptionStrict\r\nOption Explicit\r\n")] // anywhere in the module
    [DataRow("'@optionstrict\r\n")]                              // annotation names are case-insensitive
    [DataRow("  '@OptionStrict\r\n")]                            // leading whitespace before the comment
    [DataRow("'@Folder(\"Foo\") @OptionStrict\r\n")]              // alongside another annotation
    [DataRow("'@OptionStrict: a note about why\r\n")]             // trailing plain-comment tail
    public void HasOptionStrict_True_WhenSourceCarriesTheAnnotation(string source)
        => Assert.IsTrue(ModuleAnnotations.HasOptionStrict(source));

    [TestMethod]
    [DataRow("Attribute VB_Name = \"M1\"\r\nOption Explicit\r\n")]
    [DataRow("'@Folder(\"Foo\")\r\n")]
    [DataRow("Dim s As String\r\ns = \"'@OptionStrict\"\r\n")]     // not a comment - a string literal
    public void HasOptionStrict_False_WhenSourceHasNoAnnotation(string source)
        => Assert.IsFalse(ModuleAnnotations.HasOptionStrict(source));

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    public void HasOptionStrict_False_WhenThereIsNoSource(string? source)
        => Assert.IsFalse(ModuleAnnotations.HasOptionStrict(source));
}
