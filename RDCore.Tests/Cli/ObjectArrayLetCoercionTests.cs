using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>RD-VBAL §5.5.1.2.7</strong>: an array of objects let-coerced to an array of objects of another class, or of <c>Object</c>, is copied as MS-VBA
/// copies it, a shallow copy that checks no element's class, where MS-VBAL says only an array of the same element type is; an array of Variants or of
/// numbers is still a type mismatch (observed in Excel).
/// </summary>
[TestClass]
public sealed class ObjectArrayLetCoercionTests
{
    private static readonly (string Name, string Source)[] Box = [("Box", ClassModule("Box", "Option Explicit", "Public Field As Long"))];

    private static async Task<string> Run(string body)
        => (await RunAsync(Box, "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPrivate acc As String\r\n"
            + "Private Sub P(ByVal v As Variant)\r\n    acc = acc & CStr(v) & \"|\"\r\nEnd Sub\r\n"
            + "Private Function AsBoxes(ByVal v As Variant) As Box()\r\n    AsBoxes = v\r\nEnd Function\r\n"
            + $"Public Sub Main()\r\nOn Error Resume Next\r\n{string.Join("\r\n", body.Split(" ;; "))}\r\nP \"e\" & Err.Number\r\nDebug.Print acc\r\nEnd Sub\r\n")).Last();

    [TestMethod]
    [DataRow("Dim d(1) As Object ;; Set d(0) = New Collection ;; Dim v As Variant ;; v = d ;; Dim bx() As Box ;; bx = AsBoxes(v) ;; P UBound(bx) ;; P TypeName(bx(0))",
        "1|Collection|e0|", DisplayName = "A06 Object() into Box(), a Collection in it")]
    [DataRow("Dim d(1) As Object ;; Set d(0) = New Collection ;; Dim v As Variant ;; v = d ;; Dim w() As Collection ;; w = v ;; P TypeName(w(0))",
        "Collection|e0|", DisplayName = "A06 Object() into Collection()")]
    [DataRow("Dim d(1) As Object ;; Set d(0) = New Box ;; Dim v As Variant ;; v = d ;; Dim bx() As Box ;; bx = AsBoxes(v) ;; P TypeName(bx(0))",
        "Box|e0|", DisplayName = "A06 Object() into Box(), a Box in it")]
    [DataRow("Dim c(1) As Collection ;; Set c(0) = New Collection ;; Dim v As Variant ;; v = c ;; Dim o() As Object ;; o = v ;; Set o(1) = New Box ;; P TypeName(c(1)) ;; P TypeName(o(0)) ;; P (o(0) Is c(0))",
        "Nothing|Collection|True|e0|", DisplayName = "A07 Collection() into Object(), shallow")]
    [DataRow("Dim c(1) As Collection ;; Set c(0) = New Collection ;; Dim v As Variant ;; v = c ;; Dim bx() As Box ;; bx = AsBoxes(v) ;; P UBound(bx) ;; P TypeName(bx(0))",
        "1|Collection|e0|", DisplayName = "A09 Collection() into Box()")]
    [DataRow("Dim b(1) As Box ;; Dim v As Variant ;; v = b ;; Dim w() As Collection ;; w = v ;; P UBound(w)",
        "1|e0|", DisplayName = "A09 Box() into Collection()")]
    public async Task AnArrayOfObjects_IsCopiedIntoAnArrayOfObjectsOfAnotherType(string body, string printed)
        => Assert.AreEqual(printed, await Run(body));

    [TestMethod]
    [DataRow("Dim c(1) As Collection ;; Dim v As Variant ;; v = c ;; Dim vv() As Variant ;; vv = v", DisplayName = "A07 Collection() into Variant()")]
    [DataRow("Dim n() As Long ;; ReDim n(1) ;; Dim v As Variant ;; v = n ;; Dim o() As Object ;; o = v", DisplayName = "A08 Long() into Object()")]
    [DataRow("Dim v As Variant ;; v = Array(New Collection) ;; Dim o() As Object ;; o = v", DisplayName = "A08 Variant() into Object()")]
    [DataRow("Dim n(1) As Long ;; Dim v As Variant ;; v = n ;; Dim bx() As Box ;; bx = AsBoxes(v)", DisplayName = "A03 Long() into Box()")]
    public async Task AnArrayOfAnythingElse_IsError13(string body)
        => Assert.AreEqual("e13|", await Run(body));
}
