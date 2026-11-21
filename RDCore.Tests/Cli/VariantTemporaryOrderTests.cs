using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// The order <strong>MS-VBA</strong> lets go of what the calls of a statement returned when the statement is over, when a <c>Variant</c> holds an object
/// (observed in Excel, 64-bit): the objects returned as objects first, those of the arguments in the order of the parameters, then the target's; then the
/// <c>Variant</c>s, in that same order. A <c>Box</c> writes its tag to <c>Trail</c> when it is made (<c>a</c>) and when it terminates (<c>ta</c>), which
/// shows the order (MS-VBAL §5.3.1.11). Each row runs in either engine.
/// </summary>
[TestClass]
public sealed class VariantTemporaryOrderTests
{
    private static readonly (string Name, string Source) Box = ("Box", ClassModule("Box",
        "Public Tag As String",
        "Private Sub Class_Terminate()", "    Trail = Trail & \"t\" & Tag & \" \"", "End Sub",
        "Public Function Two(ByVal A As Variant, ByVal B As Variant) As String", "    Two = Tag & \":\" & A & \",\" & B", "End Function",
        "Public Function Kept(ByVal V As Variant) As Variant", "    If IsObject(V) Then Set Kept = V Else Kept = V", "End Function"));

    private const string Log = "Public Trail As String\r\n"
        + "Private acc As String\r\n"
        + "Private Sub P(ByVal x As Variant)\r\n    acc = acc & CStr(x) & \"|\"\r\nEnd Sub\r\n"
        + "Public Function Ob(ByVal Tag As String) As Box\r\n    Trail = Trail & Tag & \" \"\r\n    Set Ob = New Box\r\n    Ob.Tag = Tag\r\nEnd Function\r\n"
        + "Public Function VOb(ByVal Tag As String) As Variant\r\n    Trail = Trail & Tag & \" \"\r\n    Dim B As Box\r\n    Set B = New Box\r\n"
        + "    B.Tag = Tag\r\n    Set VOb = B\r\nEnd Function\r\n"
        + "Public Function TagOf(ByVal V As Variant) As String\r\n    TagOf = V.Tag\r\nEnd Function\r\n"
        + "Public Function Two(ByVal A As Variant, ByVal B As Variant) As String\r\n    Two = A & \",\" & B\r\nEnd Function\r\n"
        + "Public Function Three(ByVal A As Variant, ByVal B As Variant, ByVal C As Variant) As String\r\n"
        + "    Three = A & \",\" & B & \",\" & C\r\nEnd Function\r\n"
        + "Public Sub TakeTwo(ByVal A As Variant, ByVal B As Variant)\r\n    Trail = Trail & \"take \"\r\nEnd Sub\r\n";

    // the body runs in a procedure of its own: Main prints what it collected.
    private static async Task<string> RunAsync(string body, VBExecutionEngine engine)
        => string.Join(" ", await ModuleWorkspace.RunAsync([Box], "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n" + Log
            + $"Private Sub Body()\r\n{string.Join("\r\n", body.Split(" ;; "))}\r\nEnd Sub\r\n"
            + "Public Sub Main()\r\n    Body\r\n    Debug.Print acc\r\nEnd Sub\r\n", engine));

    [TestMethod]
    [TestCategory("MS-VBAL 5.3.1.11 Procedure Invocation Argument Processing")]
    [DataRow("P Two(TagOf(VOb(\"a\")), TagOf(VOb(\"b\"))) ;; P Trail", "a,b|a b ta tb |", DisplayName = "V01 two Variants, given to a function")]
    [DataRow("P Two(TypeName(VOb(\"a\")), TypeName(VOb(\"b\"))) ;; P Trail", "Box,Box|a b ta tb |", DisplayName = "V02 two Variants, given to TypeName")]
    [DataRow("P Two(TypeName(VOb(\"a\")), Ob(\"b\").Tag) ;; P Trail", "Box,b|a b tb ta |", DisplayName = "V03 a Variant, left of an object")]
    [DataRow("P Two(Ob(\"a\").Tag, TypeName(VOb(\"b\"))) ;; P Trail", "a,Box|a b ta tb |", DisplayName = "V04 an object, left of a Variant")]
    [DataRow("P Three(TypeName(VOb(\"a\")), Ob(\"b\").Tag, TypeName(VOb(\"c\"))) ;; P Trail", "Box,b,Box|a b c tb ta tc |",
        DisplayName = "V05 an object between two Variants")]
    [DataRow("P Ob(\"t\").Two(TypeName(VOb(\"a\")), TypeName(VOb(\"b\"))) ;; P Trail", "t:Box,Box|a b t tt ta tb |",
        DisplayName = "V06 two Variants, and a target an object")]
    [DataRow("P Ob(\"t\").Two(Ob(\"a\").Tag, TypeName(VOb(\"b\"))) ;; P Trail", "t:a,Box|a b t ta tt tb |",
        DisplayName = "V07 an object and a Variant, and a target an object")]
    [DataRow("P VOb(\"t\").Two(Ob(\"a\").Tag, TypeName(VOb(\"b\"))) ;; P Trail", "t:a,Box|a b t ta tb tt |",
        DisplayName = "V08 an object and a Variant, and a target a Variant")]
    [DataRow("P Two(VOb(\"a\").Tag, VOb(\"b\").Tag) ;; P Trail", "a,b|a b ta tb |", DisplayName = "V09 members of two Variants")]
    [DataRow("TakeTwo VOb(\"a\"), VOb(\"b\") ;; P Trail", "a b take ta tb |", DisplayName = "V10 two Variants given whole, in a call statement")]
    [DataRow("TakeTwo TypeName(VOb(\"a\")), TypeName(VOb(\"b\")) ;; P Trail", "a b take ta tb |", DisplayName = "V11 two Variants, in a call statement")]
    [DataRow("P Two(TypeName(Ob(\"t\").Kept(VOb(\"a\"))), TypeName(VOb(\"b\"))) ;; P Trail", "Box,Box|a t b tt ta tb |",
        DisplayName = "V12 an argument whose call is given a Variant")]
    [DataRow("P Len(TypeName(VOb(\"a\"))) + Len(TypeName(VOb(\"b\"))) ;; P Trail", "6|a b ta tb |", DisplayName = "V13 operands of an expression")]
    [DataRow("Dim S As String ;; S = TypeName(VOb(\"a\")) & TypeName(VOb(\"b\")) ;; P Trail", "a b ta tb |", DisplayName = "V14 operands of a String Let")]
    [DataRow("P Len(Ob(\"a\").Tag) + Len(Ob(\"b\").Tag) ;; P Trail", "2|a b ta tb |", DisplayName = "O01 two objects, operands of an expression")]
    [DataRow("Dim S As String ;; S = Ob(\"a\").Tag & Ob(\"b\").Tag & Ob(\"c\").Tag ;; P Trail", "a b c ta tb tc |",
        DisplayName = "O03 three objects, operands of a String Let")]
    [DataRow("P Len(TypeName(VOb(\"a\"))) + Len(Ob(\"b\").Tag) ;; P Trail", "4|a b tb ta |", DisplayName = "O04 a Variant and an object, operands")]
    [DataRow("P Len(Ob(\"a\").Tag) + Len(TypeName(VOb(\"b\"))) ;; P Trail", "4|a b ta tb |", DisplayName = "O05 an object and a Variant, operands")]
    [DataRow("Dim S As String ;; S = TypeName(VOb(\"a\")) & Ob(\"b\").Tag & TypeName(VOb(\"c\")) ;; P Trail", "a b c tb ta tc |",
        DisplayName = "O06 an object between two Variants, operands")]
    [DataRow("P Three(Ob(\"a\").Tag, Ob(\"b\").Tag, Ob(\"c\").Tag) ;; P Trail", "a,b,c|a b c ta tb tc |", DisplayName = "O08 three objects, arguments")]
    [DataRow("P Two(Ob(\"a\").Tag, Ob(\"t\").Two(Ob(\"b\").Tag, Ob(\"c\").Tag)) ;; P Trail", "a,t:b,c|a b c t ta tb tc tt |",
        DisplayName = "O09 an argument whose call holds objects of its own, right of an object")]
    [DataRow("P Two(Ob(\"t\").Two(Ob(\"b\").Tag, Ob(\"c\").Tag), Ob(\"a\").Tag) ;; P Trail", "t:b,c,a|b c t a tb tc tt ta |",
        DisplayName = "O10 an argument whose call holds objects of its own, left of an object")]
    [DataRow("P Two(TypeName(VOb(\"a\")), Two(TypeName(VOb(\"b\")), Ob(\"c\").Tag)) ;; P Trail", "Box,Box,c|a b c tc ta tb |",
        DisplayName = "O11 an argument whose call is given a Variant and an object, right of a Variant")]
    [DataRow("P Two(Two(TypeName(VOb(\"b\")), Ob(\"c\").Tag), TypeName(VOb(\"a\"))) ;; P Trail", "Box,c,Box|b c a tc tb ta |",
        DisplayName = "O12 an argument whose call is given a Variant and an object, left of a Variant")]
    [DataRow("P Two(Ob(\"a\").Tag & TypeName(VOb(\"b\")), Ob(\"c\").Tag) ;; P Trail", "aBox,c|a b c ta tc tb |",
        DisplayName = "O13 an object and a Variant in one argument, left of an object")]
    [DataRow("P Two(TypeName(VOb(\"a\")) & Ob(\"b\").Tag, TypeName(VOb(\"c\"))) ;; P Trail", "Boxb,Box|a b c tb ta tc |",
        DisplayName = "O14 a Variant and an object in one argument, left of a Variant")]
    public async Task WhatAStatementsCallsReturned_IsLetGoOf_TheObjectsFirst_ThenTheVariants(string body, string expected)
    {
        Assert.AreEqual(expected, await RunAsync(body, VBExecutionEngine.Interpreter), "the interpreter");
        Assert.AreEqual(expected, await RunAsync(body, VBExecutionEngine.Compiled), "compiled");
    }
}
