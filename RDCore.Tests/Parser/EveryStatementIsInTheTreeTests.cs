using RDCore.Parsing;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;

namespace RDCore.Tests.Parser;

/// <summary>
/// A statement the parser reads is a node of the tree. One that comes out as nothing leaves a procedure indistinguishable from the same procedure without it,
/// and the source cannot be reconstructed from the tree: that is what the graphics statements were, and what this keeps from happening again to any form of
/// <c>mainBlockStmt</c>.
/// </summary>
[TestClass]
public sealed class EveryStatementIsInTheTreeTests
{
    [TestMethod]
    [DataRow("Circle (1, 1), 2")]
    [DataRow("Form1.Circle Step (1, 1), 2, , 3")]
    [DataRow("PSet (1, 1)")]
    [DataRow("Scale (0, 0)-(10, 10)")]
    [DataRow("Form1.Scale (0, 0)-(10, 10)")]
    [DataRow("Line (0, 0)-(10, 10)")]
    [DataRow("Line -(10, 10), 3, BF")]
    [DataRow("Stop")]
    [DataRow("End")]
    [DataRow("x = 1")]
    [DataRow("Let x = 1")]
    [DataRow("Set x = Nothing")]
    [DataRow("LSet a = b")]
    [DataRow("RSet a = b")]
    [DataRow("Mid$(s, 1, 1) = \"a\"")]
    [DataRow("Print 1")]
    [DataRow("Debug.Print 1")]
    [DataRow("Debug.Assert x")]
    [DataRow("Open \"a\" For Input As #1")]
    [DataRow("Close #1")]
    [DataRow("Reset")]
    [DataRow("Seek #1, 1")]
    [DataRow("Lock #1")]
    [DataRow("Unlock #1")]
    [DataRow("Line Input #1, x")]
    [DataRow("Width #1, 40")]
    [DataRow("Write #1, x, y")]
    [DataRow("Input #1, x")]
    [DataRow("Put #1, , x")]
    [DataRow("Get #1, , x")]
    [DataRow("Name \"a\" As \"b\"")]
    [DataRow("Const c = 1")]
    [DataRow("Dim a(1 To 3) As Long")]
    [DataRow("Static x As Long")]
    [DataRow("ReDim a(3)")]
    [DataRow("Erase a")]
    [DataRow("Error 5")]
    [DataRow("On Error GoTo 0")]
    [DataRow("On Error Resume Next")]
    [DataRow("Resume Next")]
    [DataRow("Resume 10")]
    [DataRow("GoTo A")]
    [DataRow("GoSub A")]
    [DataRow("Return")]
    [DataRow("On x GoTo A, B")]
    [DataRow("On x GoSub A, B")]
    [DataRow("Exit Sub")]
    [DataRow("RaiseEvent E(1)")]
    [DataRow("Call Foo(1)")]
    [DataRow("Foo 1, 2")]
    [DataRow("If x Then y = 1 Else y = 2")]
    [DataRow("If x Then\r\ny = 1\r\nEnd If")]
    [DataRow("Select Case x\r\nCase 1\r\nEnd Select")]
    [DataRow("For i = 1 To 3\r\nNext")]
    [DataRow("For Each v In c\r\nNext")]
    [DataRow("Do\r\nLoop")]
    [DataRow("While x\r\nWend")]
    [DataRow("With x\r\n.y = 1\r\nEnd With")]
    public void AStatement_IsAtLeastOneNodeOfTheProcedure(string statement)
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), $"Public Sub Main()\r\n{statement}\r\nEnd Sub\r\n");

        var main = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        Assert.IsTrue(main.Children.Any(node => node is not (LineNumberNode or LineLabelNode)), $"'{statement}' came out of the parser as nothing");
    }
}
