using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>Me</c>, the <c>LBound</c>, <c>UBound</c> and <c>Array</c> special forms, and the dictionary access, bound (IR lowering S0.12a), evaluate to what
/// MS-VBAL 5.6.11, 3.3.5.2 and 5.6.14 say they do, and raise what they say they raise.
/// </summary>
[TestClass]
public sealed class BoundSpecialFormTests
{
    private static readonly IReadOnlyList<(string Name, string Source)> Classes =
    [
        ("Counter", ClassModule("Counter",
            "Private mValue As Long",
            "Public Sub Increment()", "    mValue = mValue + 1", "End Sub",
            "Public Property Get Value() As Long", "    Value = mValue", "End Property",
            "Public Function Self() As Counter", "    Set Self = Me", "End Function",
            "Public Function Doubled() As Long", "    Me.Increment", "    Doubled = 2 * Me.Value", "End Function")),
    ];

    private static Task<string[]> RunMainAsync(params string[] body)
        => RunAsync(Classes, $"Attribute VB_Name = \"Program\"\r\n{string.Join("\r\n", body)}\r\n");

    private static Task<string[]> RunMainBodyAsync(params string[] body)
        => RunMainAsync(["Public Sub Main()", .. body, "End Sub"]);

    // MS-VBAL 5.6.11: Me is the object the procedure was invoked on - a different one for each object, at the same expression.
    [TestMethod]
    public async Task Me_IsTheObjectTheProcedureWasInvokedOn()
        => CollectionAssert.AreEqual(new[] { "True", "False", "2", "4", "2" }, await RunMainBodyAsync(
            "Dim c As New Counter, d As New Counter",
            "Debug.Print c.Self Is c",
            "Debug.Print d.Self Is c",
            "Debug.Print c.Doubled",
            "Debug.Print c.Doubled",
            "Debug.Print d.Doubled"));

    [TestMethod]
    public async Task LBoundAndUBound_AreTheBoundsOfTheDimensionAsked()
        => CollectionAssert.AreEqual(new[] { "1", "3", "-2", "5", "9", "2" }, await RunMainBodyAsync(
            "Dim a(1 To 3, -2 To 5) As Long, d As Long, v As Variant",
            "Debug.Print LBound(a)", "Debug.Print UBound(a)",
            "d = 2",
            "Debug.Print LBound(a, d)", "Debug.Print UBound(a, d)",
            "On Error Resume Next",
            "Debug.Print UBound(a, 3)", "Debug.Print Err.Number",
            "Err.Clear",
            "v = Array(1, 2, 3)", "Debug.Print UBound(v)"));

    [TestMethod]
    public async Task UBoundOfAVariantThatHoldsNoArray_IsError13()
        => CollectionAssert.AreEqual(new[] { "13" }, await RunMainBodyAsync(
            "Dim v As Variant",
            "v = 5",
            "On Error Resume Next",
            "Debug.Print UBound(v)", "Debug.Print Err.Number"));

    // the Array special form's lower bound is the Option Base of the module it is written in; its elements are copies of what the arguments are.
    [TestMethod]
    public async Task Array_IsBasedOnTheOptionBaseOfItsModule_AndHoldsCopies()
        => CollectionAssert.AreEqual(new[] { "1", "3", "x", "2" }, await RunMainAsync(
            "Option Base 1",
            "Public Sub Main()",
            "    Dim v As Variant, s As String, n As Long",
            "    s = \"x\"", "    n = 2",
            "    v = Array(s, n, 3)",
            "    s = \"y\"", "    n = 7",
            "    Debug.Print LBound(v)", "    Debug.Print UBound(v)",
            "    Debug.Print v(1)", "    Debug.Print v(2)",
            "End Sub"));

    // MS-VBAL 5.6.14: c!key is the default member of the object, called with the name as a String - written on the object, or on the With block's target.
    [TestMethod]
    public async Task DictionaryAccess_CallsTheDefaultMemberWithTheName()
        => CollectionAssert.AreEqual(new[] { "one", "two", "5" }, await RunMainBodyAsync(
            "Dim c As New Collection",
            "c.Add \"one\", \"first\"", "c.Add \"two\", \"second\"",
            "Debug.Print c!first",
            "With c", "    Debug.Print !second", "End With",
            "On Error Resume Next",
            "Debug.Print c!third", "Debug.Print Err.Number"));
}
