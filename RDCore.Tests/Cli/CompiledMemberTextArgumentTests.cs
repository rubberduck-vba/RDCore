using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>String</c> of variable length given to a member of an object of a class of the workspace (MS-VBAL §5.3.1.11): its text, given by value, or the
/// String variable itself, given by reference, as the interpreter gives it to a procedure of a module. Compiled code calls the member on the object, its
/// default member, a <c>Property Let</c>, a member of <c>Me</c> named bare, and the member implementing one of an interface, as the interpreter calls it.
/// A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledMemberTextArgumentTests
{
    // a class that keeps the text it is given, whose Item, its default member, counts it; an interface and a class that implements it.
    private static readonly (string Name, string Source)[] Classes =
    [
        ("Tagger", ClassModule("Tagger",
            "Option Explicit",
            "Private m As String",
            "Public Function Tagged(ByVal S As String) As Boolean",
            "    m = m & S",
            "    Tagged = Len(m) > 2",
            "End Function",
            "Public Sub Append(S As String)",
            "    S = S & \"!\"",
            "End Sub",
            "Public Property Get Item(ByVal K As String) As Long",
            "Attribute Item.VB_UserMemId = 0",
            "    Item = Len(m) + Len(K)",
            "End Property",
            "Public Property Let Label(ByVal S As String)",
            "    m = S",
            "End Property",
            "Public Function Echo(S As String) As String",
            "    S = S & \"!\"",
            "    Echo = S",
            "End Function",
            "Public Function Twice(ByVal S As String) As Long",
            "    Twice = Size(S) * 2",
            "End Function",
            "Private Function Size(ByVal S As String) As Long",
            "    Size = Len(S)",
            "End Function")),
        ("Named", ClassModule("Named",
            "Option Explicit",
            "Public Function Greet(ByVal S As String) As Long",
            "End Function")),
        ("Greeter", ClassModule("Greeter",
            "Option Explicit",
            "Implements Named",
            "Private Function Named_Greet(ByVal S As String) As Long",
            "    Named_Greet = Len(S) * 10",
            "End Function")),
    ];

    // the program run both ways: the procedure compiled, with no statement stepped or made a support call.
    private static async Task AssertNoneSteppedAsync(string procedure, string expected, params string[] lines)
    {
        var compiled = await AssertAlikeAsync(Program(lines), Classes, expected);
        Assert.IsTrue(compiled.TryGetValue(procedure, out var result), $"{procedure} was not called");
        Assert.IsNotNull(result.Body, $"{procedure}: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
        Assert.IsEmpty(result.Stepped, $"{procedure} steps: {string.Join("; ", result.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
        Assert.IsEmpty(result.SupportCalls,
            $"{procedure} support calls: {string.Join("; ", result.SupportCalls.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
    }

    // a literal and a String variable, each given by value.
    [TestMethod]
    public async Task AStringGivenByValue_IsGivenItsText()
        => await AssertNoneSteppedAsync("Counted", " 2 ",
            "Public Sub Main()",
            "    Debug.Print Counted()",
            "End Sub",
            "Private Function Counted() As Long",
            "    Dim T As Tagger, S As String, I As Long",
            "    Set T = New Tagger",
            "    S = \"a\"",
            "    For I = 1 To 2",
            "        If T.Tagged(S) Then Counted = Counted + 1",
            "        If T.Tagged(\"b\") Then Counted = Counted + 1",
            "    Next",
            "End Function");

    // a String variable given by reference, which the member assigns.
    [TestMethod]
    public async Task AStringVariableGivenByReference_IsAssignedByTheMember()
        => await AssertNoneSteppedAsync("Bangs", " 3 ",
            "Public Sub Main()",
            "    Debug.Print Bangs()",
            "End Sub",
            "Private Function Bangs() As Long",
            "    Dim T As Tagger, S As String, I As Long",
            "    Set T = New Tagger",
            "    For I = 1 To 3",
            "        T.Append S",
            "    Next",
            "    Bangs = Len(S)",
            "End Function");

    // a String variable given by reference to a call inside an expression that reads it too: the program runs the same either way.
    [TestMethod]
    public async Task AStringVariableGivenByReferenceInsideAnExpressionThatReadsIt_IsReadAsTheInterpreterReadsIt()
    {
        var compiled = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Echoed()",
            "End Sub",
            "Private Function Echoed() As Long",
            "    Dim T As Tagger, S As String, I As Long",
            "    Set T = New Tagger",
            "    S = \"a\"",
            "    For I = 1 To 2",
            "        If S = T.Echo(S) Then Echoed = Echoed + 1",
            "        If T.Echo(S) = S Then Echoed = Echoed + 10",
            "    Next",
            "End Function"), Classes, null);
        Assert.IsTrue(compiled.ContainsKey("Echoed"), "Echoed was not called");
    }

    // the default member, given a String by an index expression (MS-VBAL §5.6.13).
    [TestMethod]
    public async Task TheDefaultMember_IsGivenItsText()
        => await AssertNoneSteppedAsync("Indexed", " 4 ",
            "Public Sub Main()",
            "    Debug.Print Indexed()",
            "End Sub",
            "Private Function Indexed() As Long",
            "    Dim T As Tagger",
            "    Set T = New Tagger",
            "    If T.Tagged(\"ab\") Then Indexed = 0",
            "    Indexed = T(\"xy\")",
            "End Function");

    // a Property Let, given a String as the value assigned (MS-VBAL §5.4.3.8).
    [TestMethod]
    public async Task APropertyLet_IsGivenItsText()
        => await AssertNoneSteppedAsync("Labelled", " 1 ",
            "Public Sub Main()",
            "    Debug.Print Labelled()",
            "End Sub",
            "Private Function Labelled() As Long",
            "    Dim T As Tagger",
            "    Set T = New Tagger",
            "    T.Label = \"abc\"",
            "    If T.Tagged(\"d\") Then Labelled = 1",
            "End Function");

    // a member of Me, named bare inside the class.
    [TestMethod]
    public async Task AMemberOfMeNamedBare_IsGivenItsText()
        => await AssertNoneSteppedAsync("Twice", " 6 ",
            "Public Sub Main()",
            "    Dim T As Tagger",
            "    Set T = New Tagger",
            "    Debug.Print T.Twice(\"abc\")",
            "End Sub");

    // the member of a class that implements the member of the interface the object is declared as (MS-VBAL §5.3.1.9).
    [TestMethod]
    public async Task TheMemberImplementingOneOfAnInterface_IsGivenItsText()
        => await AssertNoneSteppedAsync("Greeted", " 30 ",
            "Public Sub Main()",
            "    Debug.Print Greeted()",
            "End Sub",
            "Private Function Greeted() As Long",
            "    Dim N As Named",
            "    Set N = New Greeter",
            "    Greeted = N.Greet(\"abc\")",
            "End Function");
}
