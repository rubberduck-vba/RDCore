using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// MS-VBAL §3.3.5.3: a foreign name in square brackets is the name written between them. A label written <c>[Try Again]:</c> is the label
/// <c>Try Again</c>, which a <c>GoTo</c> names with or without the brackets.
/// </summary>
[TestClass]
public sealed class BracketedLabelTests
{
    [TestMethod]
    [DataRow("GoTo [Try Again]", DisplayName = "named in brackets")]
    [DataRow("GoTo [try again]", DisplayName = "named in brackets, in another case")]
    public async Task ABracketedLabel_IsTheTargetOfAGoToThatNamesIt(string goTo)
        => CollectionAssert.AreEqual(new[] { "ok" }, await RunAsync([],
            $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n    {goTo}\r\n    Debug.Print \"skipped\"\r\n[Try Again]:\r\n    Debug.Print \"ok\"\r\nEnd Sub\r\n"));

    [TestMethod]
    public async Task ABracketedLabel_IsTheTargetOfAGoToThatNamesItWithoutBrackets()
        => CollectionAssert.AreEqual(new[] { "ok" }, await RunAsync([],
            "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n    GoTo Retry\r\n    Debug.Print \"skipped\"\r\n[Retry]:\r\n    Debug.Print \"ok\"\r\nEnd Sub\r\n"));
}
