using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>ObjPtr</c>: the address of an interface of an object in the session's memory, as <strong>MS-VBA</strong> gives it (recorded in Excel, oracle\s17 O
/// rows) - <c>0</c> for <c>Nothing</c>, the same for as long as the object lives, one for the class, <c>Object</c> and a <c>Variant</c>, another for each
/// interface the class implements and for <c>IUnknown</c>.
/// </summary>
[TestClass]
public sealed class ObjPtrTests
{
    private static readonly (string Name, string Source) IThing = ("IThing", ClassModule("IThing", "Option Explicit", "Public Sub Touch()", "End Sub"));

    private static readonly (string Name, string Source) Thing = ("Thing", ClassModule("Thing",
        "Option Explicit", "Implements IThing", "Public Value As Long", "Private Sub IThing_Touch()", "End Sub"));

    private static string Program(string body)
        => "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPrivate acc As String\r\nPrivate Sub P(ByVal v As Variant)\r\n    acc = acc & CStr(v) & \"|\"\r\nEnd Sub\r\n"
            + $"Public Sub Main()\r\nOn Error Resume Next\r\n{string.Join("\r\n", body.Split(" ;; "))}\r\nDebug.Print acc\r\nEnd Sub\r\n";

    private static async Task<string> RunAsync(string body)
        => string.Join(" ", await ModuleWorkspace.RunAsync([IThing, Thing], [], Program(body)));

    [TestMethod]
    [DataRow("P ObjPtr(Nothing) ;; Dim c As New Collection ;; P (ObjPtr(c) <> 0) ;; P (ObjPtr(c) = ObjPtr(c))", "0|True|True|",
        DisplayName = "O01 Nothing is 0, an object the same every time")]
    [DataRow("Dim t As Thing, i As IThing, o As Object, u As IUnknown ;; Set t = New Thing ;; Set i = t ;; Set o = t ;; Set u = t ;; P (ObjPtr(t) = ObjPtr(o)) ;; P (ObjPtr(t) = ObjPtr(i)) ;; P (ObjPtr(t) = ObjPtr(u)) ;; P (ObjPtr(i) = ObjPtr(u))",
        "True|False|False|False|", DisplayName = "O02 one interface for the class and Object, another for each interface")]
    [DataRow("Dim t As Thing, v As Variant ;; Set t = New Thing ;; Set v = t ;; P (ObjPtr(v) = ObjPtr(t))", "True|", DisplayName = "O06 a Variant holding an object")]
    [DataRow("Dim t As Thing, t2 As Thing ;; Set t = New Thing ;; Set t2 = New Thing ;; P (ObjPtr(t) <> ObjPtr(t2)) ;; Set t2 = t ;; P (ObjPtr(t) = ObjPtr(t2))", "True|True|",
        DisplayName = "two objects, two addresses; one object, one")]
    [DataRow("P (ObjPtr(New Collection) <> 0) ;; P TypeName(ObjPtr(New Collection))", "True|LongLong|", DisplayName = "an object that is no variable's, a LongPtr")]
    [DataRow("Dim a(1) As Thing ;; Set a(0) = New Thing ;; P (ObjPtr(a(0)) <> 0) ;; P (ObjPtr(a(0)) = ObjPtr(a(0))) ;; P ObjPtr(a(1))", "True|True|0|",
        DisplayName = "an element of an array of objects")]
    public async Task ObjPtr_IsTheAddressOfAnInterface(string body, string expected)
        => Assert.AreEqual(expected, await RunAsync(body));
}
