using RDCore.SDK.Platform.Commands;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// Which argument of a verb is being written, as its descriptor has the forms of its arguments.
/// </summary>
[TestClass]
public sealed class VerbCompletionTests
{
    private static readonly VerbArgument Clear = new() { Name = "clear", Kind = VerbArgumentKind.Keyword, Values = [new() { Value = "CLEAR" }] };
    private static readonly VerbArgument Toggle = new() { Name = "toggle", Kind = VerbArgumentKind.Keyword, Values = [new() { Value = "TOGGLE" }] };
    private static readonly VerbArgument Line = new() { Name = "line", Kind = VerbArgumentKind.LineNumber };
    private static readonly VerbArgument Condition = new() { Name = "condition", Kind = VerbArgumentKind.Expression };

    private static readonly VerbDescriptor Break = new() { Name = "BREAK", Forms = [[Clear], [Toggle, Line], [Line, Condition]] };

    private static string[] Sites(string arguments) => [.. VerbCompletion.Read(Break, arguments).Select(site => $"{site.Argument.Name}:{site.Typed}")];

    [TestMethod]
    public void TheFirstArgument_IsTheFirstOfEveryForm()
        => CollectionAssert.AreEqual(new[] { "clear:", "toggle:", "line:" }, Sites(" "));

    [TestMethod]
    public void AWordBeingTyped_IsWhatOfTheArgumentIsTyped()
        => CollectionAssert.AreEqual(new[] { "clear:TO", "toggle:TO", "line:TO" }, Sites(" TO"));

    [TestMethod]
    public void AKeywordThatIsWritten_SaysWhichFormItIs()
    {
        CollectionAssert.AreEqual(new[] { "line:" }, Sites(" toggle "));
        CollectionAssert.AreEqual(new[] { "line:4" }, Sites(" TOGGLE 4"));
    }

    [TestMethod]
    public void AnArgumentThatTakesTheRest_IsEverythingAfterWhereItStarts()
    {
        CollectionAssert.AreEqual(new[] { "condition:" }, Sites(" 40 "));
        CollectionAssert.AreEqual(new[] { "condition:I > 5 And x." }, Sites(" 40 I > 5 And x."));
    }

    [TestMethod]
    public void AFormThatIsWrittenWhole_HasNothingMoreToIt()
    {
        Assert.AreEqual(0, Sites(" CLEAR ").Length);
        Assert.AreEqual(0, Sites(" TOGGLE 40 ").Length);
    }

    [TestMethod]
    public void AWordThatIsNoArgumentOfAnyForm_IsOfferedNothing()
        => Assert.AreEqual(0, Sites(" nonsense ").Length);

    [TestMethod]
    public void ADescriptor_TravelsWithTheRequest_AsItIs()
    {
        var request = new RDCore.SDK.Platform.Protocol.SessionCompletionParams
        {
            Source = "Public Sub Main()\r\nEnd Sub\r\n",
            ModuleName = "Program",
            Command = new RDCore.SDK.Platform.Protocol.SessionCommandCompletion { Verb = Break, Arguments = " 40 I." },
        };

        var received = RDCore.Tests.Platform.Wire.RoundTrip(request).Command!;

        Assert.AreEqual(" 40 I.", received.Arguments);
        CollectionAssert.AreEqual(new[] { "condition:I." }, VerbCompletion.Read(received.Verb, received.Arguments).Select(site => $"{site.Argument.Name}:{site.Typed}").ToArray());
        Assert.AreEqual("TOGGLE", received.Verb.Forms[1][0].Values[0].Value);
    }
}
