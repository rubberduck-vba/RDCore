namespace RDCore.Tests.Cli;

/// <summary>
/// A class whose members return an object of the class itself, called one after the other on what each returns - a builder, as MVVM's
/// <c>StringBuilderNet.Append(x).Insert(i, c)</c> and <c>ValidationErrorFormatter.WithErrorBorderColor.WithErrorBackgroundColor</c> are (<strong>MS-VBAL
/// §5.6.13</strong>): each is a call on the object the one before it returned, not an index of it.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.13 Index Expressions")]
public sealed class SelfReturningMemberTests
{
    private static readonly (string Name, string Source) Builder = ("Builder", ModuleWorkspace.ClassModule("Builder",
        "Attribute VB_PredeclaredId = True",
        "Option Explicit",
        "Private Text As String",
        "Public Function Append(ByRef Value As String, Optional ByVal Count As Long = 0) As Builder",
        "    Dim result As Builder",
        "    Set result = New Builder",
        "    result.SetText Text & Value",
        "    Set Append = result",
        "End Function",
        "Public Function Shout(Optional ByVal Times As Long = 1) As Builder",
        "    Set Shout = Append(\"!\")",
        "End Function",
        "Public Sub SetText(ByVal Value As String)",
        "    Text = Value",
        "End Sub",
        "Public Function ToString() As String",
        "    ToString = Text",
        "End Function"));

    private static string Program(params string[] body)
        => ModuleWorkspace.StandardModule("Program", ["Option Explicit", "Public Sub Main()", .. body, "End Sub"]);

    [TestMethod]
    [DataRow(new[] { "Debug.Print Builder.Append(\"a\").Append(\"b\").ToString" }, "ab", DisplayName = "a call on what a call returned")]
    [DataRow(new[] { "Debug.Print Builder.Append(\"a\").Shout.Shout.ToString" }, "a!!", DisplayName = "and members called without parentheses")]
    [DataRow(new[] { "With Builder.Append(\"a\") _", "    .Append(\"b\")", "    Debug.Print .ToString", "End With" }, "ab", DisplayName = "as the target of With")]
    [DataRow(new[] { "With Builder.Append(\"a\")", "    With .Append(\"b\")", "        Debug.Print .ToString", "    End With", "End With" }, "ab",
        DisplayName = "a call on the With block's target, as the target of the next")]
    public async Task ACallOnWhatAMemberOfTheClassReturned_IsACall(string[] body, string printed)
        => CollectionAssert.AreEqual(new[] { printed }, await ModuleWorkspace.RunAsync([Builder], Program(body)));
}
