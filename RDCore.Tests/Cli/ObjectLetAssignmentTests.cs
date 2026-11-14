using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Let</c> assignment, with no <c>Set</c>, of an object to a property whose <c>Property Let</c> takes an object (<strong>MS-VBAL §5.4.3.8</strong>): the
/// <c>Property Let</c> is given the object itself, whether its parameter is <c>Object</c>, a class or a <c>Variant</c>, early-bound or late-bound. A parameter
/// of a type that is not an object type is given the value of the object's default member, as <strong>MS-VBAL §5.5.1.2.13</strong> has it. Each row is what MS-VBA does
/// (recorded in Excel, 64-bit).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.8 Let Statement")]
public sealed class ObjectLetAssignmentTests
{
    private static readonly (string Name, string Source)[] Classes =
    [
        ("Defaulted", ClassModule("Defaulted", "Option Explicit",
            "Public Property Get Value() As Long", "Attribute Value.VB_UserMemId = 0", "    Value = 42", "End Property")),
        ("Plain", ClassModule("Plain", "Option Explicit", "Public X As Long")),
        ("Holder", ClassModule("Holder", "Option Explicit",
            "Private mo As Object",
            "Public Got As String",
            "Public Property Let PObj(ByVal v As Object)", "    Got = \"PObj \" & TypeName(v): Set mo = v", "End Property",
            "Public Property Get PObj() As Object", "    Set PObj = mo", "End Property",
            "Public Property Let PCol(ByVal v As Collection)", "    Got = \"PCol \" & TypeName(v)", "End Property",
            "Public Property Let PDef(ByVal v As Defaulted)", "    Got = \"PDef \" & TypeName(v)", "End Property",
            "Public Property Let PVar(ByVal v As Variant)", "    Got = \"PVar \" & TypeName(v) & \" \" & IsObject(v)", "End Property",
            "Public Property Let PRef(v As Object)", "    Got = \"PRef \" & TypeName(v)", "End Property",
            "Public Property Let PBoth(ByVal v As Object)", "    Got = \"PBoth let \" & TypeName(v)", "End Property",
            "Public Property Set PBoth(ByVal v As Object)", "    Got = \"PBoth set \" & TypeName(v)", "End Property",
            "Public Property Let PLong(ByVal v As Long)", "    Got = \"PLong \" & v", "End Property")),
    ];

    // h is a Holder and o the same object late-bound; d is a Defaulted (its default member is 42), pl a Plain (it has none), col a Collection, vd a Variant
    // that holds d, vn a Variant. What the row's statement raises, what the Property Let it reaches is given, and whether PObj holds d after.
    private static string Program(string statement)
        => "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n" +
           "    Dim h As New Holder, o As Object, d As New Defaulted, pl As New Plain, col As New Collection, vd As Variant, vn As Variant\r\n" +
           "    Set o = h: Set vd = d\r\n" +
           "    On Error Resume Next\r\n" +
           $"    {statement}\r\n" +
           "    Debug.Print Err.Number & \" got \" & h.Got & \" is \" & (h.PObj Is d)\r\n" +
           "End Sub\r\n";

    [TestMethod]
    [DataRow("h.PObj = d", "0 got PObj Defaulted is True", DisplayName = "Object parameter, an object with a default member")]
    [DataRow("h.PObj = pl", "0 got PObj Plain is False", DisplayName = "Object parameter, an object with no default member")]
    [DataRow("h.PObj = col", "0 got PObj Collection is False", DisplayName = "Object parameter, a Collection")]
    [DataRow("h.PObj = Nothing", "0 got PObj Nothing is False", DisplayName = "Object parameter, Nothing")]
    [DataRow("h.PCol = col", "0 got PCol Collection is False", DisplayName = "Collection parameter")]
    [DataRow("h.PDef = d", "0 got PDef Defaulted is False", DisplayName = "class parameter, the class's object")]
    [DataRow("h.PVar = d", "0 got PVar Defaulted True is False", DisplayName = "Variant parameter, an object with a default member")]
    [DataRow("h.PVar = pl", "0 got PVar Plain True is False", DisplayName = "Variant parameter, an object with no default member")]
    [DataRow("h.PRef = d", "0 got PRef Defaulted is False", DisplayName = "ByRef Object parameter")]
    [DataRow("h.PBoth = d", "0 got PBoth let Defaulted is False", DisplayName = "a Property Let and a Property Set, no Set")]
    [DataRow("Set h.PBoth = d", "0 got PBoth set Defaulted is False", DisplayName = "a Property Let and a Property Set, Set")]
    [DataRow("h.PObj = vd", "0 got PObj Defaulted is True", DisplayName = "Object parameter, a Variant that holds the object")]
    [DataRow("Let h.PObj = d", "0 got PObj Defaulted is True", DisplayName = "Object parameter, the Let keyword")]
    [DataRow("h.PLong = d", "0 got PLong 42 is False", DisplayName = "Long parameter, the default member")]
    [DataRow("h.PLong = pl", "438 got  is False", DisplayName = "Long parameter, no default member")]
    [DataRow("o.PObj = d", "0 got PObj Defaulted is True", DisplayName = "late-bound, Object parameter, an object with a default member")]
    [DataRow("o.PObj = pl", "0 got PObj Plain is False", DisplayName = "late-bound, Object parameter, an object with no default member")]
    [DataRow("o.PObj = col", "0 got PObj Collection is False", DisplayName = "late-bound, Object parameter, a Collection")]
    [DataRow("o.PObj = Nothing", "0 got PObj Nothing is False", DisplayName = "late-bound, Object parameter, Nothing")]
    [DataRow("o.PObj = 5", "13 got  is False", DisplayName = "late-bound, Object parameter, a number")]
    [DataRow("o.PVar = d", "0 got PVar Defaulted True is False", DisplayName = "late-bound, Variant parameter")]
    [DataRow("o.PLong = d", "0 got PLong 42 is False", DisplayName = "late-bound, Long parameter")]
    [DataRow("o.PBoth = d", "0 got PBoth let Defaulted is False", DisplayName = "late-bound, a Property Let and a Property Set, no Set")]
    [DataRow("o.PObj = vd", "0 got PObj Defaulted is True", DisplayName = "late-bound, Object parameter, a Variant that holds the object")]
    [DataRow("o.PCol = col", "0 got PCol Collection is False", DisplayName = "late-bound, Collection parameter")]
    [DataRow("Set vn = Nothing: h.PObj = vn", "0 got PObj Nothing is False", DisplayName = "Object parameter, a Variant that holds Nothing")]
    [DataRow("vn = 5: h.PObj = vn", "424 got  is False", DisplayName = "Object parameter, a Variant that holds a number")]
    [DataRow("h.PCol = d", "13 got  is False", DisplayName = "Collection parameter, an object of another class")]
    [DataRow("o.PCol = d", "13 got  is False", DisplayName = "late-bound, Collection parameter, an object of another class")]
    public async Task ALetOfAnObject_GivesThePropertyLetTheObject(string statement, string expected)
    {
        foreach (var engine in new[] { VBExecutionEngine.Interpreter, VBExecutionEngine.Compiled })
        {
            var output = await RunAsync(Classes, Program(statement), engine);
            Assert.AreEqual(expected, string.Join(" | ", output), engine.ToString());
        }
    }

    // a number is no object: the Let-coercion of it to Object is invalid (MS-VBAL §5.5.1.1), as MS-VBA has it.
    [TestMethod]
    public async Task ALetOfANumber_ToAnObjectParameter_IsATypeMismatch()
    {
        var errors = await LoadErrorsAsync(Classes, Program("h.PObj = 5"));

        Assert.HasCount(1, errors, string.Join(" | ", errors));
        StringAssert.StartsWith(errors[0], "Type mismatch");
    }
}
