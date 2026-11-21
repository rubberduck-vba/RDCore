namespace RDCore.Tests.Cli;

/// <summary>
/// <c>VarPtr</c> of a variable of an object, in the code of its class: one address for as long as the object lives, whatever calls are made on it or
/// on other objects meanwhile, and whichever variable refers to the object, as <strong>MS-VBA</strong> gives it (observed in Excel, 64-bit). MS-VBAL
/// does not describe <c>VarPtr</c>. The class and module are those of <see cref="ObjectArgumentOrderTests"/>; <c>Box.Addr</c> is
/// <c>VarPtr(Value)</c>.
/// </summary>
[TestClass]
public sealed class ObjectFieldAddressTests
{
    [TestMethod]
    [DataRow("Dim B As New Box ;; Dim First As LongPtr ;; First = B.Addr ;; B.Touch ;; B.Touch ;; P (B.Addr = First) ;; P (First <> 0)", "True|True|",
        DisplayName = "across calls of members of the object")]
    [DataRow("Dim B As Box, C As Box, First As LongPtr ;; Set B = New Box ;; First = B.Addr ;; Set C = New Box ;; P (C.Addr <> First) ;; Set C = Nothing ;; "
        + "P (B.Addr = First)", "True|True|", DisplayName = "across another object made and let go of")]
    [DataRow("Dim B As Box, D As Box, First As LongPtr ;; Set B = New Box ;; First = B.Addr ;; Set D = B ;; Set B = Nothing ;; P (D.Addr = First)", "True|",
        DisplayName = "through another variable, once the first lets go")]
    [DataRow("Dim B As Box, First As LongPtr ;; Set B = New Box ;; First = B.Addr ;; P (AddrOf(B) = First) ;; P (AddrRef(B) = First)", "True|True|",
        DisplayName = "through a parameter, by value and by reference")]
    [DataRow("Dim B As Box, First As LongPtr ;; Set B = New Box ;; First = B.Addr ;; B.Value = 7 ;; P (B.Addr = First) ;; P B.Value", "True|7|",
        DisplayName = "after the variable is assigned from outside the class")]
    [DataRow("Dim B As New Box ;; P B.Stable", "True|", DisplayName = "in one call of a member, across the calls it makes")]
    public async Task AVariableOfAnObject_KeepsItsAddressForTheObjectsLife(string body, string expected)
        => Assert.AreEqual(expected, await ObjectArgumentOrderTests.RunAsync(body));

    // a variable of each kind a class can have, each assigned by Touch: AddrOf is VarPtr of the one named.
    private static readonly (string Name, string Source) Kinds = ("Kinds", ModuleWorkspace.ClassModule("Kinds",
        "Option Explicit",
        "Private Type TPoint", "    X As Long", "    Y As Long", "End Type",
        "Private V As Variant", "Private S As String", "Private O As Object", "Private D As Double", "Private R As TPoint",
        "Public Function AddrOf(ByVal Which As String) As LongPtr",
        "    Select Case Which",
        "        Case \"V\": AddrOf = VarPtr(V)", "        Case \"S\": AddrOf = VarPtr(S)", "        Case \"O\": AddrOf = VarPtr(O)",
        "        Case \"D\": AddrOf = VarPtr(D)", "        Case \"R\": AddrOf = VarPtr(R)",
        "    End Select",
        "End Function",
        "Public Sub Touch()", "    V = V & \"x\"", "    S = S & \"x\"", "    Set O = New Collection", "    D = D + 1", "    R.X = R.X + 1", "End Sub"));

    [TestMethod]
    [DataRow("V", DisplayName = "a Variant")]
    [DataRow("S", DisplayName = "a String")]
    [DataRow("O", DisplayName = "an object")]
    [DataRow("D", DisplayName = "a Double")]
    [DataRow("R", DisplayName = "a record")]
    public async Task AVariableOfAnObject_OfAnyKind_KeepsItsAddressAcrossTheAssignmentsAndCallsOfTheObject(string which)
        => Assert.AreEqual("True|True|", string.Join(" ", await ModuleWorkspace.RunAsync([Kinds], [],
            "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPrivate acc As String\r\n"
            + "Private Sub P(ByVal v As Variant)\r\n    acc = acc & CStr(v) & \"|\"\r\nEnd Sub\r\n"
            + "Public Sub Main()\r\nDim K As New Kinds\r\nDim First As LongPtr\r\n"
            + $"First = K.AddrOf(\"{which}\")\r\nK.Touch\r\nK.Touch\r\nP (K.AddrOf(\"{which}\") = First)\r\nP (First <> 0)\r\n"
            + "Debug.Print acc\r\nEnd Sub\r\n")));
}
