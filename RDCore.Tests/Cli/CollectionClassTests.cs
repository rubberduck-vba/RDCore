using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §6.1.3.1</strong> the <c>Collection</c> class: a sequence of values, each reachable by its position and, when it was added with a key, by that.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 6.1.3.1 Collection Object")]
public sealed class CollectionClassTests
{
    private static async Task<string[]> RunAsync(params string[] body)
    {
        var result = await SupportedLanguageTests.RunAsync(expression: "", statement: string.Join("\r\n", body));

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, $"{result.ErrorMessage} {string.Join("; ", result.Diagnostics ?? [])}");
        return [.. result.Output.Select(line => line.Trim())];
    }

    // the number an error was raised with, which the body prints after turning error handling on.
    private static async Task<string> ErrorOfAsync(params string[] body)
        => (await RunAsync(["Dim c As New Collection", .. body.SkipLast(1), "On Error Resume Next", body[^1], "Debug.Print Err.Number"]))[^1];

    [TestMethod]
    public async Task ANewCollection_IsEmpty()
        => CollectionAssert.AreEqual(new[] { "0" }, await RunAsync("Dim c As New Collection", "Debug.Print c.Count"));

    [TestMethod]
    public async Task AMember_IsAddedAtTheEnd_AndCounted()
        => CollectionAssert.AreEqual(new[] { "3", "10", "20", "30" },
            await RunAsync("Dim c As New Collection", "c.Add 10", "c.Add 20", "c.Add 30", "Debug.Print c.Count", "Debug.Print c(1)", "Debug.Print c(2)", "Debug.Print c(3)"));

    [TestMethod]
    public async Task ItemIsTheDefaultMember_SoTheTwoSpellingsAreTheSame()
        => CollectionAssert.AreEqual(new[] { "20", "20" },
            await RunAsync("Dim c As New Collection", "c.Add 10", "c.Add 20", "Debug.Print c.Item(2)", "Debug.Print c(2)"));

    [TestMethod]
    public async Task AMemberAddedWithAKey_IsReachedByIt_WhateverTheCaseOfItsLetters()
        => CollectionAssert.AreEqual(new[] { "7", "7", "7" },
            await RunAsync("Dim c As New Collection", "c.Add 7, \"Seven\"", "Debug.Print c(\"Seven\")", "Debug.Print c(\"SEVEN\")", "Debug.Print c.Item(\"seven\")"));

    [TestMethod]
    public async Task AMemberAddedWithAKey_IsStillReachedByItsPosition()
        => CollectionAssert.AreEqual(new[] { "7" },
            await RunAsync("Dim c As New Collection", "c.Add 1", "c.Add 7, \"Seven\"", "Debug.Print c(2)"));

    [TestMethod]
    public async Task AMember_CanBeOfAnyType()
        => CollectionAssert.AreEqual(new[] { "1", "text", "2.5", "True" },
            await RunAsync("Dim c As New Collection", "c.Add 1", "c.Add \"text\"", "c.Add 2.5", "c.Add True",
                "Debug.Print c(1)", "Debug.Print c(2)", "Debug.Print c(3)", "Debug.Print c(4)"));

    [TestMethod]
    public async Task AMember_IsAddedBeforeAnother()
        => CollectionAssert.AreEqual(new[] { "1", "2", "3" },
            await RunAsync("Dim c As New Collection", "c.Add 1", "c.Add 3", "c.Add 2, , 2", "Debug.Print c(1)", "Debug.Print c(2)", "Debug.Print c(3)"));

    [TestMethod]
    public async Task AMember_IsAddedAfterAnother()
        => CollectionAssert.AreEqual(new[] { "1", "2", "3" },
            await RunAsync("Dim c As New Collection", "c.Add 1", "c.Add 3", "c.Add 2, , , 1", "Debug.Print c(1)", "Debug.Print c(2)", "Debug.Print c(3)"));

    [TestMethod]
    public async Task AMember_IsAddedBeforeOrAfterAMemberThatIsNamedByItsKey()
        => CollectionAssert.AreEqual(new[] { "a", "b", "c" },
            await RunAsync("Dim c As New Collection", "c.Add \"b\", \"B\"", "c.Add \"a\", , \"B\"", "c.Add \"c\", , , \"B\"",
                "Debug.Print c(1)", "Debug.Print c(2)", "Debug.Print c(3)"));

    [TestMethod]
    public async Task AMember_IsRemovedByPosition_AndTheOthersCloseUp()
        => CollectionAssert.AreEqual(new[] { "2", "2", "3" },
            await RunAsync("Dim c As New Collection", "c.Add 1", "c.Add 2", "c.Add 3", "c.Remove 1", "Debug.Print c.Count", "Debug.Print c(1)", "Debug.Print c(2)"));

    [TestMethod]
    public async Task AMember_IsRemovedByKey()
        => CollectionAssert.AreEqual(new[] { "1", "1" },
            await RunAsync("Dim c As New Collection", "c.Add 1", "c.Add 2, \"two\"", "c.Remove \"TWO\"", "Debug.Print c.Count", "Debug.Print c(1)"));

    [TestMethod]
    public async Task AKeyThatWasRemoved_CanBeUsedAgain()
        => CollectionAssert.AreEqual(new[] { "9" },
            await RunAsync("Dim c As New Collection", "c.Add 1, \"k\"", "c.Remove \"k\"", "c.Add 9, \"k\"", "Debug.Print c(\"k\")"));

    [TestMethod]
    public async Task Collections_AreEachTheirOwn()
        => CollectionAssert.AreEqual(new[] { "1", "0" },
            await RunAsync("Dim a As New Collection", "Dim b As New Collection", "a.Add 1", "Debug.Print a.Count", "Debug.Print b.Count"));

    [TestMethod]
    public async Task ACollection_CreatedWithSetAndNew_IsOne()
        => CollectionAssert.AreEqual(new[] { "x" },
            await RunAsync("Dim c As Collection", "Set c = New Collection", "c.Add \"x\"", "Debug.Print c(1)"));

    [TestMethod]
    public async Task ACollection_IsTheSameObjectThroughEveryVariableThatHoldsIt()
        => CollectionAssert.AreEqual(new[] { "1" },
            await RunAsync("Dim a As New Collection", "Dim b As Collection", "Set b = a", "b.Add 5", "Debug.Print a.Count"));

    [TestMethod]
    public async Task AMember_CanBeAnObject_WhichIsTheObjectItself()
        => CollectionAssert.AreEqual(new[] { "1", "5" },
            await RunAsync("Dim c As New Collection", "Dim d As New Collection", "d.Add 5", "c.Add d",
                "Debug.Print c.Count", "Debug.Print c(1)(1)"));

    [TestMethod]
    public async Task AnObjectThatIsAMember_IsReachedAsAnObject()
        => CollectionAssert.AreEqual(new[] { "2" },
            await RunAsync("Dim c As New Collection", "Dim d As New Collection", "d.Add 5", "d.Add 6", "c.Add d", "Debug.Print c(1).Count"));

    [TestMethod]
    public async Task APositionThatIsNotThere_IsSubscriptOutOfRange()
    {
        Assert.AreEqual("9", await ErrorOfAsync("c.Add 1", "Debug.Print c(2)"));
        Assert.AreEqual("9", await ErrorOfAsync("Debug.Print c(0)"));
        Assert.AreEqual("9", await ErrorOfAsync("c.Remove 1"));
    }

    [TestMethod]
    public async Task AKeyThatIsNotThere_IsAnInvalidArgument()
    {
        Assert.AreEqual("5", await ErrorOfAsync("Debug.Print c(\"nothing\")"));
        Assert.AreEqual("5", await ErrorOfAsync("c.Remove \"nothing\""));
    }

    [TestMethod]
    public async Task AKeyThatIsAlreadyInUse_IsError457_WhateverTheCase()
        => Assert.AreEqual("457", await ErrorOfAsync("c.Add 1, \"k\"", "c.Add 2, \"K\""));

    [TestMethod]
    public async Task AFailedAdd_AddsNothing()
        => CollectionAssert.AreEqual(new[] { "1" },
            await RunAsync("Dim c As New Collection", "c.Add 1, \"k\"", "On Error Resume Next", "c.Add 2, \"k\"", "Debug.Print c.Count"));

    [TestMethod]
    public async Task BothBeforeAndAfter_IsAnInvalidArgument()
        => Assert.AreEqual("5", await ErrorOfAsync("c.Add 1", "c.Add 2, , 1, 1"));

    [TestMethod]
    public async Task ABeforeOrAfterThatIsNotThere_IsAnError()
    {
        Assert.AreEqual("9", await ErrorOfAsync("c.Add 1", "c.Add 2, , 5"));
        Assert.AreEqual("5", await ErrorOfAsync("c.Add 1", "c.Add 2, , , \"nothing\""));
    }

    [TestMethod]
    public async Task AKeyThatIsNotAString_IsATypeMismatch()
        => Assert.AreEqual("13", await ErrorOfAsync("c.Add 1, 5"));

    [TestMethod]
    public async Task APositionThatIsNotANumberOrAString_IsATypeMismatch()
        => Assert.AreEqual("13", await ErrorOfAsync("c.Add 1", "Debug.Print c(True = False)"));
}
