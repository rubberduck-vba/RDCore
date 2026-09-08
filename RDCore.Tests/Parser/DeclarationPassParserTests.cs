using RDCore.Parsing;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Text.Json;

namespace RDCore.Tests.Parser;

[TestClass]
public class DeclarationPassParserTests
{
    [TestMethod]
    public void InvalidContent_ReturnsErrorResult()
    {
        // arrange
        var uri = new Uri("file://C:/RDCore.Tests/Parser/TestModule.bas");
        var sut = new ModuleParser();
        var content = "invalid content";

        // act
        var result = sut.Parse(uri, ModuleType.StdModule, content);

        // assert
        Assert.IsFalse(result.IsSuccess);
        Assert.IsNotEmpty(result.SyntaxErrors);
    }

    [TestMethod]
    // review D1: an operator anywhere in a #If / #Const used to desync the CC listener and forfeit
    // EVERY directive in the module. Walking the parse tree (rather than AddParseListener) fixes it.
    [DataRow("#If VBA7 And Win64 Then\r\nPublic X As Long\r\n#End If", DisplayName = "#If A And B")]
    [DataRow("#If DEBUG = 1 Then\r\nPublic X As Long\r\n#End If", DisplayName = "#If A = B")]
    [DataRow("#If Not DEBUG Then\r\nPublic X As Long\r\n#End If", DisplayName = "#If Not A")]
    [DataRow("#Const A = 1\r\n#Const B = A + 1\r\n#If B Then\r\nPublic X As Long\r\n#End If", DisplayName = "#Const B = A + 1")]
    public void OperatorInConditional_PreservesPrecompilerTrivia(string content)
    {
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), ModuleType.StdModule, content);

        Assert.IsNotEmpty(result.PrecompilerTrivia);
    }

    [TestMethod]
    public void SplitStatementConditional_ReportsLocatedSyntaxErrors()
    {
        // legal VBA, but a #If that splits a statement (here the function header) cannot be parsed by
        // an ANTLR grammar — Rubberduck never supported it either. we don't try; we only make sure the
        // failure survives the two-stage parse as syntax-error metadata a client can anchor a squiggle on.
        const string content = """
            #If VBA7 Then
            Private Function GetPtr() As LongPtr
            #Else
            Private Function GetPtr() As Long
            #End If
                GetPtr = 0
            End Function
            """;
        var uri = TestUri.TestModuleUri();

        var result = new ModuleParser().Parse(uri, ModuleType.StdModule, content);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsNotEmpty(result.SyntaxErrors);
        Assert.IsTrue(result.SyntaxErrors.All(error => error.Location.Uri == uri));
        Assert.IsTrue(
            result.SyntaxErrors.Any(error => error.Location.Range.Start.Line > 0),
            "at least one syntax error should carry a real source location");
    }

    [TestMethod]
    public void PrecompilerTrivia_IsIncludedInResult()
    {
        const string content = """
            Option Explicit
            #Const DEBUG = 1
            #If DEBUG Then
            Dim Foo As Long
            Dim Bar As Integer
            #Else
            Dim Foo As Double
            #End If
            """;
        var uri = TestUri.TestModuleUri();
        var sut = new ModuleParser();

        var result = sut.Parse(uri, ModuleType.StdModule, content);

        Assert.IsNotNull(result.SyntaxTree);
        Assert.HasCount(1, result.SyntaxTree.Children.OfType<ModuleOptionDirectiveNode>());
        Assert.HasCount(3, result.SyntaxTree.Children.OfType<VariableDeclarationNode>());

        // ccBlock matches itself
        Assert.HasCount(1, result.PrecompilerTrivia.OfType<PrecompilerTriviaNode>());
    }

    private const string _testModuleWithDeclarations = """
Option Explicit

#Const DEBUG = 1

Public Sub Test()
    DoSomething 42
    DoSomething 32767
    DoSomething -32768
End Sub

Private Sub DoSomething(ByVal SomeValue As Long)
    Const MultiplierValue = 2

#If DEBUG Then
    Dim OtherValue As Integer
    OtherValue = IIf(SomeValue > 32767, 0, 10)
#EndIf

    On Error GoTo CleanFail
    Debug.Print MultiplierValue * SomeValue + OtherValue

CleanExit:
    Exit Sub

CleanFail:
    Debug.Print Err.Description
    Resume CleanExit
End Sub
""";

    [TestMethod]
    public void ValidContent_ReturnsModuleNodeWithChildren()
    {
        // arrange
        var content = _testModuleWithDeclarations;
        var uri = new Uri("file://C:/RDCore.Tests/Parser/TestModule.bas");
        var sut = new ModuleParser();

        // act
        var result = sut.Parse(uri, ModuleType.StdModule, content);

        // assert
        Assert.IsNotNull(result.SyntaxTree);
        Assert.IsGreaterThan(0, result.SyntaxTree!.Children.Length);
    }

    [TestMethod]
    public void ValidContent_ContainsLocalVariableChildren()
    {
        // arrange
        var content = _testModuleWithDeclarations;
        var uri = new Uri("file://C:/RDCore.Tests/Parser/TestModule.bas");
        var sut = new ModuleParser();

        // act
        var result = sut.Parse(uri, ModuleType.StdModule, content);
        var localVariables = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>()
            .SelectMany(member => member.Children.OfType<VariableDeclarationNode>())
            .ToArray();

        // assert
        Assert.IsGreaterThan(0, localVariables.Length);
    }

    [TestMethod]
    public void ValidContent_ContainsLocalConstChildren()
    {
        // arrange
        var content = _testModuleWithDeclarations;
        var uri = new Uri("file://C:/RDCore.Tests/Parser/TestModule.bas");
        var sut = new ModuleParser();

        // act
        var result = sut.Parse(uri, ModuleType.StdModule, content);
        var localConstants = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>()
            .SelectMany(member => member.Children.OfType<ConstantDeclarationNode>())
            .ToArray();

        // assert
        Assert.IsGreaterThan(0, localConstants.Length);
    }

    [TestMethod]
    public void ValidContent_ContainsLabelChildren()
    {
        // arrange
        var content = _testModuleWithDeclarations;
        var uri = new Uri("file://C:/RDCore.Tests/Parser/TestModule.bas");
        var sut = new ModuleParser();

        // act
        var result = sut.Parse(uri, ModuleType.StdModule, content);
        var lineLabels = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>()
            .SelectMany(member => member.Children.OfType<LineLabelNode>())
            .ToArray();

        // assert
        Assert.IsGreaterThan(0, lineLabels.Length);
    }

    [TestMethod]
    public void SyntaxTree_DeserializesToSyntaxNode()
    {
        var content = _testModuleWithDeclarations;
        var uri = new Uri("file://C:/RDCore.Tests/Parser/TestModule.bas");
        var sut = new ModuleParser();

        // act
        var result = sut.Parse(uri, ModuleType.StdModule, content);
        if (result.IsSuccess)
        {
            var ast = result.SyntaxTree!;
            var json = JsonSerializer.Serialize(ast);
            var deserialized = JsonSerializer.Deserialize<ModuleNode>(json);

            Assert.AreEqual(ast.Identity, deserialized?.Identity);
            Assert.AreSequenceEqual(ast.Children.Select(node => node.Identity), deserialized?.Children.Select(node => node.Identity));
        }
        else
        {
            Assert.Inconclusive(result.SyntaxErrors[0]!.Description);
        }
    }

    [TestMethod]
    public void DeclareOrEvent_DoesNotPoisonLaterModuleDeclarationCapture()
    {
        // regression: a Declare/Event has an argList but no body, and ExitArgList used to set
        // _isInsideProcedure, leaving declaration-pass expression capture off for every module-level
        // declaration between it and the next procedure.
        const string content = """
            Option Explicit
            Declare PtrSafe Sub Foo Lib "k" (ByVal a As Long)
            Public Const Answer As Long = 42
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), ModuleType.StdModule, content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var literal = Descendants(result.SyntaxTree!).OfType<LiteralExpressionNode>().SingleOrDefault();
        Assert.IsNotNull(literal, "the Const's '42' literal was dropped from the AST");
        Assert.IsInstanceOfType<VBIntegerValue>(literal!.StaticValue);
        Assert.AreEqual((short)42, ((VBIntegerValue)literal.StaticValue).Value);
    }

    [TestMethod]
    // C8: the declaration pass captured leaf literals only and dropped the unary minus, so
    // `Const N = -1` came out as +1.
    [DataRow("Public Const N As Long = -1", -1L)]
    [DataRow("Public Const N = -32768", -32768L)]
    [DataRow("Public Const N As Integer = -5", -5L)]
    [DataRow("Public Const N = - -7", 7L)]
    public void NegativeConstant_KeepsItsSign(string source, long expected)
    {
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), ModuleType.StdModule, source);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var literal = Descendants(result.SyntaxTree!).OfType<LiteralExpressionNode>().Single();
        long actual = literal.StaticValue switch
        {
            VBIntegerValue v => v.Value,
            VBLongValue v => v.Value,
            _ => throw new AssertFailedException($"unexpected value type {literal.StaticValue.GetType().Name}"),
        };
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void UserDefinedType_EmitsMemberFieldNodes()
    {
        const string content = """
            Public Type TPoint
                X As Long
                Y As String
            End Type
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), ModuleType.StdModule, content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var udt = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>()
            .Single(member => member.MemberKind == MemberKind.UserDefinedType);
        var fields = udt.Children.OfType<MemberDeclarationNode>()
            .Where(member => member.MemberKind == MemberKind.UserDefinedTypeField)
            .ToArray();

        Assert.HasCount(2, fields);
        Assert.AreEqual("X", fields[0].Name);
        Assert.AreEqual("Y", fields[1].Name);
        Assert.HasCount(1, fields[0].Children.OfType<AsTypeExpressionNode>());
    }

    private static IEnumerable<SyntaxNode> Descendants(SyntaxNode node)
    {
        foreach (var child in node.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
