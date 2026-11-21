using RDCore.Diagnostics;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// The fix that makes a <c>Function</c> a <c>Sub</c>, of <c>RDC00217</c> and <c>RDC00206</c>: offered for a function no caller uses the value of, it
/// keeps what the program does.
/// </summary>
[TestClass]
public sealed class FunctionMadeASubFixTests
{
    // a program whose Main calls Add twice as a statement, and prints the total Add keeps.
    private static string[] Program(string header, string end, params string[] body)
        => ["Option Explicit", "Private mTotal As Long", "Public Sub Main()", "    Add 1", "    Call Add(2)", "    Debug.Print mTotal", "End Sub",
            header, "    mTotal = mTotal + n", .. body, end];

    [TestMethod]
    [DataRow("Private Function Add(ByVal n As Long) As Long", "Private Sub Add(ByVal n As Long)", DisplayName = "a Private function")]
    [DataRow("Public Function Add(ByVal n As Long) As Long", "Public Sub Add(ByVal n As Long)", DisplayName = "a Public function of a program")]
    [DataRow("Function Add(ByVal n As Long) As Long", "Sub Add(ByVal n As Long)", DisplayName = "no access modifier")]
    [DataRow("Private Static Function Add(ByVal n As Long) As Long", "Private Static Sub Add(ByVal n As Long)", DisplayName = "a Static function")]
    [DataRow("Private Function Add(ByVal n As Long)", "Private Sub Add(ByVal n As Long)", DisplayName = "no As clause")]
    public Task AFunctionEveryCallDiscards_IsMadeASub(string header, string made)
        => CodeActionFixture.AssertFixedAsync(RDCoreDiagnosticId.FunctionReturnValueAlwaysDiscarded,
            Program(header, "End Function", "    If n > 5 Then Exit Function", "    Add = mTotal", "    Add = 0"),
            Program(made, "End Sub", "    If n > 5 Then Exit Sub"), runs: true);

    [TestMethod]
    public Task AFunctionThatNeverAssignsItsReturnValue_AndThatEveryCallDiscards_IsMadeASub()
        => CodeActionFixture.AssertFixedAsync(RDCoreDiagnosticId.NotAllPathsReturnValue,
            Program("Private Function Add(ByVal n As Long) As Long", "End Function"), Program("Private Sub Add(ByVal n As Long)", "End Sub"),
            runs: true, choice: 1);

    [TestMethod]
    [DataRow("    Add = Len(CStr(n))", DisplayName = "a value that calls a function")]
    [DataRow("    Add = mTotal|    Debug.Print Add", DisplayName = "the return value read")]
    [DataRow("    If n > 0 Then Add = 1", DisplayName = "an assignment written in a line of another statement")]
    public Task AFunctionWhoseReturnValueIsUsedOtherwise_IsNotMadeASub(string body)
        => CodeActionFixture.AssertNoFixAsync(RDCoreDiagnosticId.FunctionReturnValueAlwaysDiscarded,
            Program("Private Function Add(ByVal n As Long) As Long", "End Function", body.Split('|')));

    [TestMethod]
    public Task AFunctionWrittenWithATypeCharacter_IsNotMadeASub()
        => CodeActionFixture.AssertNoFixAsync(RDCoreDiagnosticId.FunctionReturnValueAlwaysDiscarded,
            Program("Private Function Add&(ByVal n As Long)", "End Function", "    Add = 0"));

    // a caller uses what the function returns: the other fix, which assigns the default value, is the only one.
    [TestMethod]
    public async Task AFunctionWhoseValueACallerUses_IsNotMadeASub()
    {
        var found = (await CodeActionFixture.FindingsOfAsync(CodeActionFixture.Module(
                "Option Explicit", "Public Sub Main()", "    Debug.Print Add(1)", "End Sub",
                "Private Function Add(ByVal n As Long) As Long", "    Debug.Print n", "End Function")))
            .Where(each => each.Id == RDCoreDiagnosticId.NotAllPathsReturnValue).ToArray();

        Assert.IsNotEmpty(found, "the premise: RDC00206 is found");
        Assert.IsTrue(found.All(each => each.Fixes?.Edits.All(fix => fix.Edits.All(edit => edit.NewText != "Sub")) ?? true), "no fix writes Sub");
    }
}
