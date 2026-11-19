using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A Let whose value is an object, to a property: MS-VBA gives a <c>Variant</c> value parameter the object itself, early bound or late bound, and only a
/// parameter of another type has the object's default member read for it (RD-VBAL §5.4.3.8, observed in Excel). A Let to a variable reads it, as
/// MS-VBAL §5.4.3.8 says.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.8 Let Statement")]
public sealed class PropertyLetObjectValueTests
{
    // what a Property Let was given: its type, and whether it is an object.
    private static readonly string Holder = ClassModule("Holder",
        "Option Explicit",
        "Public Got As String",
        "Public Field As Variant",
        "Public Property Let Item(ByVal V As Variant)",
        "    Got = TypeName(V) & \" \" & IsObject(V)",
        "End Property",
        "Public Property Get Item() As Variant",
        "    Item = Got",
        "End Property",
        "Public Property Let Number(ByVal V As Long)",
        "    Got = \"Long \" & V",
        "End Property",
        "Public Property Get Number() As Long",
        "    Number = 0",
        "End Property");

    // a class with no default member.
    private static readonly string Thing = ClassModule("Thing",
        "Option Explicit",
        "Public Name As String");

    // a class whose default member is 42.
    private static readonly string Defaulted = ClassModule("Defaulted",
        "Option Explicit",
        "Public Property Get Value() As Long",
        "Attribute Value.VB_UserMemId = 0",
        "    Value = 42",
        "End Property");

    [TestMethod]
    [DataRow("Dim H As Object: Set H = New Holder ;; H.Item = New Thing ;; Debug.Print H.Got", "Thing True", DisplayName = "late bound, an object with no default member")]
    [DataRow("Dim H As Object: Set H = New Holder ;; H.Item = New Defaulted ;; Debug.Print H.Got", "Defaulted True", DisplayName = "late bound, an object with a default member")]
    [DataRow("Dim H As Object: Set H = New Holder ;; H.Item = Nothing ;; Debug.Print H.Got", "Nothing True", DisplayName = "late bound, Nothing")]
    [DataRow("Dim H As Object: Set H = New Holder ;; Dim V As Variant: Set V = New Defaulted ;; H.Item = V ;; Debug.Print H.Got", "Defaulted True",
        DisplayName = "late bound, an object a Variant holds")]
    [DataRow("Dim H As Object: Set H = New Holder ;; H.Item = 5 ;; Debug.Print H.Got", "Integer False", DisplayName = "late bound, a number")]
    [DataRow("Dim H As Holder: Set H = New Holder ;; H.Item = New Defaulted ;; Debug.Print H.Got", "Defaulted True", DisplayName = "early bound, an object with a default member")]
    [DataRow("Dim H As Holder: Set H = New Holder ;; H.Item = New Thing ;; Debug.Print H.Got", "Thing True", DisplayName = "early bound, an object with no default member")]
    [DataRow("Dim H As Holder: Set H = New Holder ;; H.Item = Nothing ;; Debug.Print H.Got", "Nothing True", DisplayName = "early bound, Nothing")]
    [DataRow("Dim H As Holder: Set H = New Holder ;; H.Item = 5 ;; Debug.Print H.Got", "Integer False", DisplayName = "early bound, a number")]
    public async Task AnObjectLetToAVariantProperty_IsTheObject(string body, string expected)
        => CollectionAssert.AreEqual(new[] { expected }, await Run(body));

    [TestMethod]
    [DataRow("Dim H As Object: Set H = New Holder ;; H.Number = New Defaulted ;; Debug.Print H.Got", "Long 42", DisplayName = "late bound, a Long property")]
    [DataRow("Dim H As Object: Set H = New Holder ;; H.Number = New Thing ;; Debug.Print H.Got", "error 13", DisplayName = "late bound, a Long property, no default member")]
    [DataRow("Dim V As Variant ;; V = New Defaulted ;; Debug.Print TypeName(V) & \" \" & IsObject(V)", "Long False", DisplayName = "a Variant variable")]
    [DataRow("Dim V As Variant ;; Dim T As Thing: Set T = New Thing ;; V = T ;; Debug.Print TypeName(V) & \" \" & IsObject(V)", "error 438",
        DisplayName = "a Variant variable, no default member")]
    [DataRow("Dim H As Holder: Set H = New Holder ;; H.Number = New Defaulted ;; Debug.Print H.Got", "Long 42", DisplayName = "early bound, a Long property")]
    [DataRow("Dim H As Holder: Set H = New Holder ;; H.Number = New Thing ;; Debug.Print H.Got", "error 438", DisplayName = "early bound, a Long property, no default member")]
    [DataRow("Dim H As Holder: Set H = New Holder ;; H.Field = New Defaulted ;; Debug.Print TypeName(H.Field) & \" \" & IsObject(H.Field)", "Long False",
        DisplayName = "early bound, a public Variant variable")]
    [DataRow("Dim H As Object: Set H = New Holder ;; H.Field = New Defaulted ;; Debug.Print TypeName(H.Field) & \" \" & IsObject(H.Field)", "Long False",
        DisplayName = "late bound, a public Variant variable")]
    public async Task AnObjectLetToAnythingElse_IsItsDefaultMember(string body, string expected)
        => CollectionAssert.AreEqual(new[] { expected }, await Run(body));

    private static Task<string[]> Run(string body)
        => RunAsync([("Holder", Holder), ("Thing", Thing), ("Defaulted", Defaulted)], StandardModule("Program",
            "Option Explicit",
            "Public Sub Main()",
            "    On Error GoTo Failed",
            string.Join("\r\n", body.Split(" ;; ").Select(line => "    " + line)),
            "    Exit Sub",
            "Failed:",
            "    Debug.Print \"error \" & Err.Number",
            "End Sub"));
}
