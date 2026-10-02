using RDCore.Parsing;
using RDCore.SDK.Model.AST.Declarations;

namespace RDCore.Tests.Parser;

/// <summary>
/// <strong>MS-VBAL §5.3.1.2</strong> a procedure declared <c>Static</c> is a <c>Static</c> procedure, whatever its kind.
/// </summary>
[TestClass]
public sealed class StaticProcedureParserTests
{
    private static MemberDeclarationNode Member(string source)
    {
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), source);
        Assert.IsTrue(result.IsSuccess, string.Join("; ", result.SyntaxErrors.Select(error => error.Verbose)));
        return result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
    }

    [TestMethod]
    [DataRow("Static Sub Foo()\r\nEnd Sub", MemberKind.Procedure)]
    [DataRow("Public Static Sub Foo()\r\nEnd Sub", MemberKind.Procedure)]
    [DataRow("Private Static Function Foo() As Long\r\nEnd Function", MemberKind.Function)]
    [DataRow("Public Static Property Get Foo() As Long\r\nEnd Property", MemberKind.PropertyGet)]
    [DataRow("Public Static Property Let Foo(ByVal v As Long)\r\nEnd Property", MemberKind.PropertyLet)]
    [DataRow("Public Static Property Set Foo(ByVal v As Object)\r\nEnd Property", MemberKind.PropertySet)]
    public void AProcedureDeclaredStatic_IsStatic(string source, MemberKind kind)
    {
        var member = Member(source);

        Assert.AreEqual(kind, member.MemberKind);
        Assert.IsTrue(member.IsStatic);
    }

    [TestMethod]
    [DataRow("Sub Foo()\r\nEnd Sub")]
    [DataRow("Public Function Foo() As Long\r\nEnd Function")]
    [DataRow("Property Get Foo() As Long\r\nEnd Property")]
    [DataRow("Sub Foo()\r\nStatic n As Long\r\nEnd Sub", DisplayName = "a Static local does not make its procedure Static")]
    public void AProcedureNotDeclaredStatic_IsNotStatic(string source)
        => Assert.IsFalse(Member(source).IsStatic);
}
