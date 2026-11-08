using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A unary <c>+</c> (RD-VBAL §5.6.9.3) is no operator: the VBE erases it, and a program runs as the code it keeps. What it is written before is what it
/// was: a String stays a String, a Boolean a Boolean, and a variable stays a variable that a call can change. What the VBE keeps of each was recorded in
/// Excel, and so were the values of the numbers, the String, the Boolean and Null.
/// </summary>
[TestClass]
public sealed class UnaryPlusTests
{
    // the program of a case: P writes what it is given, as a Variant, with its type, and what was written is printed at the end.
    private static async Task<string> Run(string body, string declarations = "")
    {
        string[] lines =
        [
            "Attribute VB_Name = \"Program\"",
            "Option Explicit",
            declarations,
            "Private acc As String",
            "Private Sub P(ByVal x As Variant)",
            "    If IsNull(x) Then acc = acc & \"Null|\" Else acc = acc & CStr(x) & \" \" & TypeName(x) & \"|\"",
            "End Sub",
            "Private Sub Inc(ByRef n As Long)",
            "    n = n + 1",
            "End Sub",
            "Public Sub Main()",
            "    Dim x As Variant",
            "    " + string.Join("\r\n    ", body.Split(" ;; ")),
            "    Debug.Print acc",
            "End Sub",
        ];
        return string.Join("/", await RunAsync([], string.Join("\r\n", lines) + "\r\n"));
    }

    [TestMethod]
    [DataRow("x = +1 ;; P x", "1 Integer|", DisplayName = "of a literal")]
    [DataRow("x = 2 * +3 ;; P x", "6 Integer|", DisplayName = "an operand of an operator")]
    [DataRow("x = + 1 ;; P x", "1 Integer|", DisplayName = "a space after it")]
    [DataRow("x = ++1 ;; P x", "1 Integer|", DisplayName = "twice")]
    [DataRow("x = -+1 ;; P x", "-1 Integer|", DisplayName = "under a negation")]
    [DataRow("x = +-1 ;; P x", "-1 Integer|", DisplayName = "over a negation")]
    [DataRow("x = Abs(+1) ;; P x", "1 Integer|", DisplayName = "an argument")]
    [DataRow("P +1", "1 Integer|", DisplayName = "the argument of a call statement")]
    [DataRow("Const C = +1 ;; P C", "1 Integer|", DisplayName = "the value of a constant")]
    [DataRow("x = 2 ^ +3 ^ 2 ;; P x", "64 Double|", DisplayName = "an operand of a chain of ^, which groups as the chain without it")]
    [DataRow("x = 2 ^ 3 ^ 2 ;; P x", "64 Double|", DisplayName = "the chain of ^ without it")]
    [DataRow("x = -+3 ^ 2 ;; P x", "-9 Double|", DisplayName = "under a negation of a ^, as -3 ^ 2")]
    [DataRow("x = -3 ^ 2 ;; P x", "-9 Double|", DisplayName = "the negation of a ^ without it")]
    public async Task AUnaryPlus_LeavesANumberAsItIs(string body, string printed)
        => Assert.AreEqual(printed, await Run(body));

    [TestMethod]
    public async Task AUnaryPlus_IsTheValueOfAModuleConstant()
        => Assert.AreEqual("1 Integer|", await Run("P C", "Private Const C = +1"));

    // no coercion: what the + is written before keeps its type, and Null is Null.
    [TestMethod]
    [DataRow("x = +\"1\" ;; P x", "1 String|", DisplayName = "a String")]
    [DataRow("x = +True ;; P x", "True Boolean|", DisplayName = "a Boolean")]
    [DataRow("x = +Null ;; P x", "Null|", DisplayName = "Null")]
    [DataRow("x = +\"1\" & \"2\" ;; P x", "12 String|", DisplayName = "a String concatenated")]
    public async Task AUnaryPlus_LeavesWhatItIsWrittenBefore_OfItsOwnType(string body, string printed)
        => Assert.AreEqual(printed, await Run(body));

    // a variable after a + is the variable: a call is given its reference, and changes it.
    [TestMethod]
    public async Task AUnaryPlus_BeforeAVariable_PassesTheVariableByReference()
        => Assert.AreEqual("2 Long|", await Run("Dim n As Long ;; n = 1 ;; Inc +n ;; P n"));

    // an object after a + is the object: Set assigns it.
    [TestMethod]
    public async Task AUnaryPlus_BeforeAnObject_IsTheObjectASetAssigns()
        => Assert.AreEqual("True Boolean|", await Run("Dim c As Collection ;; Set c = New Collection ;; Dim o As Object ;; Set o = +c ;; P o Is c"));

    // a + before TypeOf is before the TypeOf…Is expression: what the expression tests is unchanged.
    [TestMethod]
    public async Task AUnaryPlus_BeforeTypeOf_LeavesTheTypeOfIsExpression()
        => Assert.AreEqual("True Boolean|", await Run("Dim c As Collection ;; Set c = New Collection ;; x = +TypeOf c Is Collection ;; P x"));

    // a Tab clause, a space, then a + is two items: the + begins the second, and is erased. The file holds 1 printed at the second column.
    [TestMethod]
    public async Task AUnaryPlus_AfterATabClauseAndASpace_BeginsTheNextItem_OfAPrintToAFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rdcore-uplus-{Guid.NewGuid():N}.txt");
        try
        {
            string[] lines =
            [
                "Attribute VB_Name = \"Program\"",
                "Public Sub Main()",
                "    Dim ff As Integer",
                "    Dim s As String",
                "    ff = FreeFile",
                $"    Open \"{path}\" For Output As #ff",
                "    Print #ff, Tab(2) + 1",
                "    Close #ff",
                $"    Open \"{path}\" For Input As #ff",
                "    Line Input #ff, s",
                "    Close #ff",
                "    Debug.Print \"[\" & s & \"]\"",
                "End Sub",
            ];

            var output = await RunAsync([], string.Join("\r\n", lines) + "\r\n");

            Assert.AreEqual("[  1 ]", Assert.ContainsSingle(output));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
