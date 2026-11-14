using RDCore.LanguageServer.Services.Refactoring.ExtractMethod;
using RDCore.SDK.Model.Symbols.Abstract;

namespace RDCore.Tests.Semantics.Refactoring;

/// <summary>
/// The duplicates of a selection: code written as it is elsewhere, which a call to the new procedure replaces.
/// </summary>
[TestClass]
public sealed class ExtractionDuplicatesTests
{
    // lines 1 and 2 are extracted: x is the new procedure's own, n its parameter.
    private const string Body = "Dim x As Long\nx = n * 2\nDebug.Print x";

    // a standard module beside Main, whose Twin does what lines 1 and 2 do.
    private static readonly (string Name, string Source)[] OtherTwin =
        [("Other", "Attribute VB_Name = \"Other\"\r\nOption Explicit\r\n\r\nPublic Sub Twin(ByVal m As Long)\r\n    Dim y As Long\r\n    y = m * 2\r\n    Debug.Print y\r\nEnd Sub\r\n")];

    // the end of Run, then a procedure Twin of the module, whose body is its declaration and the code.
    private static string Twin(string code, string signature = "ByVal m As Long", string declaration = "Dim y As Long")
        => $"End Sub\n\nPrivate Sub Twin({signature})\n    {declaration}\n{string.Join("\n", code.Split('\n').Select(line => "    " + line))}\nEnd Sub";

    private static string NameOf(SemanticId variable) => variable.Uri.Fragment[(variable.Uri.Fragment.LastIndexOf('.') + 1)..];

    [TestMethod]
    public void TheSameCodeElsewhere_IsADuplicate_WhoseVariablesStandForTheSelections()
    {
        var (duplicates, _, _, analyzed) = ExtractionFixture.Duplicates(Body, 1, 2, end: Twin("y = m * 2\nDebug.Print y"));

        var duplicate = duplicates.Single();
        var line = analyzed.Text.Split("\r\n").ToList().IndexOf("    y = m * 2") + 1;
        Assert.AreEqual($"Main:{line}:5", duplicate.Id);
        CollectionAssert.AreEquivalent(new[] { "n m", "x y" }, duplicate.Variables.Select(pair => $"{NameOf(pair.Key)} {NameOf(pair.Value)}").ToArray());
    }

    // spaces, a line continuation and a comment are not what the code does.
    [TestMethod]
    public void TheSameCodeWrittenOtherwise_IsADuplicate()
        => Assert.HasCount(1, ExtractionFixture.Duplicates(Body, 1, 2, end: Twin("y = m _\n    *   2 ' twice\nDebug.Print y")).Duplicates);

    [TestMethod]
    [DataRow("y = m * 3\nDebug.Print y", "ByVal m As Long", "Dim y As Long", DisplayName = "another literal")]
    [DataRow("y = m + 2\nDebug.Print y", "ByVal m As Long", "Dim y As Long", DisplayName = "another operator")]
    [DataRow("y = field * 2\nDebug.Print y", "ByVal m As Long", "Dim y As Long", DisplayName = "a module variable where a parameter was")]
    [DataRow("y = m * 2\nDebug.Print m", "ByVal m As Long", "Dim y As Long", DisplayName = "two variables where one was")]
    [DataRow("y = m * 2\nDebug.Print y", "ByVal m As Integer", "Dim y As Long", DisplayName = "a parameter declared otherwise")]
    [DataRow("y = m * 2\nDebug.Print y", "ByVal m As Long", "Dim y As Double", DisplayName = "a variable declared otherwise")]
    [DataRow("y = m * 2\nDebug.Print y\nDebug.Print y", "ByVal m As Long", "Dim y As Long", DisplayName = "a value read after it, which the call would not give back")]
    public void OtherCode_IsNoDuplicate(string code, string signature, string declaration)
        => Assert.IsEmpty(ExtractionFixture.Duplicates(Body, 1, 2, end: Twin(code, signature, declaration)).Duplicates);

    [TestMethod]
    public void ADuplicateThatLeavesAsTheSelectionDoes_IsADuplicate()
    {
        const string body = "If n > 0 Then Exit Sub\nDebug.Print n";

        Assert.HasCount(1, ExtractionFixture.Duplicates(body, 0, 0, end: "End Sub\n\nPrivate Sub Twin(ByVal m As Long)\n    If m > 0 Then Exit Sub\n    Debug.Print m\nEnd Sub").Duplicates);
        Assert.IsEmpty(
            ExtractionFixture.Duplicates(body, 0, 0, end: "End Sub\n\nPrivate Function Twin(ByVal m As Long) As Long\n    If m > 0 Then Exit Function\n    Debug.Print m\nEnd Function").Duplicates,
            "it leaves a Function");
    }

    [TestMethod]
    public void ADuplicateInAnotherModule_IsFoundWhereTheSearchGoes()
    {
        Assert.IsEmpty(ExtractionFixture.Duplicates(Body, 1, 2, modules: OtherTwin).Duplicates, "the module's alone");
        Assert.AreEqual("Other", ExtractionFixture.Duplicates(Body, 1, 2, modules: OtherTwin, searched: ["Main", "Other"]).Duplicates.Single().Id.Split(':')[0]);
    }

    // a call to Extracted written in Other would mean Other's own.
    [TestMethod]
    public void ADuplicateInAModuleThatDeclaresTheName_IsNoDuplicate()
    {
        (string, string)[] declaring = [("Other", OtherTwin[0].Source + "\r\nPrivate Sub Extracted()\r\nEnd Sub\r\n")];

        Assert.IsEmpty(ExtractionFixture.Duplicates(Body, 1, 2, modules: declaring, searched: ["Main", "Other"]).Duplicates);
    }

    [TestMethod]
    public void ADuplicate_IsReplacedByACallPassingItsOwnVariables_AndTheDeclarationThatMovesGoes()
    {
        var (duplicates, plan, choices, analyzed) = ExtractionFixture.Duplicates(Body, 1, 2, end: Twin("y = m * 2\nDebug.Print y"));

        var replaced = ExtractionText.Apply(analyzed.Text, duplicates.Single().Replacement(plan, choices)).Text;

        StringAssert.Contains(replaced, "Private Sub Twin(ByVal m As Long)\r\n    Extracted m\r\nEnd Sub");
    }
}
