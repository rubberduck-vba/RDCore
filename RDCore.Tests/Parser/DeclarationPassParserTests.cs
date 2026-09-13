using RDCore.Parsing;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
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
        var result = sut.Parse(uri, content);

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
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);

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

        var result = new ModuleParser().Parse(uri, content);

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

        var result = sut.Parse(uri, content);

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
        var result = sut.Parse(uri, content);

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
        var result = sut.Parse(uri, content);
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
        var result = sut.Parse(uri, content);
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
        var result = sut.Parse(uri, content);
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
        var result = sut.Parse(uri, content);
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
    // the AST crosses the process boundary as an STJ string; the array-bounds / ReDim nodes carry
    // data beyond child identities (bounds text, Preserve, qualifier) that must survive the round-trip.
    public void SyntaxTree_RoundTrips_ArrayAndRedimNodes()
    {
        const string content = """
            Public Sub Grow()
                Dim Grid(1 To 3) As Long
                ReDim Preserve Grid(1 To 10)
            End Sub
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var json = JsonSerializer.Serialize(result.SyntaxTree!);
        var member = JsonSerializer.Deserialize<ModuleNode>(json)!.Children.OfType<MemberDeclarationNode>().Single();

        var dimBounds = member.Children.OfType<VariableDeclarationNode>().Single().Children.OfType<ArrayBoundsNode>().Single();
        Assert.AreEqual(new ArrayDimensionBound("1", "3"), dimBounds.Bounds.Single());

        var redim = member.Children.OfType<RedimDeclarationNode>().Single();
        Assert.AreEqual("Grid", redim.Name);
        Assert.IsTrue(redim.IsPreserve);
        Assert.AreEqual(new ArrayDimensionBound("1", "10"), redim.Children.OfType<ArrayBoundsNode>().Single().Bounds.Single());
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

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
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
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), source);
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
    // the declaration pass used to capture leaf literals only: `Const N = 1 + 2` produced two flat
    // sibling literals with no node recording that they were meant to be added together.
    public void BinaryOperator_BuildsAnOperatorTree()
    {
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), "Public Const N = 1 + 2");
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);

        var op = Descendants(result.SyntaxTree!).OfType<VBBinaryOperatorExpressionNode>().Single();
        Assert.AreEqual(Tokens.AdditionOp, op.Token);
        Assert.AreEqual(1L, IntValue(op.Left));
        Assert.AreEqual(2L, IntValue(op.Right));
    }

    [TestMethod]
    public void BinaryOperators_RespectPrecedence()
    {
        // `*` binds tighter than `+`: the tree must nest 2*3 under the right operand of 1+(2*3),
        // not flatten into three siblings or associate as (1+2)*3.
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), "Public Const N = 1 + 2 * 3");
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);

        var add = Descendants(result.SyntaxTree!).OfType<VBBinaryOperatorExpressionNode>().Single(n => n.Token == Tokens.AdditionOp);
        Assert.AreEqual(1L, IntValue(add.Left));
        var mult = add.Right as VBBinaryOperatorExpressionNode;
        Assert.IsNotNull(mult);
        Assert.AreEqual(Tokens.MultiplicationOp, mult.Token);
        Assert.AreEqual(2L, IntValue(mult.Left));
        Assert.AreEqual(3L, IntValue(mult.Right));
    }

    [TestMethod]
    [DataRow("Public Const N = 2 ^ 3", "^")]
    [DataRow("Public Const N = 6 \\ 4", "\\")]
    [DataRow("Public Const N = 7 Mod 2", "Mod")]
    [DataRow("Public Const N = 1 - 2", "-")]
    [DataRow("Public Const N = 1 = 2", "=")]
    [DataRow("Public Const N = 1 <> 2", "<>")]
    [DataRow("Public Const N = 1 < 2", "<")]
    [DataRow("Public Const N = 1 > 2", ">")]
    [DataRow("Public Const N = 1 <= 2", "<=")]
    [DataRow("Public Const N = 1 >= 2", ">=")]
    [DataRow("Public Const N = True And False", "And")]
    [DataRow("Public Const N = True Or False", "Or")]
    [DataRow("Public Const N = True Xor False", "Xor")]
    [DataRow("Public Const N = True Eqv False", "Eqv")]
    [DataRow("Public Const N = True Imp False", "Imp")]
    public void BinaryOperator_MapsToItsToken(string source, string expectedToken)
    {
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), source);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);

        var op = Descendants(result.SyntaxTree!).OfType<VBBinaryOperatorExpressionNode>().Single();
        Assert.AreEqual(expectedToken, op.Token);
    }

    [TestMethod]
    public void ConcatOperator_MapsToItsToken()
    {
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), "Public Const N = \"a\" & \"b\"");
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);

        var op = Descendants(result.SyntaxTree!).OfType<VBBinaryOperatorExpressionNode>().Single();
        Assert.AreEqual(Tokens.ConcatOp, op.Token);
    }

    [TestMethod]
    public void LogicalNot_BuildsAUnaryOperatorNode()
    {
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), "Public Const N = Not True");
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);

        var op = Descendants(result.SyntaxTree!).OfType<VBUnaryOperatorExpressionNode>().Single();
        Assert.AreEqual(Tokens.LogicalNotOp, op.Token);
    }

    [TestMethod]
    // regression: the unary-minus fold logic mistakenly swept a sibling AsTypeExpressionNode into
    // its own operand set, losing both the type node and the sign (see NegativeConstant_KeepsItsSign
    // for the sign; this pins the type node survives alongside it).
    public void NegativeConstant_WithAsClause_KeepsBothTheTypeAndTheSign()
    {
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), "Public Const N As Long = -1");
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);

        var constNode = result.SyntaxTree!.Children.OfType<ConstantDeclarationNode>().Single();
        Assert.HasCount(1, constNode.Children.OfType<AsTypeExpressionNode>());
        var literal = constNode.Children.OfType<LiteralExpressionNode>().Single();
        Assert.AreEqual(-1L, IntValue(literal));
    }

    [TestMethod]
    public void ReDimAsClause_DoesNotLeakItsTypeOntoTheMember()
    {
        // backlog G: ExitAsTypeClause had no parent guard, so a `ReDim x() As Long` in a body
        // attached its type node to the enclosing member. A real local `Dim` still keeps its own.
        const string content = """
            Public Sub Grow()
                Dim total As Long
                ReDim buffer(1 To 10) As Long
            End Sub
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        Assert.IsEmpty(member.Children.OfType<AsTypeExpressionNode>());

        var local = member.Children.OfType<VariableDeclarationNode>().Single(variable => variable.Name == "total");
        Assert.ContainsSingle(local.Children.OfType<AsTypeExpressionNode>());
    }

    [TestMethod]
    // MS-VBAL 5.4.3.1: a local declared `Static` (or in a `Static` procedure) keeps its value across calls.
    public void LocalDeclaration_CapturesStaticToken()
    {
        const string content = """
            Public Sub Tally()
                Static Count As Long
                Dim Delta As Long
            End Sub
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var locals = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single()
            .Children.OfType<VariableDeclarationNode>().ToArray();

        Assert.IsTrue(locals.Single(v => v.Name == "Count").IsStatic);
        Assert.IsFalse(locals.Single(v => v.Name == "Delta").IsStatic);
    }

    [TestMethod]
    // MS-VBAL 5.2.3.1.3 Array Dim: `( [ boundsList ] )`, each dimSpec `[ <lower> To ] <upper>`.
    public void LocalArrayDeclaration_CapturesBoundsPerDimension()
    {
        const string content = """
            Public Sub Fill()
                Dim Grid(1 To 3, 0 To 4) As Long
                Dim Row(10) As Long
                Dim Buffer() As Byte
                Dim Scalar As Long
            End Sub
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var locals = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single()
            .Children.OfType<VariableDeclarationNode>()
            .ToDictionary(v => v.Name, v => v.Children.OfType<ArrayBoundsNode>().SingleOrDefault());

        var grid = locals["Grid"]!;
        Assert.IsFalse(grid.IsResizable);
        Assert.AreEqual(2, grid.Rank);
        Assert.AreEqual(new ArrayDimensionBound("1", "3"), grid.Bounds[0]);
        Assert.AreEqual(new ArrayDimensionBound("0", "4"), grid.Bounds[1]);

        var row = locals["Row"]!;
        Assert.IsFalse(row.IsResizable);
        Assert.AreEqual(new ArrayDimensionBound(null, "10"), row.Bounds.Single());

        var buffer = locals["Buffer"]!;
        Assert.IsTrue(buffer.IsResizable);
        Assert.AreEqual(0, buffer.Rank);

        Assert.IsNull(locals["Scalar"]);
    }

    [TestMethod]
    // MS-VBAL 5.4.3.3 ReDim: `ReDim [Preserve] name(<bounds>) [As type]`, one target per comma.
    public void Redim_EmitsRedimDeclarationNode_WithBoundsAndPreserve()
    {
        const string content = """
            Public Sub Grow(ByVal n As Long)
                ReDim Preserve Grid(1 To 10, 0 To n)
            End Sub
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var redim = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single()
            .Children.OfType<RedimDeclarationNode>().Single();

        Assert.AreEqual("Grid", redim.Name);
        Assert.IsNull(redim.QualifierName);
        Assert.IsTrue(redim.IsPreserve);

        var bounds = redim.Children.OfType<ArrayBoundsNode>().Single();
        Assert.AreEqual(2, bounds.Rank);
        Assert.AreEqual(new ArrayDimensionBound("1", "10"), bounds.Bounds[0]);
        Assert.AreEqual(new ArrayDimensionBound("0", "n"), bounds.Bounds[1]);
    }

    [TestMethod]
    public void Redim_UpperBoundOnly_HasNullLowerBound()
    {
        const string content = """
            Public Sub Grow()
                ReDim Buffer(10)
            End Sub
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var redim = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single()
            .Children.OfType<RedimDeclarationNode>().Single();

        Assert.IsFalse(redim.IsPreserve);
        Assert.AreEqual(new ArrayDimensionBound(null, "10"), redim.Children.OfType<ArrayBoundsNode>().Single().Bounds.Single());
    }

    [TestMethod]
    public void Redim_AsClause_LandsOnTheRedimNode_NotTheMember()
    {
        const string content = """
            Public Sub Grow()
                ReDim Buffer(1 To 4) As Long
            End Sub
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        Assert.IsEmpty(member.Children.OfType<AsTypeExpressionNode>());

        var redim = member.Children.OfType<RedimDeclarationNode>().Single();
        Assert.AreEqual("Long", redim.Children.OfType<AsTypeExpressionNode>().Single().TypeName);
    }

    [TestMethod]
    public void Redim_MemberAccessTarget_CapturesQualifier()
    {
        const string content = """
            Public Sub Grow()
                ReDim Me.Buffer(3)
            End Sub
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var redim = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single()
            .Children.OfType<RedimDeclarationNode>().Single();

        Assert.AreEqual("Buffer", redim.Name);
        Assert.AreEqual("Me", redim.QualifierName);
    }

    [TestMethod]
    public void Redim_MultipleTargets_EmitOneNodeEach()
    {
        const string content = """
            Public Sub Grow()
                ReDim a(1), b(2 To 4)
            End Sub
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var redims = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single()
            .Children.OfType<RedimDeclarationNode>().ToArray();

        CollectionAssert.AreEquivalent(new[] { "a", "b" }, redims.Select(r => r.Name).ToArray());
    }

    [TestMethod]
    // an If block now has its own shape (ConditionExpression + Body), so a ReDim nested in its
    // branch parents to that branch's Body, not to the procedure member directly — SymbolBuilder is
    // the one that walks the whole body looking for locals (LanguageServer-side test coverage).
    public void Redim_NestedInABlock_ParentsToTheIfBranch()
    {
        const string content = """
            Public Sub Grow(ByVal Flag As Boolean)
                If Flag Then
                    ReDim Nested(5)
                End If
            End Sub
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var ifBlock = member.Children.OfType<IfBlockStatementNode>().Single();
        Assert.AreEqual("Nested", ifBlock.Body.Children.OfType<RedimDeclarationNode>().Single().Name);
    }

    [TestMethod]
    // a statement's condition is inside a procedure body, where the declaration pass otherwise drops
    // every expression (IsDeclarationPassExpression) until the general statement-body pass exists —
    // `booleanExpression` is the narrow carve-out that lets If/ElseIf conditions through today.
    public void IfStatement_WithoutElse_CapturesConditionAndBody()
    {
        const string content = """
            Public Sub DoWork(ByVal Flag As Boolean)
                If Flag Then
                    Dim x As Long
                End If
            End Sub
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var ifBlock = member.Children.OfType<IfBlockStatementNode>().Single();

        var condition = (SimpleNameExpressionNode)ifBlock.ConditionExpression;
        Assert.AreEqual("Flag", condition.IdentifierName);
        Assert.HasCount(1, ifBlock.Body.Children.OfType<VariableDeclarationNode>());
        Assert.IsEmpty(ifBlock.ElseIfBlocks);
        Assert.IsNull(ifBlock.ElseBlock);
    }

    [TestMethod]
    // proves the booleanExpression carve-out threads through the full operator pipeline (not just a
    // bare name): the same operator-tree machinery is now reachable from inside a procedure body.
    public void IfStatement_ConditionIsAnOperatorTree()
    {
        const string content = """
            Public Sub DoWork(ByVal N As Long)
                If N > 0 And N < 10 Then
                End If
            End Sub
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var ifBlock = member.Children.OfType<IfBlockStatementNode>().Single();

        var and = (VBBinaryOperatorExpressionNode)ifBlock.ConditionExpression;
        Assert.AreEqual(Tokens.LogicalAndOp, and.Token);
        Assert.AreEqual(Tokens.CompareGreaterThanOp, ((VBBinaryOperatorExpressionNode)and.Left).Token);
        Assert.AreEqual(Tokens.CompareLessThanOp, ((VBBinaryOperatorExpressionNode)and.Right).Token);
    }

    [TestMethod]
    // one IfBlockStatementNode models the whole chain: ElseIf branches in source order, then the
    // trailing Else — mirroring SelectCaseStatementNode's control-expression + branch-list shape.
    public void IfStatement_WithElseIfAndElse_ChainsBranchesInSourceOrder()
    {
        const string content = """
            Public Sub Classify(ByVal N As Long)
                If N = 1 Then
                    Dim a As Long
                ElseIf N = 2 Then
                    Dim b As Long
                ElseIf N = 3 Then
                    Dim c As Long
                Else
                    Dim d As Long
                End If
            End Sub
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var ifBlock = member.Children.OfType<IfBlockStatementNode>().Single();

        Assert.AreEqual("a", ifBlock.Body.Children.OfType<VariableDeclarationNode>().Single().Name);
        Assert.HasCount(2, ifBlock.ElseIfBlocks);

        var elseIf1 = ifBlock.ElseIfBlocks[0];
        Assert.AreEqual(2L, IntValue(((VBBinaryOperatorExpressionNode)elseIf1.ConditionExpression).Right));
        Assert.AreEqual("b", elseIf1.Body.Children.OfType<VariableDeclarationNode>().Single().Name);

        var elseIf2 = ifBlock.ElseIfBlocks[1];
        Assert.AreEqual(3L, IntValue(((VBBinaryOperatorExpressionNode)elseIf2.ConditionExpression).Right));
        Assert.AreEqual("c", elseIf2.Body.Children.OfType<VariableDeclarationNode>().Single().Name);

        Assert.IsNotNull(ifBlock.ElseBlock);
        Assert.AreEqual("d", ifBlock.ElseBlock!.Body.Children.OfType<VariableDeclarationNode>().Single().Name);
    }

    [TestMethod]
    // While's condition is a bare `expression` with no wrapper rule like If's booleanExpression —
    // _isCapturingLoopHeaderExpression (cleared by the loop's own EnterBlock) is what unblocks it.
    public void WhileWendStatement_CapturesConditionAndBody()
    {
        const string content = """
            Public Sub DoWork(ByVal N As Long)
                While N > 0
                    Dim x As Long
                Wend
            End Sub
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var whileWend = member.Children.OfType<WhileWendStatementNode>().Single();

        var condition = (VBBinaryOperatorExpressionNode)whileWend.ConditionExpression;
        Assert.AreEqual(Tokens.CompareGreaterThanOp, condition.Token);
        Assert.AreEqual("x", whileWend.Body.Children.OfType<VariableDeclarationNode>().Single().Name);
    }

    [TestMethod]
    // regression guard: giving While its own real scope (like If) means a declaration nested in its
    // body must still parent to that scope's Body, not flatten onto the enclosing procedure member.
    public void WhileWendStatement_NestedDeclaration_ParentsToTheLoopBody()
    {
        const string content = """
            Public Sub Grow(ByVal Flag As Boolean)
                While Flag
                    ReDim Nested(5)
                Wend
            End Sub
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var whileWend = member.Children.OfType<WhileWendStatementNode>().Single();
        Assert.AreEqual("Nested", whileWend.Body.Children.OfType<RedimDeclarationNode>().Single().Name);
    }

    [TestMethod]
    public void DoLoop_NoCondition_BuildsPlainInfiniteLoop()
    {
        const string content = """
            Public Sub DoWork()
                Do
                    Dim x As Long
                Loop
            End Sub
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var doLoop = member.Children.OfType<DoLoopStatementNode>().Single();
        Assert.AreEqual("x", doLoop.Body.Children.OfType<VariableDeclarationNode>().Single().Name);
    }

    [TestMethod]
    public void DoLoop_TopWhile_BuildsDoWhileLoopStatement()
    {
        const string content = """
            Public Sub DoWork(ByVal N As Long)
                Do While N > 0
                    Dim x As Long
                Loop
            End Sub
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var doWhile = member.Children.OfType<DoWhileLoopStatementNode>().Single();
        Assert.AreEqual(Tokens.CompareGreaterThanOp, ((VBBinaryOperatorExpressionNode)doWhile.ConditionExpression).Token);
        Assert.AreEqual("x", doWhile.Body.Children.OfType<VariableDeclarationNode>().Single().Name);
    }

    [TestMethod]
    public void DoLoop_TopUntil_BuildsDoUntilLoopStatement()
    {
        const string content = """
            Public Sub DoWork(ByVal N As Long)
                Do Until N <= 0
                    Dim x As Long
                Loop
            End Sub
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var doUntil = member.Children.OfType<DoUntilLoopStatementNode>().Single();
        Assert.AreEqual(Tokens.CompareLessThanOrEqualOp, ((VBBinaryOperatorExpressionNode)doUntil.ConditionExpression).Token);
    }

    [TestMethod]
    // the bottom-condition forms exercise CaptureIsolatedExpression, not the live-window trick While
    // uses — this is the one that most needs an operator-tree condition proving the re-walk threads
    // through the full PopLastChildren pipeline, not just a bare comparison.
    public void DoLoop_BottomWhile_BuildsDoLoopWhileStatementWithOperatorTreeCondition()
    {
        const string content = """
            Public Sub DoWork(ByVal N As Long)
                Do
                    Dim x As Long
                Loop While N > 0 And N < 10
            End Sub
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var doLoopWhile = member.Children.OfType<DoLoopWhileStatementNode>().Single();

        var and = (VBBinaryOperatorExpressionNode)doLoopWhile.ConditionExpression;
        Assert.AreEqual(Tokens.LogicalAndOp, and.Token);
        Assert.AreEqual(Tokens.CompareGreaterThanOp, ((VBBinaryOperatorExpressionNode)and.Left).Token);
        Assert.AreEqual(Tokens.CompareLessThanOp, ((VBBinaryOperatorExpressionNode)and.Right).Token);
        Assert.AreEqual("x", doLoopWhile.Body.Children.OfType<VariableDeclarationNode>().Single().Name);
    }

    [TestMethod]
    public void DoLoop_BottomUntil_BuildsDoLoopUntilStatement()
    {
        const string content = """
            Public Sub DoWork(ByVal N As Long)
                Do
                    Dim x As Long
                Loop Until N <= 0
            End Sub
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var doLoopUntil = member.Children.OfType<DoLoopUntilStatementNode>().Single();
        Assert.AreEqual(Tokens.CompareLessThanOrEqualOp, ((VBBinaryOperatorExpressionNode)doLoopUntil.ConditionExpression).Token);
    }

    [TestMethod]
    // regression guard, same shape as If/While: giving Do its own real scope means a declaration
    // nested in its body must still parent to that scope's Body, not flatten onto the member.
    public void DoLoop_NestedDeclaration_ParentsToTheLoopBody()
    {
        const string content = """
            Public Sub Grow(ByVal Flag As Boolean)
                Do While Flag
                    ReDim Nested(5)
                Loop
            End Sub
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var doWhile = member.Children.OfType<DoWhileLoopStatementNode>().Single();
        Assert.AreEqual("Nested", doWhile.Body.Children.OfType<RedimDeclarationNode>().Single().Name);
    }

    [TestMethod]
    // the control-variable `i = 1` is parsed as one expression (grammar comment: "expression EQ
    // expression refactored to expression to allow SLL") — BuildForStatement must split it back into
    // Control/Start off the top-level `=` operator node.
    public void ForNextStatement_CapturesControlStartEndAndStep()
    {
        const string content = """
            Public Sub DoWork(ByVal N As Long)
                For i = 1 To N Step 2
                    Dim x As Long
                Next i
            End Sub
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var forStatement = member.Children.OfType<ForStatementNode>().Single();

        Assert.AreEqual("i", ((SimpleNameExpressionNode)forStatement.ControlExpression).IdentifierName);
        Assert.AreEqual(1L, IntValue(forStatement.StartExpression));
        Assert.AreEqual("N", ((SimpleNameExpressionNode)forStatement.EndExpression).IdentifierName);
        Assert.AreEqual(2L, IntValue(forStatement.StepExpression!));
        Assert.AreEqual("x", forStatement.Body.Children.OfType<VariableDeclarationNode>().Single().Name);
    }

    [TestMethod]
    // no Step clause: MS-VBAL's implicit default of 1 is a runtime concern, not the parser's — the
    // node must leave this null rather than synthesize a fake literal with no real source location.
    public void ForNextStatement_WithoutStep_LeavesStepExpressionNull()
    {
        const string content = """
            Public Sub DoWork(ByVal N As Long)
                For i = 1 To N
                Next i
            End Sub
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var forStatement = member.Children.OfType<ForStatementNode>().Single();
        Assert.IsNull(forStatement.StepExpression);
    }

    [TestMethod]
    // proves CaptureIsolatedExpression threads a full operator tree, not just a bare name/literal.
    public void ForNextStatement_EndExpressionIsAnOperatorTree()
    {
        const string content = """
            Public Sub DoWork(ByVal N As Long)
                For i = 1 To N * 2
                Next i
            End Sub
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var forStatement = member.Children.OfType<ForStatementNode>().Single();
        Assert.AreEqual(Tokens.MultiplicationOp, ((VBBinaryOperatorExpressionNode)forStatement.EndExpression).Token);
    }

    [TestMethod]
    public void ForEachStatement_CapturesControlAndCollection()
    {
        const string content = """
            Public Sub DoWork(ByVal Items As Variant)
                For Each Item In Items
                    Dim x As Long
                Next Item
            End Sub
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var forEach = member.Children.OfType<ForEachStatementNode>().Single();

        Assert.AreEqual("Item", ((SimpleNameExpressionNode)forEach.ControlExpression).IdentifierName);
        Assert.AreEqual("Items", ((SimpleNameExpressionNode)forEach.CollectionExpression).IdentifierName);
        Assert.AreEqual("x", forEach.Body.Children.OfType<VariableDeclarationNode>().Single().Name);
    }

    [TestMethod]
    // regression guard, same shape as If/While/Do: a declaration nested in a For body must still
    // parent to that loop's own Body, not flatten onto the enclosing procedure member.
    public void ForNextStatement_NestedDeclaration_ParentsToTheLoopBody()
    {
        const string content = """
            Public Sub Grow()
                For i = 1 To 5
                    ReDim Nested(5)
                Next i
            End Sub
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var forStatement = member.Children.OfType<ForStatementNode>().Single();
        Assert.AreEqual("Nested", forStatement.Body.Children.OfType<RedimDeclarationNode>().Single().Name);
    }

    [TestMethod]
    public void SelectCase_ValueRangeClause_BuildsCaseValueRangeClauseNode()
    {
        const string content = """
            Public Sub Classify(ByVal N As Long)
                Select Case N
                Case 5
                    Dim x As Long
                End Select
            End Sub
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var selectCase = member.Children.OfType<SelectCaseStatementNode>().Single();

        Assert.AreEqual("N", ((SimpleNameExpressionNode)selectCase.ControlExpression).IdentifierName);
        Assert.HasCount(1, selectCase.CaseExpressionBlocks);
        var clause = (CaseValueRangeClauseNode)selectCase.CaseExpressionBlocks[0].RangeClauses.Single();
        Assert.AreEqual(5L, IntValue(clause.Value));
        Assert.AreEqual("x", selectCase.CaseExpressionBlocks[0].Block.Children.OfType<VariableDeclarationNode>().Single().Name);
        Assert.IsNull(selectCase.CaseElseBlock);
    }

    [TestMethod]
    public void SelectCase_ComparisonRangeClause_BuildsCaseComparisonRangeClauseNode()
    {
        const string content = """
            Public Sub Classify(ByVal N As Long)
                Select Case N
                Case Is > 5
                End Select
            End Sub
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var selectCase = member.Children.OfType<SelectCaseStatementNode>().Single();
        var clause = (CaseComparisonRangeClauseNode)selectCase.CaseExpressionBlocks[0].RangeClauses.Single();

        Assert.AreEqual(Tokens.CompareGreaterThanOp, clause.ComparisonOperator);
        Assert.AreEqual(5L, IntValue(clause.Value));
    }

    [TestMethod]
    public void SelectCase_ToRangeClause_BuildsCaseToRangeClauseNode()
    {
        const string content = """
            Public Sub Classify(ByVal N As Long)
                Select Case N
                Case 1 To 10
                End Select
            End Sub
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var selectCase = member.Children.OfType<SelectCaseStatementNode>().Single();
        var clause = (CaseToRangeClauseNode)selectCase.CaseExpressionBlocks[0].RangeClauses.Single();

        Assert.AreEqual(1L, IntValue(clause.Start));
        Assert.AreEqual(10L, IntValue(clause.End));
    }

    [TestMethod]
    // a single Case line can carry several comma-separated range clauses, each independently one of
    // the three shapes — proves CaptureRangeClause's per-clause id allocation doesn't collide.
    public void SelectCase_MultipleRangeClausesOnOneLine_AreCapturedInOrder()
    {
        const string content = """
            Public Sub Classify(ByVal N As Long)
                Select Case N
                Case 1, 3, 5 To 10, Is > 100
                End Select
            End Sub
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var selectCase = member.Children.OfType<SelectCaseStatementNode>().Single();
        var clauses = selectCase.CaseExpressionBlocks[0].RangeClauses;

        Assert.HasCount(4, clauses);
        Assert.AreEqual(1L, IntValue(((CaseValueRangeClauseNode)clauses[0]).Value));
        Assert.AreEqual(3L, IntValue(((CaseValueRangeClauseNode)clauses[1]).Value));
        var range = (CaseToRangeClauseNode)clauses[2];
        Assert.AreEqual(5L, IntValue(range.Start));
        Assert.AreEqual(10L, IntValue(range.End));
        Assert.AreEqual(Tokens.CompareGreaterThanOp, ((CaseComparisonRangeClauseNode)clauses[3]).ComparisonOperator);
    }

    [TestMethod]
    public void SelectCase_WithCaseElse_BuildsCaseElseBlock()
    {
        const string content = """
            Public Sub Classify(ByVal N As Long)
                Select Case N
                Case 1
                    Dim a As Long
                Case Else
                    Dim b As Long
                End Select
            End Sub
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var selectCase = member.Children.OfType<SelectCaseStatementNode>().Single();

        Assert.IsNotNull(selectCase.CaseElseBlock);
        Assert.AreEqual("b", selectCase.CaseElseBlock!.Body.Children.OfType<VariableDeclarationNode>().Single().Name);
    }

    [TestMethod]
    // proves CaptureIsolatedExpression threads a full operator tree for the control expression too.
    public void SelectCase_ControlExpressionIsAnOperatorTree()
    {
        const string content = """
            Public Sub Classify(ByVal N As Long)
                Select Case N + 1
                Case 1
                End Select
            End Sub
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var selectCase = member.Children.OfType<SelectCaseStatementNode>().Single();
        Assert.AreEqual(Tokens.AdditionOp, ((VBBinaryOperatorExpressionNode)selectCase.ControlExpression).Token);
    }

    [TestMethod]
    // regression guard, same shape as every other construct wired in this PR.
    public void SelectCase_NestedDeclaration_ParentsToTheCaseBody()
    {
        const string content = """
            Public Sub Grow(ByVal N As Long)
                Select Case N
                Case 1
                    ReDim Nested(5)
                End Select
            End Sub
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var selectCase = member.Children.OfType<SelectCaseStatementNode>().Single();
        Assert.AreEqual("Nested", selectCase.CaseExpressionBlocks[0].Block.Children.OfType<RedimDeclarationNode>().Single().Name);
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

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
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

    // a numeric literal resolves to the smallest intrinsic type that fits (MS-VBAL 3.3.2); test
    // sources here are small enough to land as Integer, but keep this tolerant of Long too.
    private static long IntValue(SyntaxNode node) => ((LiteralExpressionNode)node).StaticValue switch
    {
        VBIntegerValue v => v.Value,
        VBLongValue v => v.Value,
        var other => throw new AssertFailedException($"unexpected value type {other.GetType().Name}"),
    };

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
