using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Workspace;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.Workspace;

/// <summary>
/// A program of the platform's BASIC is lines with nothing around them; as far as the language is concerned it is the module whose one procedure the lines are the body of.
/// </summary>
[TestClass]
public sealed class BasicProgramTextTests
{
    [TestMethod]
    public void TheLinesOfAProgram_AreTheBodyOfItsProcedure()
        => Assert.AreEqual("Public Sub Main()\r\n100 X = 1\r\n110 PRINT X\r\nEnd Sub\r\n", BasicProgramText.ToModuleSource("100 X = 1\r\n110 PRINT X\r\n"));

    [TestMethod]
    public void ALastLineWithNoLineEnding_GetsOne_SoThatTheFooterIsOnALineOfItsOwn()
        => Assert.AreEqual("Public Sub Main()\r\n100 X = 1\r\nEnd Sub\r\n", BasicProgramText.ToModuleSource("100 X = 1"));

    [TestMethod]
    public void AProgramOfNoLines_IsAnEmptyProcedure()
        => Assert.AreEqual("Public Sub Main()\r\nEnd Sub\r\n", BasicProgramText.ToModuleSource(""));

    [TestMethod]
    [DataRow("x.rdc", true)]
    [DataRow(@"C:\work\Hello.RDC", true)]
    [DataRow("x.bas", false)]
    [DataRow("x.cls", false)]
    [DataRow("rdc", false)]
    public void AProgramIsAFileOfTheExtensionOfTheBasic(string path, bool isProgram)
        => Assert.AreEqual(isProgram, BasicProgramText.IsProgram(path));

    [TestMethod]
    public void ARangeOfTheModule_IsWhereItIsInTheText()
    {
        var range = BasicProgramText.ToTextRange(new Range(new Position(3, 4), new Position(3, 9)));

        Assert.AreEqual(new Range(new Position(2, 4), new Position(2, 9)), range);
    }

    [TestMethod]
    public void ARangeOfTheHeader_IsTheStartOfTheText()
        => Assert.AreEqual(new Range(new Position(0, 0), new Position(0, 0)), BasicProgramText.ToTextRange(new Range(new Position(0, 2), new Position(0, 6))));
}
