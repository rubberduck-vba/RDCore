using RDCore.Parsing;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.Tests.Cli;
using System.Collections.Immutable;

namespace RDCore.Tests.Parser;

/// <summary>
/// The type an <c>As</c> clause names is the clause's <see cref="AsTypeExpressionNode"/> alone, not an expression of the declaration
/// (<strong>MS-VBAL §5.2.3.1</strong>, <strong>MS-VBAL §5.3.1.5</strong>): a parameter or a variable declared as a class, <c>Object</c> or a qualified type
/// has no expression child other than its default value. A procedure with such a parameter is analyzed whole.
/// </summary>
[TestClass]
public sealed class AsTypeClauseShapeTests
{
    private static ImmutableArray<SyntaxNode> ExpressionsOf(string source, Func<SyntaxNode, bool> declaration)
    {
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), source);
        var node = result.SyntaxTree!.Descendants().First(declaration);
        return [.. node.Children.OfType<ExpressionNode>()];
    }

    [TestMethod]
    [DataRow("Public Sub Run(ByVal o As Object)\r\nEnd Sub", "o", "Object", null, DisplayName = "a parameter As Object")]
    [DataRow("Public Sub Run(ByVal o As Thing)\r\nEnd Sub", "o", "Thing", null, DisplayName = "a parameter As a class")]
    [DataRow("Public Sub Run(ByVal r As Excel.Range)\r\nEnd Sub", "r", "Range", "Excel", DisplayName = "a parameter As a qualified type")]
    [DataRow("Public Sub Run(ByVal n As Long)\r\nEnd Sub", "n", "Long", null, DisplayName = "a parameter As an intrinsic type")]
    [DataRow("Private o As Thing", "o", "Thing", null, DisplayName = "a variable of the module As a class")]
    [DataRow("Private o As New Thing", "o", "Thing", null, DisplayName = "a variable of the module As New a class")]
    [DataRow("Private r As Excel.Range", "r", "Range", "Excel", DisplayName = "a variable of the module As a qualified type")]
    public void TheTypeOfAnAsClause_IsTheClauseAlone(string source, string name, string typeName, string? qualifier)
    {
        var expressions = ExpressionsOf(source, node => node is ParameterDeclarationNode { Name: var parameter } && parameter == name
            || node is VariableDeclarationNode { Name: var variable } && variable == name);

        var clause = Assert.IsInstanceOfType<AsTypeExpressionNode>(Assert.ContainsSingle(expressions), string.Join(", ", expressions.Select(node => node.GetType().Name)));
        Assert.AreEqual(typeName, clause.TypeName);
        Assert.AreEqual(qualifier, clause.QualifierName);
    }

    // the default value of an Optional parameter is the expression after its As clause, and the only one.
    [TestMethod]
    public void AnOptionalParameterAsAClass_HasItsClauseAndItsDefaultValue()
    {
        var expressions = ExpressionsOf("Public Sub Run(Optional ByVal o As Thing = Nothing)\r\nEnd Sub", node => node is ParameterDeclarationNode);

        Assert.HasCount(2, expressions, string.Join(", ", expressions.Select(node => node.GetType().Name)));
        Assert.IsInstanceOfType<AsTypeExpressionNode>(expressions[0]);
        Assert.IsNotInstanceOfType<AsTypeExpressionNode>(expressions[1]);
    }

    private static readonly (string Name, string Source)[] Things = [("Thing", ModuleWorkspace.ClassModule("Thing", "Option Explicit", "Public Count As Long"))];

    [TestMethod]
    public async Task AProcedureWithAParameterAsAClassOrAsObject_IsAnalyzedWhole()
    {
        var payload = await ModuleWorkspace.SemanticsAsync(Things, string.Join("\r\n",
            "Attribute VB_Name = \"Program\"",
            "Option Explicit",
            "Public Sub Main()",
            "    Run New Thing, New Thing",
            "End Sub",
            "Public Sub Run(ByVal o As Object, ByVal t As Thing, Optional ByVal u As Thing = Nothing)",
            "    t.Count = t.Count + 1",
            "End Sub",
            ""), moduleName: "Program");

        var run = payload.Modules.Single().Procedures.Single(procedure => procedure.Procedure.Fragment.EndsWith(".Run", StringComparison.Ordinal));
        Assert.IsTrue(run.IsFullyAnalyzed);
    }

    [TestMethod]
    public async Task AnOptionalParameterAsAClass_IsNothingWhenOmitted()
        => CollectionAssert.AreEqual(new[] { "True", "False" }, await ModuleWorkspace.RunAsync(Things, string.Join("\r\n",
            "Attribute VB_Name = \"Program\"",
            "Option Explicit",
            "Public Sub Main()",
            "    Run",
            "    Run New Thing",
            "End Sub",
            "Public Sub Run(Optional ByVal o As Thing = Nothing)",
            "    Debug.Print o Is Nothing",
            "End Sub",
            "")));
}
