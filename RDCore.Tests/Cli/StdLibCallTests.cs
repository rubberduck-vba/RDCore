using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A member of the standard library is bound once to the code behind it (IR lowering S1.9): every call of it is marshalled from what was found the first
/// time, whatever its arguments - given, left out, passed on as Missing, collected into a ParamArray - and whatever the call before it was given.
/// </summary>
[TestClass]
public sealed class StdLibCallTests
{
    private static string Run(string procedures, string body)
    {
        var program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n" + string.Join("\r\n", procedures.Split(" ;; "))
            + "\r\nPublic Sub Main()\r\n" + string.Join("\r\n", body.Split(" ;; ")) + "\r\nEnd Sub\r\n";
        return string.Join("/", RunAsync([], program).GetAwaiter().GetResult());
    }

    // Split's Limit, left out, is the -1 its declaration states; given, it is what it was given - one call after the other, in a loop.
    [TestMethod]
    public void AnOptionalArgument_LeftOutThenGiven_IsEachTimeWhatTheCallSays()
        => Assert.AreEqual("3/2/3/2", Run("", "Dim i As Long ;; For i = 1 To 2 ;; Debug.Print UBound(Split(\"a,b,c,d\", \",\")) ;; Debug.Print UBound(Split(\"a,b,c,d\", \",\", 3)) ;; Next"));

    // an Optional Variant parameter that was itself omitted passes on Missing, which the library takes as left out.
    [TestMethod]
    public void AMissingArgumentPassedOn_IsLeftOut()
        => Assert.AreEqual("bcd/bc", Run("Private Sub F(Optional L As Variant) ;; Debug.Print Mid$(\"abcd\", 2, L) ;; End Sub", "F ;; F 2"));

    [TestMethod]
    public void AParamArray_CollectsEachCallsOwnArguments()
        => Assert.AreEqual("b/c/a", Run("", "Debug.Print Choose(2, \"a\", \"b\", \"c\") ;; Debug.Print Choose(3, \"a\", \"b\", \"c\") ;; Debug.Print Choose(1, \"a\")"));

    // MS-VBAL §5.5.1.2.12: a Variant holds what it is let-assigned as it is, of its own type - an argument evaluated on payloads (S1.9), or what a call
    // returned - and the parameter is a value of its own.
    [TestMethod]
    [DataRow("Dim l As Long ;; l = 7 ;; F l, x", "Long 7", DisplayName = "a Long variable")]
    [DataRow("F 7, x", "Integer 7", DisplayName = "an Integer literal")]
    [DataRow("F 2.5, x", "Double 2.5", DisplayName = "a Double literal")]
    [DataRow("Dim s As String ;; s = \"ab\" ;; F s, x", "String ab", DisplayName = "a String variable")]
    [DataRow("F \"a\" & \"b\", x", "String ab", DisplayName = "Strings joined")]
    [DataRow("F CStr(12), x", "String 12", DisplayName = "a String a call returned")]
    [DataRow("F CLng(12), x", "Long 12", DisplayName = "a Long a call returned")]
    [DataRow("F Abs(-2), x", "Integer 2", DisplayName = "a Variant a call returned")]
    public void AnArgumentPassedToAVariant_KeepsItsType(string body, string printed)
        => Assert.AreEqual(printed, Run("Private Sub F(ByVal V As Variant, ByRef R As Long) ;; Debug.Print TypeName(V) & \" \" & V ;; End Sub",
            "Dim x As Long ;; " + body));

    [TestMethod]
    public void AStringPassedByValue_IsTheParametersOwn()
        => Assert.AreEqual("zb/ab/zb/ab", Run(
            "Private Sub F(ByVal S As String, ByRef R As Long) ;; Mid(S, 1, 1) = \"z\" ;; Debug.Print S ;; End Sub ;; "
                + "Private Sub G(ByVal V As Variant, ByRef R As Long) ;; Mid(V, 1, 1) = \"z\" ;; Debug.Print V ;; End Sub",
            "Dim t As String, x As Long ;; t = \"ab\" ;; F t, x ;; Debug.Print t ;; G t, x ;; Debug.Print t"));

    // what a call returns is let-assigned to the variable: a Variant's value converted to the variable's type, a String as it is.
    [TestMethod]
    [DataRow("Dim l As Long ;; l = Abs(-2.5) ;; Debug.Print TypeName(l) & \" \" & l", "Long 2", DisplayName = "a Variant Double to a Long")]
    [DataRow("Dim v As Variant ;; v = Abs(-2) ;; Debug.Print TypeName(v) & \" \" & v", "Integer 2", DisplayName = "a Variant Integer to a Variant")]
    [DataRow("Dim s As String ;; s = Mid(\"abc\", 2) ;; Debug.Print TypeName(s) & \" \" & s", "String bc", DisplayName = "a Variant String to a String")]
    [DataRow("Dim s As String ;; s = CStr(5) ;; Debug.Print s & s", "55", DisplayName = "a String to a String")]
    [DataRow("Dim s As String * 3 ;; s = CStr(5) ;; Debug.Print \"[\" & s & \"]\"", "[5  ]", DisplayName = "a String to a fixed-length String")]
    [DataRow("Dim i As Integer ;; On Error Resume Next ;; i = CLng(40000) ;; Debug.Print Err.Number & \" \" & i", "6 0", DisplayName = "a Long past an Integer")]
    public void WhatACallReturns_IsLetAssigned(string body, string printed)
        => Assert.AreEqual(printed, Run("", body));

    [TestMethod]
    public void AMemberOfAClassOfTheLibrary_IsCalledOnTheObjectItIsCalledOn()
        => Assert.AreEqual("2/1", Run("", "Dim a As New Collection, b As New Collection ;; a.Add 1 ;; a.Add 2 ;; b.Add 3 ;; Debug.Print a.Count ;; Debug.Print b.Count"));
}
