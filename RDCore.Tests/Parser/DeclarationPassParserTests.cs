using RDCore.Parsing;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
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
    public void MemberAccessExpression_BuildsOwnerAndMember()
    {
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), "Public Const N = Foo.Bar");
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);

        var access = Descendants(result.SyntaxTree!).OfType<MemberAccessExpressionNode>().Single();
        Assert.AreEqual("Foo", ((SimpleNameExpressionNode)access.Owner!).IdentifierName);
        Assert.AreEqual("Bar", access.Member.IdentifierName);
    }

    [TestMethod]
    // proves the left-recursive PopLastChildren technique chains correctly: the owner of the outer
    // access (`.Baz`) is itself a MemberAccessExpressionNode (`Foo.Bar`), not a bare name.
    public void MemberAccessExpression_ChainsMultipleLevels()
    {
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), "Public Const N = Foo.Bar.Baz");
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);

        var outer = Descendants(result.SyntaxTree!).OfType<MemberAccessExpressionNode>().Single(a => a.Member.IdentifierName == "Baz");
        var inner = (MemberAccessExpressionNode)outer.Owner!;
        Assert.AreEqual("Foo", ((SimpleNameExpressionNode)inner.Owner!).IdentifierName);
        Assert.AreEqual("Bar", inner.Member.IdentifierName);
    }

    [TestMethod]
    // the parser doesn't police that a leading-dot member access is only legal inside a With block
    // (that's a downstream compile-error concern) - it parses fine standalone too.
    public void WithMemberAccessExpression_BuildsWithNullOwner()
    {
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), "Public Const N = .Bar");
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);

        var access = Descendants(result.SyntaxTree!).OfType<MemberAccessExpressionNode>().Single();
        Assert.IsNull(access.Owner);
        Assert.AreEqual("Bar", access.Member.IdentifierName);
    }

    [TestMethod]
    public void DictionaryAccessExpression_BuildsOwnerAndMember()
    {
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), "Public Const N = Foo!Bar");
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);

        var access = Descendants(result.SyntaxTree!).OfType<DictionaryAccessExpressionNode>().Single();
        Assert.AreEqual("Foo", ((SimpleNameExpressionNode)access.Owner!).IdentifierName);
        Assert.AreEqual("Bar", access.Member.IdentifierName);
    }

    [TestMethod]
    public void WithDictionaryAccessExpression_BuildsWithNullOwner()
    {
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), "Public Const N = !Bar");
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);

        var access = Descendants(result.SyntaxTree!).OfType<DictionaryAccessExpressionNode>().Single();
        Assert.IsNull(access.Owner);
        Assert.AreEqual("Bar", access.Member.IdentifierName);
    }

    [TestMethod]
    public void InstanceExpression_BuildsMeReference()
    {
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), "Public Const N = Me");
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);

        Assert.HasCount(1, Descendants(result.SyntaxTree!).OfType<InstanceExpressionNode>());
    }

    [TestMethod]
    public void IndexExpression_BuildsCalleeAndPositionalArguments()
    {
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), "Public Const N = Foo(1, 2)");
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);

        var index = Descendants(result.SyntaxTree!).OfType<IndexExpressionNode>().Single();
        Assert.AreEqual("Foo", ((SimpleNameExpressionNode)index.Callee).IdentifierName);
        Assert.HasCount(2, index.Arguments);
        Assert.AreEqual(1L, IntValue(index.Arguments[0]));
        Assert.AreEqual(2L, IntValue(index.Arguments[1]));
    }

    [TestMethod]
    // the callee of an index expression can itself be built by a different lExpression alternative.
    public void IndexExpression_CalleeCanBeAMemberAccess()
    {
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), "Public Const N = Foo.Bar(1)");
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);

        var index = Descendants(result.SyntaxTree!).OfType<IndexExpressionNode>().Single();
        var callee = (MemberAccessExpressionNode)index.Callee;
        Assert.AreEqual("Foo", ((SimpleNameExpressionNode)callee.Owner!).IdentifierName);
        Assert.AreEqual("Bar", callee.Member.IdentifierName);
        Assert.AreEqual(1L, IntValue(index.Arguments.Single()));
    }

    [TestMethod]
    // a skipped argument position (`Foo(1, , 3)`) must not collapse the array - MissingArgumentNode
    // preserves the position for later parameter binding.
    public void IndexExpression_WithMissingArgument_PreservesPosition()
    {
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), "Public Const N = Foo(1, , 3)");
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);

        var index = Descendants(result.SyntaxTree!).OfType<IndexExpressionNode>().Single();
        Assert.HasCount(3, index.Arguments);
        Assert.AreEqual(1L, IntValue(index.Arguments[0]));
        Assert.IsInstanceOfType<MissingArgumentNode>(index.Arguments[1]);
        Assert.AreEqual(3L, IntValue(index.Arguments[2]));
    }

    [TestMethod]
    public void IndexExpression_WithNamedArgument_CapturesNameAndValue()
    {
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), "Public Const N = Foo(Bar:=5)");
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);

        var index = Descendants(result.SyntaxTree!).OfType<IndexExpressionNode>().Single();
        var named = (NamedArgumentNode)index.Arguments.Single();
        Assert.AreEqual("Bar", named.Name);
        Assert.AreEqual(5L, IntValue(named.Value));
    }

    [TestMethod]
    public void IndexExpression_WithAddressOfArgument_CapturesTarget()
    {
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), "Public Const N = Foo(AddressOf Bar)");
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.IsEmpty ? "" : result.SyntaxErrors[0].Description);

        var index = Descendants(result.SyntaxTree!).OfType<IndexExpressionNode>().Single();
        var addressOf = (AddressOfExpressionNode)index.Arguments.Single();
        Assert.AreEqual("Bar", ((SimpleNameExpressionNode)addressOf.Target).IdentifierName);
    }

    [TestMethod]
    // proves the whole family cooperates with CaptureIsolatedExpression - a body-level condition
    // expression (not module-scope, where capture is unconditional) can build a full lExpression.
    public void IfStatement_ConditionCanBeAnIndexExpressionOnAMemberAccess()
    {
        const string content = """
            Public Sub DoWork()
                If Foo.Bar(1) Then
                End If
            End Sub
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var ifBlock = member.Children.OfType<IfBlockStatementNode>().Single();
        var index = (IndexExpressionNode)ifBlock.ConditionExpression;
        var callee = (MemberAccessExpressionNode)index.Callee;
        Assert.AreEqual("Foo", ((SimpleNameExpressionNode)callee.Owner!).IdentifierName);
        Assert.AreEqual("Bar", callee.Member.IdentifierName);
        Assert.AreEqual(1L, IntValue(index.Arguments.Single()));
    }

    [TestMethod]
    // proves the same cooperation for a KeywordStatement's argument capture, retroactively unlocked
    // by this same family (Erase's targets were bare names only until now).
    public void EraseStatement_TargetCanBeAMemberAccess()
    {
        var result = ParseInProcedure("Erase Foo.Bar");
        var statement = KeywordStatement(result, Tokens.Erase);

        var access = (MemberAccessExpressionNode)statement.Inputs.Single();
        Assert.AreEqual("Foo", ((SimpleNameExpressionNode)access.Owner!).IdentifierName);
        Assert.AreEqual("Bar", access.Member.IdentifierName);
    }

    [TestMethod]
    // `Call Foo(1, 2)` carries its arguments inside the callee's own IndexExpressionNode - the
    // statement's own Arguments stays empty.
    public void CallStatement_Explicit_CarriesArgumentsOnTheCallee()
    {
        var result = ParseInProcedure("Call Foo(1, 2)");

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var call = member.Children.OfType<CallStatementNode>().Single();

        Assert.IsTrue(call.IsExplicitCall);
        Assert.IsEmpty(call.Arguments);
        var index = (IndexExpressionNode)call.Callee;
        Assert.AreEqual("Foo", ((SimpleNameExpressionNode)index.Callee).IdentifierName);
        Assert.HasCount(2, index.Arguments);
        Assert.AreEqual(1L, IntValue(index.Arguments[0]));
        Assert.AreEqual(2L, IntValue(index.Arguments[1]));
    }

    [TestMethod]
    // the bare, unparenthesized form (`Foo 1, 2`) has no `Call` keyword and no lExpression-embedded
    // argument list - its arguments are the statement's own, separate CallStatementNode.Arguments.
    public void CallStatement_Bare_CarriesItsOwnArguments()
    {
        var result = ParseInProcedure("Foo 1, 2");

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var call = member.Children.OfType<CallStatementNode>().Single();

        Assert.IsFalse(call.IsExplicitCall);
        Assert.AreEqual("Foo", ((SimpleNameExpressionNode)call.Callee).IdentifierName);
        Assert.HasCount(2, call.Arguments);
        Assert.AreEqual(1L, IntValue(call.Arguments[0]));
        Assert.AreEqual(2L, IntValue(call.Arguments[1]));
    }

    [TestMethod]
    public void CallStatement_BareWithNoArguments_HasEmptyArgumentsAndCallee()
    {
        var result = ParseInProcedure("Foo");

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var call = member.Children.OfType<CallStatementNode>().Single();

        Assert.IsFalse(call.IsExplicitCall);
        Assert.AreEqual("Foo", ((SimpleNameExpressionNode)call.Callee).IdentifierName);
        Assert.IsEmpty(call.Arguments);
    }

    [TestMethod]
    // a bare call's callee can itself be a member access (`Debug.Assert(x)`'s shape, minus parens).
    public void CallStatement_Bare_CalleeCanBeAMemberAccess()
    {
        var result = ParseInProcedure("Foo.Bar 1");

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var call = member.Children.OfType<CallStatementNode>().Single();

        var callee = (MemberAccessExpressionNode)call.Callee;
        Assert.AreEqual("Foo", ((SimpleNameExpressionNode)callee.Owner!).IdentifierName);
        Assert.AreEqual("Bar", callee.Member.IdentifierName);
        Assert.AreEqual(1L, IntValue(call.Arguments.Single()));
    }

    [TestMethod]
    // Let and Set (MS-VBAL §5.4.3.8/9) share one node shape - the bare form omits the optional `Let`.
    public void LetStatement_Bare_IsImplicitLet()
    {
        var result = ParseInProcedure("x = 1");

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var assignment = member.Children.OfType<AssignmentStatementNode>().Single();

        Assert.AreEqual(AssignmentKind.ImplicitLet, assignment.Kind);
        Assert.AreEqual("x", ((SimpleNameExpressionNode)assignment.Target).IdentifierName);
        Assert.AreEqual(1L, IntValue(assignment.Value));
    }

    [TestMethod]
    public void LetStatement_Explicit_IsExplicitLet()
    {
        var result = ParseInProcedure("Let x = 1");

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var assignment = member.Children.OfType<AssignmentStatementNode>().Single();

        Assert.AreEqual(AssignmentKind.ExplicitLet, assignment.Kind);
    }

    [TestMethod]
    public void SetStatement_IsSetKind()
    {
        var result = ParseInProcedure("Set x = Foo");

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var assignment = member.Children.OfType<AssignmentStatementNode>().Single();

        Assert.AreEqual(AssignmentKind.Set, assignment.Kind);
        Assert.AreEqual("x", ((SimpleNameExpressionNode)assignment.Target).IdentifierName);
        Assert.AreEqual("Foo", ((SimpleNameExpressionNode)assignment.Value).IdentifierName);
    }

    [TestMethod]
    // the lExpression target can be any of its own family - a member access here, not just a bare name.
    public void LetStatement_TargetCanBeAMemberAccess()
    {
        var result = ParseInProcedure("Foo.Bar = 1");

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var assignment = member.Children.OfType<AssignmentStatementNode>().Single();

        var target = (MemberAccessExpressionNode)assignment.Target;
        Assert.AreEqual("Foo", ((SimpleNameExpressionNode)target.Owner!).IdentifierName);
        Assert.AreEqual("Bar", target.Member.IdentifierName);
        Assert.AreEqual(1L, IntValue(assignment.Value));
    }

    [TestMethod]
    public void SetStatement_TargetCanBeAnIndexExpression()
    {
        var result = ParseInProcedure("Set arr(1) = Foo");

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var assignment = member.Children.OfType<AssignmentStatementNode>().Single();

        var target = (IndexExpressionNode)assignment.Target;
        Assert.AreEqual("arr", ((SimpleNameExpressionNode)target.Callee).IdentifierName);
        Assert.AreEqual(1L, IntValue(target.Arguments.Single()));
    }

    [TestMethod]
    // LSet/RSet (MS-VBAL §5.4.3.6/7) reuse AssignmentStatementNode too - same `keyword target =
    // expression` shape as Let/Set, just with string-fixing coercion semantics.
    public void LSetStatement_IsLSetKind()
    {
        var result = ParseInProcedure("LSet x = \"foo\"");

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var assignment = member.Children.OfType<AssignmentStatementNode>().Single();

        Assert.AreEqual(AssignmentKind.LSet, assignment.Kind);
        Assert.AreEqual("x", ((SimpleNameExpressionNode)assignment.Target).IdentifierName);
    }

    [TestMethod]
    public void RSetStatement_IsRSetKind()
    {
        var result = ParseInProcedure("RSet x = \"foo\"");

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var assignment = member.Children.OfType<AssignmentStatementNode>().Single();

        Assert.AreEqual(AssignmentKind.RSet, assignment.Kind);
        Assert.AreEqual("x", ((SimpleNameExpressionNode)assignment.Target).IdentifierName);
    }

    [TestMethod]
    public void MidStatement_WithoutLength_CapturesTargetStartAndValue()
    {
        var result = ParseInProcedure("Mid(x, 1) = \"foo\"");

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var mid = member.Children.OfType<MidStatementNode>().Single();

        Assert.IsFalse(mid.IsByteMode);
        Assert.AreEqual("x", ((SimpleNameExpressionNode)mid.Target).IdentifierName);
        Assert.AreEqual(1L, IntValue(mid.Start));
        Assert.IsNull(mid.Length);
    }

    [TestMethod]
    public void MidStatement_WithLength_CapturesLength()
    {
        var result = ParseInProcedure("Mid(x, 1, 3) = \"foo\"");

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var mid = member.Children.OfType<MidStatementNode>().Single();

        Assert.IsNotNull(mid.Length);
        Assert.AreEqual(3L, IntValue(mid.Length!));
    }

    [TestMethod]
    public void MidBStatement_IsByteMode()
    {
        var result = ParseInProcedure("MidB(x, 1) = \"foo\"");

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var mid = member.Children.OfType<MidStatementNode>().Single();

        Assert.IsTrue(mid.IsByteMode);
    }

    [TestMethod]
    public void MidDollarStatement_ParsesTheSameAsMid()
        // Mid$/MidB$ only differ from Mid/MidB in their return type as a function; as a statement,
        // MS-VBAL's own runtime semantics only ever distinguish Mid/Mid$ from MidB/MidB$.
    {
        var result = ParseInProcedure("Mid$(x, 1) = \"foo\"");

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var mid = member.Children.OfType<MidStatementNode>().Single();

        Assert.IsFalse(mid.IsByteMode);
    }

    [TestMethod]
    public void GoToStatement_CapturesLabelExpression()
    {
        var result = ParseInProcedure("GoTo Label1");

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var goTo = member.Children.OfType<GoToStatementNode>().Single();

        Assert.AreEqual("Label1", ((SimpleNameExpressionNode)goTo.LabelExpression).IdentifierName);
    }

    [TestMethod]
    public void GoSubStatement_CapturesLabelExpression()
    {
        var result = ParseInProcedure("GoSub Label1");

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var goSub = member.Children.OfType<GoSubStatementNode>().Single();

        Assert.AreEqual("Label1", ((SimpleNameExpressionNode)goSub.LabelExpression).IdentifierName);
    }

    [TestMethod]
    public void ReturnStatement_Parses()
    {
        var result = ParseInProcedure("Return");

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        Assert.HasCount(1, member.Children.OfType<ReturnStatementNode>());
    }

    [TestMethod]
    public void OnErrorGoToStatement_CapturesLabelExpression()
    {
        var result = ParseInProcedure("On Error GoTo Handler");

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var onError = member.Children.OfType<OnErrorGoToStatementNode>().Single();

        Assert.AreEqual("Handler", ((SimpleNameExpressionNode)onError.LabelExpression).IdentifierName);
    }

    [TestMethod]
    public void OnErrorGoToZero_IsStillAGoToNotAResumeNode()
    {
        var result = ParseInProcedure("On Error GoTo 0");

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var onError = member.Children.OfType<OnErrorGoToStatementNode>().Single();

        Assert.AreEqual(0L, IntValue(onError.LabelExpression));
    }

    [TestMethod]
    public void OnErrorResumeNextStatement_Parses()
    {
        var result = ParseInProcedure("On Error Resume Next");

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        Assert.HasCount(1, member.Children.OfType<OnErrorResumeStatementNode>());
    }

    [TestMethod]
    public void ResumeStatement_Bare_HasNullLabelExpression()
    {
        var result = ParseInProcedure("Resume");

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var resume = member.Children.OfType<ResumeStatementNode>().Single();

        Assert.IsNull(resume.LabelExpression);
    }

    [TestMethod]
    public void ResumeStatement_WithLabel_CapturesLabelExpression()
    {
        var result = ParseInProcedure("Resume Handler");

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var resume = member.Children.OfType<ResumeStatementNode>().Single();

        Assert.AreEqual("Handler", ((SimpleNameExpressionNode)resume.LabelExpression!).IdentifierName);
    }

    [TestMethod]
    // `Resume Next` is its own dedicated node, not ResumeStatementNode with a "Next" label.
    public void ResumeNextStatement_IsItsOwnNodeNotAResumeWithLabel()
    {
        var result = ParseInProcedure("Resume Next");

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        Assert.HasCount(1, member.Children.OfType<ResumeNextStatementNode>());
        Assert.IsEmpty(member.Children.OfType<ResumeStatementNode>());
    }

    [TestMethod]
    public void ErrorStatement_CapturesNumberExpression()
    {
        var result = ParseInProcedure("Error 5");

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var error = member.Children.OfType<ErrorStatementNode>().Single();

        Assert.AreEqual(5L, IntValue(error.NumberExpression));
    }

    [TestMethod]
    public void SingleLineIf_WithThenOnly_CapturesConditionAndThenBody()
    {
        var result = ParseInProcedure("If x Then y = 1");

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var ifStmt = member.Children.OfType<InlineIfStatementNode>().Single();

        Assert.AreEqual("x", ((SimpleNameExpressionNode)ifStmt.ConditionExpression).IdentifierName);
        var assignment = (AssignmentStatementNode)ifStmt.ThenBody.Children.Single();
        Assert.AreEqual("y", ((SimpleNameExpressionNode)assignment.Target).IdentifierName);
        Assert.IsNull(ifStmt.ElseBody);
    }

    [TestMethod]
    public void SingleLineIf_WithElse_CapturesBothBodies()
    {
        var result = ParseInProcedure("If x Then y = 1 Else y = 2");

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var ifStmt = member.Children.OfType<InlineIfStatementNode>().Single();

        Assert.HasCount(1, ifStmt.ThenBody.Children);
        Assert.IsNotNull(ifStmt.ElseBody);
        Assert.HasCount(1, ifStmt.ElseBody!.Children);
    }

    [TestMethod]
    public void SingleLineIf_MultipleColonSeparatedStatements_AreAllCaptured()
    {
        var result = ParseInProcedure("If x Then y = 1 : z = 2");

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var ifStmt = member.Children.OfType<InlineIfStatementNode>().Single();

        Assert.HasCount(2, ifStmt.ThenBody.Children);
    }

    [TestMethod]
    // MS-VBAL: a bare line-number target in a single-line If's Then/Else branch has the effect of a
    // GoTo statement targeting that line - synthesized directly rather than modeled as its own shape.
    public void SingleLineIf_BareLineNumberTarget_SynthesizesGoToStatement()
    {
        var result = ParseInProcedure("If x Then 100");

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var ifStmt = member.Children.OfType<InlineIfStatementNode>().Single();

        var goTo = (GoToStatementNode)ifStmt.ThenBody.Children.Single();
        Assert.AreEqual(100L, IntValue(goTo.LabelExpression));
    }

    [TestMethod]
    public void SingleLineIf_BareLineNumberTargetInElseBranch_SynthesizesGoToStatement()
    {
        var result = ParseInProcedure("If x Then y = 1 Else 200");

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var ifStmt = member.Children.OfType<InlineIfStatementNode>().Single();

        var goTo = (GoToStatementNode)ifStmt.ElseBody!.Children.Single();
        Assert.AreEqual(200L, IntValue(goTo.LabelExpression));
    }

    [TestMethod]
    public void SingleLineIf_EmptyThen_HasEmptyThenBodyAndRequiresElse()
    {
        var result = ParseInProcedure("If x Then : Else y = 1");

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var ifStmt = member.Children.OfType<InlineIfStatementNode>().Single();

        Assert.IsEmpty(ifStmt.ThenBody.Children);
        Assert.IsNotNull(ifStmt.ElseBody);
        Assert.HasCount(1, ifStmt.ElseBody!.Children);
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
    public void EraseStatement_CapturesTargetExpressions()
    {
        var result = ParseInProcedure("Erase a, b");
        var statement = KeywordStatement(result, Tokens.Erase);

        Assert.HasCount(2, statement.Inputs);
        Assert.AreEqual("a", ((SimpleNameExpressionNode)statement.Inputs[0]).IdentifierName);
        Assert.AreEqual("b", ((SimpleNameExpressionNode)statement.Inputs[1]).IdentifierName);
    }

    [TestMethod]
    public void NameStatement_CapturesOldAndNewPath()
    {
        var result = ParseInProcedure("""Name "a.txt" As "b.txt" """.TrimEnd());
        var statement = KeywordStatement(result, Tokens.Name);

        Assert.HasCount(2, statement.Inputs);
        Assert.AreEqual("a.txt", ((VBStringValue)((LiteralExpressionNode)statement.Inputs[0]).StaticValue).Value);
        Assert.AreEqual("b.txt", ((VBStringValue)((LiteralExpressionNode)statement.Inputs[1]).StaticValue).Value);
    }

    [TestMethod]
    // the event name is a bare identifier (not an expression) - synthesized as the first Input.
    public void RaiseEventStatement_CapturesEventNameAndArguments()
    {
        var result = ParseInProcedure("RaiseEvent Changed(1, 2)");
        var statement = KeywordStatement(result, Tokens.RaiseEvent);

        Assert.HasCount(3, statement.Inputs);
        Assert.AreEqual("Changed", ((SimpleNameExpressionNode)statement.Inputs[0]).IdentifierName);
        Assert.AreEqual(1L, IntValue(statement.Inputs[1]));
        Assert.AreEqual(2L, IntValue(statement.Inputs[2]));
    }

    [TestMethod]
    public void RaiseEventStatement_WithNoArguments_CapturesJustTheEventName()
    {
        var result = ParseInProcedure("RaiseEvent Changed");
        var statement = KeywordStatement(result, Tokens.RaiseEvent);

        Assert.HasCount(1, statement.Inputs);
        Assert.AreEqual("Changed", ((SimpleNameExpressionNode)statement.Inputs[0]).IdentifierName);
    }

    [TestMethod]
    public void CloseStatement_WithFileNumbers_CapturesThem()
    {
        var result = ParseInProcedure("Close #1, #2");
        var statement = KeywordStatement(result, Tokens.Close);

        Assert.HasCount(2, statement.Inputs);
        Assert.AreEqual(1L, IntValue(statement.Inputs[0]));
        Assert.AreEqual(2L, IntValue(statement.Inputs[1]));
    }

    [TestMethod]
    public void CloseStatement_WithNoFileNumbers_HasNoInputs()
    {
        var result = ParseInProcedure("Close");
        Assert.IsEmpty(KeywordStatement(result, Tokens.Close).Inputs);
    }

    [TestMethod]
    public void ResetStatement_HasNoInputs()
    {
        var result = ParseInProcedure("Reset");
        Assert.IsEmpty(KeywordStatement(result, Tokens.Reset).Inputs);
    }

    [TestMethod]
    public void SeekStatement_CapturesFileNumberAndPosition()
    {
        var result = ParseInProcedure("Seek #1, 5");
        var statement = KeywordStatement(result, Tokens.Seek);

        Assert.HasCount(2, statement.Inputs);
        Assert.AreEqual(1L, IntValue(statement.Inputs[0]));
        Assert.AreEqual(5L, IntValue(statement.Inputs[1]));
    }

    [TestMethod]
    public void LockStatement_WithJustAStartRecord_CapturesOneRecordNumber()
    {
        var result = ParseInProcedure("Lock #1, 5");
        var statement = KeywordStatement(result, Tokens.Lock);

        Assert.HasCount(2, statement.Inputs);
        Assert.AreEqual(5L, IntValue(statement.Inputs[1]));
    }

    [TestMethod]
    public void LockStatement_WithARecordRange_CapturesBothBounds()
    {
        var result = ParseInProcedure("Lock #1, 5 To 10");
        var statement = KeywordStatement(result, Tokens.Lock);

        Assert.HasCount(3, statement.Inputs);
        Assert.AreEqual(5L, IntValue(statement.Inputs[1]));
        Assert.AreEqual(10L, IntValue(statement.Inputs[2]));
    }

    [TestMethod]
    public void UnlockStatement_CapturesFileNumberAndRecordRange()
    {
        var result = ParseInProcedure("Unlock #1, 5 To 10");
        var statement = KeywordStatement(result, Tokens.Unlock);
        Assert.HasCount(3, statement.Inputs);
    }

    [TestMethod]
    public void GetStatement_CapturesFileNumberRecordNumberAndVariable()
    {
        var result = ParseInProcedure("Get #1, 5, x");
        var statement = KeywordStatement(result, Tokens.Get);

        Assert.HasCount(3, statement.Inputs);
        Assert.AreEqual(1L, IntValue(statement.Inputs[0]));
        Assert.AreEqual(5L, IntValue(statement.Inputs[1]));
        Assert.AreEqual("x", ((SimpleNameExpressionNode)statement.Inputs[2]).IdentifierName);
    }

    [TestMethod]
    public void PutStatement_CapturesFileNumberRecordNumberAndData()
    {
        var result = ParseInProcedure("Put #1, 5, x");
        var statement = KeywordStatement(result, Tokens.Put);

        Assert.HasCount(3, statement.Inputs);
        Assert.AreEqual("x", ((SimpleNameExpressionNode)statement.Inputs[2]).IdentifierName);
    }

    [TestMethod]
    public void LineInputStatement_CapturesFileNumberAndVariable()
    {
        var result = ParseInProcedure("Line Input #1, x");
        var statement = KeywordStatement(result, Tokens.LineInput);

        Assert.HasCount(2, statement.Inputs);
        Assert.AreEqual(1L, IntValue(statement.Inputs[0]));
        Assert.AreEqual("x", ((SimpleNameExpressionNode)statement.Inputs[1]).IdentifierName);
    }

    [TestMethod]
    public void WidthStatement_CapturesFileNumberAndWidth()
    {
        var result = ParseInProcedure("Width #1, 80");
        var statement = KeywordStatement(result, Tokens.Width);

        Assert.HasCount(2, statement.Inputs);
        Assert.AreEqual(80L, IntValue(statement.Inputs[1]));
    }

    [TestMethod]
    public void InputStatement_CapturesFileNumberAndVariables()
    {
        var result = ParseInProcedure("Input #1, a, b");
        var statement = KeywordStatement(result, Tokens.Input);

        Assert.HasCount(3, statement.Inputs);
        Assert.AreEqual(1L, IntValue(statement.Inputs[0]));
        Assert.AreEqual("a", ((SimpleNameExpressionNode)statement.Inputs[1]).IdentifierName);
        Assert.AreEqual("b", ((SimpleNameExpressionNode)statement.Inputs[2]).IdentifierName);
    }

    private static ModuleParseResult ParseInProcedure(string statement)
    {
        var content = $"""
            Public Sub DoWork()
                {statement}
            End Sub
            """;
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);
        return result;
    }

    private static KeywordStatementNode KeywordStatement(ModuleParseResult result, string token)
    {
        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        return member.Children.OfType<KeywordStatementNode>().Single(k => k.Token == token);
    }

    [TestMethod]
    public void EndStatement_BuildsKeywordStatementWithNoInputs()
    {
        var result = ParseInProcedure("End");
        Assert.IsEmpty(KeywordStatement(result, Tokens.End).Inputs);
    }

    [TestMethod]
    public void StopStatement_BuildsKeywordStatementWithNoInputs()
    {
        var result = ParseInProcedure("Stop");
        Assert.IsEmpty(KeywordStatement(result, Tokens.Stop).Inputs);
    }

    [TestMethod]
    // the parser doesn't validate that an Exit form matches its enclosing construct (that's a
    // downstream compile-error concern) - all 5 parse the same way regardless of context.
    [DataRow("Exit Do", Tokens.ExitDo)]
    [DataRow("Exit For", Tokens.ExitFor)]
    [DataRow("Exit Function", Tokens.ExitFunction)]
    [DataRow("Exit Property", Tokens.ExitProperty)]
    [DataRow("Exit Sub", Tokens.ExitSub)]
    public void ExitStatement_MapsToItsToken(string source, string expectedToken)
    {
        var result = ParseInProcedure(source);
        Assert.IsEmpty(KeywordStatement(result, expectedToken).Inputs);
    }

    [TestMethod]
    public void WithStatement_CapturesExpressionAndBody()
    {
        const string content = """
            Public Sub DoWork()
                With Target
                    Dim x As Long
                End With
            End Sub
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var withStatement = member.Children.OfType<WithStatementNode>().Single();

        Assert.AreEqual("Target", ((SimpleNameExpressionNode)withStatement.WithExpression).IdentifierName);
        Assert.AreEqual("x", withStatement.Body.Children.OfType<VariableDeclarationNode>().Single().Name);
    }

    [TestMethod]
    // With's expression has no wrapper rule, same as While - proves the reused
    // _isCapturingLoopHeaderExpression window threads a full operator tree, not just a bare name.
    // (An operator expression isn't a realistic With target, but the parser doesn't police that -
    // this is purely about proving the capture mechanism, matching the If/While/Do precedent.)
    public void WithStatement_ExpressionIsAnOperatorTree()
    {
        var result = ParseInProcedure("""
            With A + B
            End With
            """);

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var withStatement = member.Children.OfType<WithStatementNode>().Single();
        Assert.AreEqual(Tokens.AdditionOp, ((VBBinaryOperatorExpressionNode)withStatement.WithExpression).Token);
    }

    [TestMethod]
    // regression guard, same shape as every other block construct wired in this session.
    public void WithStatement_NestedDeclaration_ParentsToTheBody()
    {
        const string content = """
            Public Sub Grow()
                With Target
                    ReDim Nested(5)
                End With
            End Sub
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var withStatement = member.Children.OfType<WithStatementNode>().Single();
        Assert.AreEqual("Nested", withStatement.Body.Children.OfType<RedimDeclarationNode>().Single().Name);
    }

    [TestMethod]
    // this grammar's outputItem never actually pairs a value with its trailing separator in one node
    // (outputClause and charPosition each surface as their own item, confirmed empirically here, not
    // assumed) - a value item's own Separator is always null; the separator that visually follows it
    // is the *next* item, with a null Value.
    public void PrintStatement_CapturesFileNumberAndItemsWithSeparators()
    {
        var result = ParseInProcedure("""Print #1, "a"; "b", "c" """.TrimEnd());
        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var print = member.Children.OfType<PrintStatementNode>().Single();

        Assert.AreEqual(Tokens.Print, print.Token);
        Assert.AreEqual(1L, IntValue(print.FileNumber!));
        Assert.HasCount(5, print.Items);

        Assert.AreEqual("a", ((VBStringValue)((LiteralExpressionNode)print.Items[0].Value!).StaticValue).Value);
        Assert.AreEqual(";", print.Items[1].Separator);
        Assert.IsNull(print.Items[1].Value);
        Assert.AreEqual("b", ((VBStringValue)((LiteralExpressionNode)print.Items[2].Value!).StaticValue).Value);
        Assert.AreEqual(",", print.Items[3].Separator);
        Assert.IsNull(print.Items[3].Value);
        Assert.AreEqual("c", ((VBStringValue)((LiteralExpressionNode)print.Items[4].Value!).StaticValue).Value);
        Assert.IsNull(print.Items[4].Separator);
    }

    [TestMethod]
    // a bare separator (nothing printed before it) must still show up as its own item, with a null
    // Value - dropping it would silently shift every later item's column position.
    public void PrintStatement_BareSeparatorItem_HasNullValue()
    {
        var result = ParseInProcedure("""Print #1, , "x" """.TrimEnd());
        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var print = member.Children.OfType<PrintStatementNode>().Single();

        Assert.HasCount(2, print.Items);
        Assert.IsNull(print.Items[0].Value);
        Assert.AreEqual(",", print.Items[0].Separator);
        Assert.AreEqual("x", ((VBStringValue)((LiteralExpressionNode)print.Items[1].Value!).StaticValue).Value);
    }

    [TestMethod]
    public void PrintStatement_WithSpcAndTabClauses()
    {
        var result = ParseInProcedure("Print #1, Spc(3); Tab(10); Tab");
        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var print = member.Children.OfType<PrintStatementNode>().Single();

        Assert.HasCount(5, print.Items);
        Assert.AreEqual(3L, IntValue(((PrintSpcClauseNode)print.Items[0].Value!).Count));
        Assert.AreEqual(";", print.Items[1].Separator);
        Assert.AreEqual(10L, IntValue(((PrintTabClauseNode)print.Items[2].Value!).Column!));
        Assert.AreEqual(";", print.Items[3].Separator);
        Assert.IsNull(((PrintTabClauseNode)print.Items[4].Value!).Column);
    }

    [TestMethod]
    public void WriteStatement_CapturesFileNumberAndItems()
    {
        var result = ParseInProcedure("""Write #1, "a", "b" """.TrimEnd());
        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var write = member.Children.OfType<PrintStatementNode>().Single();

        Assert.AreEqual(Tokens.Write, write.Token);
        Assert.AreEqual(1L, IntValue(write.FileNumber!));
        Assert.HasCount(3, write.Items);
        Assert.AreEqual(",", write.Items[1].Separator);
    }

    [TestMethod]
    // the object-relative bare form (invoking the enclosing form/report's own Print member) has no
    // file number at all.
    public void UnqualifiedObjectPrintStatement_HasNoFileNumber()
    {
        var result = ParseInProcedure("""Print "x" """.TrimEnd());
        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var print = member.Children.OfType<PrintStatementNode>().Single();

        Assert.IsNull(print.FileNumber);
        Assert.AreEqual("x", ((VBStringValue)((LiteralExpressionNode)print.Items.Single().Value!).StaticValue).Value);
    }

    [TestMethod]
    // Debug.Print "x" reaches mainBlockStmt through callStmt's bare form - the owner.Print shape is
    // captured as the callee of a CallStatementNode, same as any other bare call.
    public void ObjectPrintExpression_DebugPrint_CapturesOwnerAndItems()
    {
        var result = ParseInProcedure("""Debug.Print "x", "y" """.TrimEnd());
        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var call = member.Children.OfType<CallStatementNode>().Single();

        var objectPrint = (ObjectPrintExpressionNode)call.Callee;
        Assert.AreEqual("Debug", ((SimpleNameExpressionNode)objectPrint.Owner).IdentifierName);
        Assert.HasCount(3, objectPrint.Items);
        Assert.AreEqual("x", ((VBStringValue)((LiteralExpressionNode)objectPrint.Items[0].Value!).StaticValue).Value);
        Assert.AreEqual(",", objectPrint.Items[1].Separator);
        Assert.AreEqual("y", ((VBStringValue)((LiteralExpressionNode)objectPrint.Items[2].Value!).StaticValue).Value);
    }

    [TestMethod]
    public void OpenStatement_CapturesAllClauses()
    {
        var result = ParseInProcedure("""Open "file.txt" For Append Access Read Write Shared As #1 Len = 128""");
        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var open = member.Children.OfType<OpenStatementNode>().Single();

        Assert.AreEqual("file.txt", ((VBStringValue)((LiteralExpressionNode)open.PathName).StaticValue).Value);
        Assert.AreEqual(VBFileMode.Append, open.Mode);
        Assert.AreEqual(VBFileAccessMode.ReadWrite, open.Access);
        Assert.AreEqual(VBFileLockMode.Shared, open.Lock);
        Assert.AreEqual(1L, IntValue(open.FileNumber));
        Assert.AreEqual(128L, IntValue(open.RecordLength!));
    }

    [TestMethod]
    public void OpenStatement_WithOnlyRequiredClauses_LeavesOptionalClausesNull()
    {
        var result = ParseInProcedure("""Open "file.txt" As #1""");
        var member = result.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var open = member.Children.OfType<OpenStatementNode>().Single();

        Assert.IsNull(open.Mode);
        Assert.IsNull(open.Access);
        Assert.IsNull(open.Lock);
        Assert.IsNull(open.RecordLength);
        Assert.AreEqual(1L, IntValue(open.FileNumber));
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
