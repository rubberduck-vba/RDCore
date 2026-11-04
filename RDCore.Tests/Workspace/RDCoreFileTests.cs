using RDCore.SDK.Workspace;

namespace RDCore.Tests.Workspace;

[TestClass]
public sealed class RDCoreFileTests
{
    [TestMethod]
    [DataRow("README.md", ".md")]
    [DataRow("docs/notes.v2.txt", ".txt")]
    [DataRow("src/Module1.bas", ".bas")]
    [DataRow("LICENSE", "")]
    public void Extension_IsTheFileNamesLastExtension_WithItsPeriod(string relativeUri, string expected)
    {
        Assert.AreEqual(expected, new RDCoreFile { RelativeUri = relativeUri }.Extension);
    }
}
