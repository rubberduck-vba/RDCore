using RDCore.SDK.Model.Symbols.Completion;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// The call being written is read off the line before the cursor: the innermost argument list open after a name, or a call statement's own arguments - and the
/// argument the cursor is in, by the commas before it.
/// </summary>
[TestClass]
public sealed class SignatureSiteTests
{
    private static string? Read(string text) => SignatureSite.Read(text) is { } site
        ? $"{site.Callee}|{site.ArgumentIndex}|{site.ArgumentName}|{(site.IsMemberAccess ? site.Qualifier + "." : string.Empty)}{site.Name}"
        : null;

    [TestMethod]
    [DataRow("x = Left(", "Left|0||Left", DisplayName = "an argument list that opens")]
    [DataRow("x = Left(s, ", "Left|1||Left", DisplayName = "after a comma")]
    [DataRow("x = Left$(s, ", "Left$|1||Left$", DisplayName = "a type hint")]
    [DataRow("x = Left(Mid(s, 2), ", "Left|1||Left", DisplayName = "an argument list that is closed")]
    [DataRow("x = Left(Mid(s, ", "Mid|1||Mid", DisplayName = "the innermost")]
    [DataRow("x = Left((a + b, ", "Left|0||Left", DisplayName = "the parentheses of an expression are no call")]
    [DataRow("x = Left(\"a, b\", ", "Left|1||Left", DisplayName = "commas of a string literal")]
    [DataRow("x = o.Item(", "o.Item|0||o.Item", DisplayName = "a member")]
    [DataRow("x = w.Grow(2).Parts.Add(", "w.Grow(2).Parts.Add|0||w.Grow(2).Parts.Add", DisplayName = "a member of a call")]
    [DataRow("    .Add(", ".Add|0||.Add", DisplayName = "a member of a With block's object")]
    public void AnArgumentList_IsTheCallOfTheNameBeforeIt(string text, string expected)
        => Assert.AreEqual(expected, Read(text));

    [TestMethod]
    [DataRow("MsgBox ", "MsgBox|0||MsgBox", DisplayName = "the space after the name")]
    [DataRow("MsgBox \"a, b\", ", "MsgBox|1||MsgBox", DisplayName = "after a comma")]
    [DataRow("10 MsgBox \"a\", ", "MsgBox|1||MsgBox", DisplayName = "after a line number")]
    [DataRow("x = 1: Debug.Print \"a\", ", "Debug.Print|1||Debug.Print", DisplayName = "after a statement separator")]
    [DataRow("If x Then MsgBox \"a\", ", "MsgBox|1||MsgBox", DisplayName = "after Then")]
    [DataRow("MsgBox \"a\", Title:=", "MsgBox|1|Title|MsgBox", DisplayName = "a named argument")]
    [DataRow("MsgBox Title:=", "MsgBox|0|Title|MsgBox", DisplayName = "a named argument, first")]
    public void ACallStatement_TakesItsArgumentsAfterASpace(string text, string expected)
        => Assert.AreEqual(expected, Read(text));

    [TestMethod]
    [DataRow("x = 1", DisplayName = "an assignment")]
    [DataRow("x = ", DisplayName = "an assignment, with a space")]
    [DataRow("MsgBox", DisplayName = "a name with no space after it")]
    [DataRow("MsgBox \"a, (", DisplayName = "a string literal")]
    [DataRow("MsgBox 1 ' Left(", DisplayName = "a comment")]
    [DataRow("x = (", DisplayName = "parentheses after no name")]
    public void WhereNoCallIsWritten_ThereIsNone(string text)
        => Assert.IsNull(Read(text));
}
