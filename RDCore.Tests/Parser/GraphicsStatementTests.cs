using RDCore.Parsing;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Tests.Parser;

/// <summary>
/// <c>Circle</c>, <c>Line</c>, <c>PSet</c> and <c>Scale</c> are statements with a syntax of their own, and the tree has to hold everything they were written with:
/// the parser built nothing at all for them, which left a procedure with a drawing statement in it indistinguishable from one without.
/// </summary>
[TestClass]
public sealed class GraphicsStatementTests
{
    // what the procedure is made of, without the line numbers and the labels that only say where a statement is.
    private static GraphicsMethodStatementNode Graphics(string body)
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), $"Public Sub Main()\r\n{body}\r\nEnd Sub\r\n");
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        var statement = Assert.ContainsSingle(parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single().Children.Where(node => node is not (LineNumberNode or LineLabelNode)));
        return Assert.IsInstanceOfType<GraphicsMethodStatementNode>(statement);
    }

    private static int Number(ExpressionNode? expression)
        => Assert.IsInstanceOfType<LiteralExpressionNode>(expression).StaticValue switch
        {
            VBIntegerValue integer => integer.Value,
            VBLongValue number => number.Value,
            var other => throw new AssertFailedException($"unexpected value type {other.GetType().Name}"),
        };

    private static string Name(ExpressionNode? expression) => Assert.IsInstanceOfType<SimpleNameExpressionNode>(expression).IdentifierName;

    // ---- Circle ----

    [TestMethod]
    public void ACircle_HasItsCenterAndItsRadius()
    {
        var circle = Graphics("Circle (10, 20), 5");

        Assert.AreEqual(Tokens.Circle, circle.Token);
        Assert.IsNull(circle.Target);
        Assert.IsFalse(circle.From!.IsRelative);
        Assert.AreEqual(10, Number(circle.From.X));
        Assert.AreEqual(20, Number(circle.From.Y));
        Assert.IsNull(circle.To);
        Assert.AreEqual(5, Number(circle.Arguments.Single()));
    }

    [TestMethod]
    public void ACircleWithAnOmittedArgument_KeepsTheGap()
    {
        var circle = Graphics("Circle Step (1, 2), 5, , 3");

        Assert.IsTrue(circle.From!.IsRelative);
        Assert.HasCount(3, circle.Arguments);
        Assert.AreEqual(5, Number(circle.Arguments[0]));
        Assert.IsNull(circle.Arguments[1], "the color is not there, which is not a color of zero");
        Assert.AreEqual(3, Number(circle.Arguments[2]));
    }

    [TestMethod]
    public void ACircleOfAnObject_HasTheObject()
    {
        var circle = Graphics("Picture1.Circle (1, 2), 3, 4, 5, 6, 7");

        Assert.AreEqual("Picture1", Name(circle.Target));
        CollectionAssert.AreEqual(new[] { 3, 4, 5, 6, 7 }, circle.Arguments.Select(Number).ToArray());
    }

    // ---- PSet ----

    [TestMethod]
    public void APSet_HasItsPoint_AndItsColorWhenItHasOne()
    {
        var plain = Graphics("PSet (1, 2)");
        var colored = Graphics("Form1.PSet Step (3, 4), 255");

        Assert.AreEqual(Tokens.PSet, plain.Token);
        Assert.IsFalse(plain.From!.IsRelative);
        Assert.IsEmpty(plain.Arguments);
        Assert.AreEqual("Form1", Name(colored.Target));
        Assert.IsTrue(colored.From!.IsRelative);
        Assert.AreEqual(255, Number(colored.Arguments.Single()));
    }

    // ---- Scale ----

    [TestMethod]
    public void AScale_HasItsTwoCorners()
    {
        var scale = Graphics("Scale (0, 0)-(100, 50)");

        Assert.AreEqual(Tokens.Scale, scale.Token);
        Assert.AreEqual(0, Number(scale.From!.X));
        Assert.AreEqual(100, Number(scale.To!.X));
        Assert.AreEqual(50, Number(scale.To.Y));
        Assert.IsEmpty(scale.Arguments);
    }

    [TestMethod]
    public void AScaleOfAnObject_HasTheObject()
        => Assert.AreEqual("Printer", Name(Graphics("Printer.Scale (0, 0)-(1, 1)").Target));

    // Scale written with no corners sets the scale back to twips: it is the statement still, and not a call of the method with no arguments.
    [TestMethod]
    [DataRow("Scale", null, DisplayName = "with no object")]
    [DataRow("Form1.Scale", "Form1", DisplayName = "of an object")]
    [DataRow("Form1.Scale ' twips again", "Form1", DisplayName = "before a comment")]
    [DataRow("Form1.Scale: x = 1", "Form1", DisplayName = "before another statement on the line")]
    public void AScaleWithNoCorners_IsTheStatement_WithNoPoints(string body, string? target)
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), $"Public Sub Main()\r\n{body}\r\nEnd Sub\r\n");
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        var scale = Assert.ContainsSingle(parse.SyntaxTree!.Descendants().OfType<GraphicsMethodStatementNode>());
        Assert.AreEqual(Tokens.Scale, scale.Token);
        Assert.AreEqual(target, scale.Target is null ? null : Name(scale.Target));
        Assert.IsNull(scale.From);
        Assert.IsNull(scale.To);
        Assert.IsEmpty(scale.Arguments);
        Assert.IsEmpty(parse.SyntaxTree!.Descendants().OfType<UnbuiltStatementTriviaNode>());
    }

    [TestMethod]
    public void AScaleWithNoCorners_OfAMemberAccess_IsTheScaleOfTheObject()
    {
        var scale = Graphics("Me.Picture1.Scale");

        Assert.IsInstanceOfType<MemberAccessExpressionNode>(scale.Target);
        Assert.IsNull(scale.From);
    }

    [TestMethod]
    public void AScaleWithNoCorners_InAWithBlock_IsOfTheObjectOfTheWithBlock()
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), "Public Sub Main()\r\nWith Form1\r\n.Scale\r\nEnd With\r\nEnd Sub\r\n");
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        var scale = Assert.ContainsSingle(parse.SyntaxTree!.Descendants().OfType<GraphicsMethodStatementNode>());
        Assert.IsTrue(scale.IsWithRelative);
        Assert.IsNull(scale.From);
    }

    // a member named Scale that is assigned, or called with arguments of its own, is no Scale statement.
    [TestMethod]
    [DataRow("x.Scale = 1", DisplayName = "assigned")]
    [DataRow("x.Scale 1, 2", DisplayName = "called with arguments")]
    [DataRow("x.Scale(1)", DisplayName = "called with an argument in parentheses")]
    public void AMemberNamedScale_ThatIsNotTheStatement_IsNotRead_AsTheStatement(string body)
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), $"Public Sub Main()\r\n{body}\r\nEnd Sub\r\n");
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        Assert.IsEmpty(parse.SyntaxTree!.Descendants().OfType<GraphicsMethodStatementNode>());
    }

    // ---- Line ----

    [TestMethod]
    public void ALine_HasItsTwoPoints()
    {
        var line = Graphics("Line (0, 0)-(10, 20)");

        Assert.AreEqual(Tokens.Line, line.Token);
        Assert.IsNull(line.Target);
        Assert.IsFalse(line.IsWithRelative);
        Assert.AreEqual(0, Number(line.From!.X));
        Assert.AreEqual(10, Number(line.To!.X));
        Assert.AreEqual(20, Number(line.To.Y));
        Assert.IsEmpty(line.Arguments);
        Assert.IsNull(line.LineOption);
    }

    [TestMethod]
    public void ALineFromTheLastPoint_HasNoStartPoint_ButItsColorAndItsBox()
    {
        var line = Graphics("Line -(10, 10), 3, BF");

        Assert.IsNull(line.From, "the line starts where the last one ended; nothing was written");
        Assert.AreEqual(10, Number(line.To!.X));
        Assert.AreEqual(3, Number(line.Arguments.Single()));
        Assert.AreEqual("BF", line.LineOption);
    }

    [TestMethod]
    public void ALineWithStepOnBothPoints_AndNoColor_KeepsBoth()
    {
        var line = Graphics("Line Step (1, 2)-Step (3, 4), , B");

        Assert.IsTrue(line.From!.IsRelative);
        Assert.IsTrue(line.To!.IsRelative);
        Assert.HasCount(1, line.Arguments);
        Assert.IsNull(line.Arguments[0]);
        Assert.AreEqual("B", line.LineOption);
    }

    [TestMethod]
    public void OnlyTheSecondPointOfALine_IsRelative_WhenOnlyItIsWrittenWithStep()
    {
        var line = Graphics("Line (1, 2)-Step (3, 4)");

        Assert.IsFalse(line.From!.IsRelative);
        Assert.IsTrue(line.To!.IsRelative);
    }

    [TestMethod]
    public void ALineOfAnObject_HasTheObject_WhateverTheCaseOfTheWord()
    {
        var line = Graphics("Form1.LINE (0, 0)-(1, 1)");

        Assert.AreEqual("Form1", Name(line.Target));
        Assert.AreEqual(Tokens.Line, line.Token);
    }

    // blanks before a parenthesis begin an argument list in an expression, and the object and the word of a Line are one: they still begin its first point.
    [TestMethod]
    [DataRow("Line (0, 0) - (1, 1)")]
    [DataRow("Line (0, 0) -(1, 1)")]
    [DataRow("Line (0, 0) -Step(1, 1)")]
    [DataRow("Line\t(0, 0) - (1, 1)")]
    [DataRow("Me.Line (0, 0) - (1, 1)")]
    [DataRow("Me.Line (0, 0) -(1, 1)")]
    [DataRow("Form1.Line (0, 0) - (1, 1), 3, BF")]
    [DataRow("With Form1\r\n.Line (0, 0) - (1, 1)\r\nEnd With")]
    [DataRow("If x Then .Line (0, 0) - (1, 1)")]
    [DataRow("If x Then Line (0, 0) - (1, 1)")]
    public void ALineWithBlanksBeforeItsPoints_HasBothPoints(string body)
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), $"Public Sub Main()\r\n{body}\r\nEnd Sub\r\n");
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        Assert.IsNotNull(parse.SyntaxTree);
        var line = Assert.ContainsSingle(parse.SyntaxTree.Descendants().OfType<GraphicsMethodStatementNode>());
        Assert.AreEqual(Tokens.Line, line.Token);
        Assert.AreEqual(0, Number(line.From!.X));
        Assert.AreEqual(1, Number(line.To!.Y));
        Assert.IsEmpty(parse.SyntaxTree!.Descendants().OfType<UnbuiltStatementTriviaNode>());
    }

    // ---- inside a With block ----

    [TestMethod]
    [DataRow("Circle")]
    [DataRow("Line")]
    [DataRow("PSet")]
    [DataRow("Scale")]
    public void AStatementWrittenWithADotAndNoObject_IsOfTheObjectOfTheWithBlock(string word)
    {
        var rest = word switch
        {
            "Circle" => " (1, 1), 2",
            "PSet" => " (1, 1)",
            _ => " (0, 0)-(1, 1)",
        };
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), $"Public Sub Main()\r\nWith Form1\r\n.{word}{rest}\r\nEnd With\r\nEnd Sub\r\n");

        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
        var with = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single().Children.OfType<WithStatementNode>().Single();
        var statement = Assert.IsInstanceOfType<GraphicsMethodStatementNode>(Assert.ContainsSingle(with.Body.Children));
        Assert.AreEqual(word, statement.Token);
        Assert.IsTrue(statement.IsWithRelative);
        Assert.IsNull(statement.Target);
    }

    [TestMethod]
    public void AStatementWithAnObject_IsNotWithRelative()
        => Assert.IsFalse(Graphics("Form1.Circle (1, 1), 2").IsWithRelative);

    [TestMethod]
    public void AnObjectThatIsAMemberAccess_IsTheObject_NotItsLastMember()
    {
        var scale = Graphics("Me.Picture1.Scale (0, 0)-(1, 1)");

        Assert.AreEqual(Tokens.Scale, scale.Token, "it is the Scale of Me.Picture1, and not a Line of Me.Picture1.Scale");
        Assert.IsInstanceOfType<MemberAccessExpressionNode>(scale.Target);
    }

    // ---- all of them ----

    [TestMethod]
    [DataRow("Circle (1, 1), 2")]
    [DataRow("PSet (1, 1)")]
    [DataRow("Scale (0, 0)-(10, 10)")]
    [DataRow("Line (0, 0)-(10, 10)")]
    [DataRow("10 Line (0, 0)-(10, 10)")]
    [DataRow("A: Circle (1, 1), 2")]
    public void AGraphicsStatement_IsOneStatementOfTheProcedure(string body) => Assert.IsNotNull(Graphics(body));

    [TestMethod]
    public void TheExpressionsOfAGraphicsStatement_AreChildrenOfIt_InSourceOrder()
    {
        var line = Graphics("Form1.Line (1, 2)-(3, 4), 5");

        CollectionAssert.AreEqual(
            new[] { typeof(SimpleNameExpressionNode), typeof(GraphicsPointNode), typeof(GraphicsPointNode), typeof(LiteralExpressionNode) },
            line.Children.Select(child => child.GetType()).ToArray());
    }
}
