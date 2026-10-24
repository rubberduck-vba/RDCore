using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// An omitted argument's default, let-coerced once for each parameter, and a scalar passed by value to a <c>Variant</c> parameter as its payload (IR lowering
/// S1.7): the same values as the boxed evaluation - which the suite is also run with (<c>RDCORE_UNBOXED=0</c>), for these to hold both ways.
/// </summary>
[TestClass]
public sealed class CallArgumentPayloadTests
{
    private static string Run(string procedures, string body)
    {
        var program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n" + string.Join("\r\n", procedures.Split(" ;; "))
            + "\r\nPublic Sub Main()\r\n" + string.Join("\r\n", body.Split(" ;; ")) + "\r\nEnd Sub\r\n";
        return string.Join("/", RunAsync([], program).GetAwaiter().GetResult());
    }

    // the default is a value the parameter is made of: what the callee assigns to the parameter is its own, and the next call starts from the default again.
    [TestMethod]
    [DataRow("Private Sub F(Optional ByVal A As Long = 5) ;; Debug.Print A ;; A = A + 1 ;; End Sub", "F ;; F ;; F 9 ;; F", "5/5/9/5", DisplayName = "a Long, assigned by the callee")]
    [DataRow("Private Sub F(Optional A As Variant = 2.5) ;; Debug.Print TypeName(A) & \" \" & A ;; A = \"x\" ;; End Sub", "F ;; F", "Double 2.5/Double 2.5",
        DisplayName = "a Variant, assigned by the callee")]
    [DataRow("Private Sub F(Optional ByVal S As String = \"ab\") ;; Debug.Print S ;; Mid(S, 1, 1) = \"z\" ;; End Sub", "F ;; F", "ab/ab", DisplayName = "a String, written in place")]
    [DataRow("Private Sub F(Optional A As Variant) ;; Debug.Print IsMissing(A) ;; End Sub", "F ;; F 1 ;; F", "True/False/True", DisplayName = "Missing")]
    public void AnOmittedArgument_IsTheDeclaredDefault_EveryCall(string procedures, string body, string printed)
        => Assert.AreEqual(printed, Run(procedures, body));

    // MS-VBAL §5.5.1.2.12: a Variant holds a scalar it is let-assigned as it is, of its own type.
    [TestMethod]
    [DataRow("Dim l As Long ;; l = 7 ;; F l", "Long 7", DisplayName = "a Long")]
    [DataRow("F 7", "Integer 7", DisplayName = "an Integer literal")]
    [DataRow("F 2.5", "Double 2.5", DisplayName = "a Double literal")]
    [DataRow("Dim b As Boolean ;; b = True ;; F b", "Boolean True", DisplayName = "a Boolean")]
    [DataRow("Dim c As Currency ;; c = 1.25 ;; F c", "Currency 1.25", DisplayName = "a Currency")]
    [DataRow("Dim y As Byte ;; y = 200 ;; F y + 100", "Integer 300", DisplayName = "a Byte sum past a Byte")]
    [DataRow("Dim l As Long ;; l = 7 ;; F l * 2", "Long 14", DisplayName = "an expression")]
    public void AScalarPassedToAVariantParameter_KeepsItsType(string body, string printed)
        => Assert.AreEqual(printed, Run("Private Sub F(ByVal V As Variant) ;; Debug.Print TypeName(V) & \" \" & V ;; End Sub", body));

    // MS-VBAL §5.3.1.11: each argument is given to its parameter as the call is bound - by position or by name, in any order, a ParamArray collecting the
    // rest - and a ByRef parameter is a second name for a variable, a copy of anything else.
    [TestMethod]
    [DataRow("Private Function F(ByVal A As Long, ByVal B As Long) As Long ;; F = A * 10 + B ;; End Function", "Debug.Print F(B:=2, A:=1) ;; Debug.Print F(3, B:=4)",
        "12/34", DisplayName = "named, a function")]
    [DataRow("Private Sub S(ByVal A As Long, ByVal B As Long) ;; Debug.Print A * 10 + B ;; End Sub", "S B:=2, A:=1 ;; Call S(B:=4, A:=3)", "12/34",
        DisplayName = "named, a call statement")]
    [DataRow("Private Sub S(A As Long) ;; A = A + 1 ;; End Sub", "Dim l As Long ;; S l ;; S l ;; S (l) ;; S l + 0 ;; Debug.Print l", "2",
        DisplayName = "ByRef: a variable, then values")]
    [DataRow("Private Sub S(ByVal A As Long, ParamArray R() As Variant) ;; Debug.Print A & \":\" & UBound(R) ;; End Sub", "S 1 ;; S 1, 2, 3", "1:-1/1:1",
        DisplayName = "a ParamArray")]
    [DataRow("Private Sub S(Optional ByVal A As Long = 5, Optional ByVal B As Long = 6) ;; Debug.Print A * 10 + B ;; End Sub", "S ;; S B:=1 ;; S , 2", "56/51/52",
        DisplayName = "omitted, by name and by position")]
    public void AnArgument_IsGivenToItsParameter(string procedures, string body, string printed)
        => Assert.AreEqual(printed, Run(procedures, body));

    // a call statement's procedure is bound once: what the name resolves to in the scope it is written in.
    [TestMethod]
    public void ACallStatement_CallsTheProcedureItsNameResolvesTo_EveryTime()
        => Assert.AreEqual("1/2/3", Run("Private Sub S(ByVal A As Long) ;; Debug.Print A ;; End Sub", "Dim i As Long ;; For i = 1 To 3 ;; S i ;; Next"));
}
