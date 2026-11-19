using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// The error a <c>Collection</c> raises for an item it does not have, as MS-VBA raises it: a number on an empty collection is 5, as a key is on any; a number
/// outside the items of one that has some is 9 (observed in Excel).
/// </summary>
[TestClass]
public sealed class CollectionIndexErrorTests
{
    private static async Task<string> Run(string body)
        => (await RunAsync([], "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n"
            + $"Public Sub Main()\r\nOn Error Resume Next\r\nDim e As New Collection, x As Variant\r\n{string.Join("\r\n", body.Split(" ;; "))}\r\nDebug.Print Err.Number\r\nEnd Sub\r\n")).Last();

    [TestMethod]
    [DataRow("x = e(1)", "5", DisplayName = "an empty collection's first")]
    [DataRow("x = e.Item(1)", "5", DisplayName = "an empty collection's first, by Item")]
    [DataRow("x = e(0)", "5", DisplayName = "an empty collection's 0")]
    [DataRow("e.Remove 1", "5", DisplayName = "an empty collection's first removed")]
    [DataRow("x = e(\"a\")", "5", DisplayName = "an empty collection's key")]
    [DataRow("e.Add 7 ;; x = e(2)", "9", DisplayName = "past the last")]
    [DataRow("e.Add 7 ;; x = e(0)", "9", DisplayName = "0")]
    [DataRow("e.Add 7 ;; x = e(-1)", "9", DisplayName = "below 0")]
    [DataRow("e.Add 7 ;; e.Remove 2", "9", DisplayName = "past the last removed")]
    [DataRow("e.Add 7 ;; x = e(\"b\")", "5", DisplayName = "a key it does not have")]
    [DataRow("e.Add 7 ;; e.Remove \"b\"", "5", DisplayName = "a key it does not have removed")]
    public async Task AnItemTheCollectionDoesNotHave_IsTheErrorMSVBARaises(string body, string number)
        => Assert.AreEqual(number, await Run(body));
}
