using RDCore.Parsing;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RDCore.Tests.Model.AST;

/// <summary>
/// The AST crosses process boundaries as polymorphic JSON. <see cref="SyntaxNode"/> declares the
/// closed set of concrete node types via <see cref="JsonDerivedTypeAttribute"/>; a type missing from
/// that set throws <see cref="NotSupportedException"/> the moment an AST containing it is serialized.
/// </summary>
[TestClass]
public sealed class SyntaxNodeSerializationTests
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    [TestMethod]
    public void EveryConcreteSyntaxNode_HasAPolymorphicRegistration()
    {
        var registered = typeof(SyntaxNode)
            .GetCustomAttributes<JsonDerivedTypeAttribute>(inherit: false)
            .Select(a => a.DerivedType)
            .ToHashSet();

        var missing = typeof(SyntaxNode).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsClass: true } && t.IsAssignableTo(typeof(SyntaxNode)))
            .Where(t => !registered.Contains(t))
            .Select(t => t.FullName)
            .OrderBy(name => name)
            .ToArray();

        Assert.AreEqual(0, missing.Length,
            "SyntaxNode subtypes with no [JsonDerivedType] registration (an AST containing one fails to serialize):"
            + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    [TestMethod]
    public void PolymorphicDiscriminators_AreUnique()
    {
        var discriminators = typeof(SyntaxNode)
            .GetCustomAttributes<JsonDerivedTypeAttribute>(inherit: false)
            .Select(a => a.TypeDiscriminator?.ToString() ?? "<null>")
            .ToArray();

        CollectionAssert.AreEquivalent(discriminators.Distinct().ToArray(), discriminators);
    }

    [TestMethod]
    public void Tree_OfPreviouslyUnregisteredNodeTypes_RoundTripsStably()
    {
        var loc = TestLocations.TestLocation;
        static SyntaxNodeId Id(params int[] lineage) => new("file:///test.bas", [.. lineage]);

        // every node type that was missing a [JsonDerivedType] registration
        var typedDecl = new VBTypedDeclarationExpressionNode(Id(0), loc, "Foo");
        var elseBlock = new ElseBlockStatementNode(Id(1), loc, new StatementBlock([]));
        var pcName = new PrecompilerNameExpressionNode(Id(2, 0), loc, "RDDEBUG");
        var pcElse = new PrecompilerElseBlockStatementNode(Id(2, 1), loc, []);
        var trivia = new PrecompilerTriviaNode(Id(2), loc, [pcName, pcElse], "#If RDDEBUG Then");

        SyntaxNode module = new ModuleNode(Id(), loc, [typedDecl, elseBlock, trivia]);

        var json = JsonSerializer.Serialize(module, Options);
        var rehydrated = JsonSerializer.Deserialize<SyntaxNode>(json, Options);

        Assert.IsInstanceOfType<ModuleNode>(rehydrated);
        Assert.AreEqual(json, JsonSerializer.Serialize(rehydrated, Options));
        Assert.AreEqual(3, ((ModuleNode)rehydrated!).Children.Length);
        Assert.IsInstanceOfType<PrecompilerTriviaNode>(((ModuleNode)rehydrated).Children[2]);
    }

    // one representative instance per node family that carries a property or collection element
    // declared as a narrower abstract SyntaxNode subtype (ExpressionNode, CaseRangeClauseNode) rather
    // than SyntaxNode itself — the exact shape that broke deserialization (see
    // NodeFamiliesWithNarrowerAbstractProperties_RoundTripStably's own remarks). Each row is built with
    // every abstract-typed slot actually populated (not left null/empty) so a regression in
    // SyntaxNodeSubtypeJsonConverter<T>, or a future node reverting to the broken shape, fails loudly.
    public static IEnumerable<object[]> NodeFamiliesWithNarrowerAbstractProperties()
    {
        var loc = TestLocations.TestLocation;
        static SyntaxNodeId Id(params int[] lineage) => new("file:///test.bas", [.. lineage]);
        static LiteralExpressionNode IntLiteral(SyntaxNodeId id, short value) => new(id, TestLocations.TestLocation, new VBIntegerValue(value));
        static SimpleNameExpressionNode Name(SyntaxNodeId id, string name) => new(id, TestLocations.TestLocation, name);

        yield return ["If/ElseIf/Else", (SyntaxNode)new IfBlockStatementNode(
            Id(0), loc, Name(Id(0, 0), "A"), new StatementBlock([]),
            [new ElseIfBlockStatementNode(Id(0, 1), loc, Name(Id(0, 1, 0), "B"), new StatementBlock([]))],
            new ElseBlockStatementNode(Id(0, 2), loc, new StatementBlock([])))];

        yield return ["While...Wend", (SyntaxNode)new WhileWendStatementNode(Id(1), loc, Name(Id(1, 0), "Flag"), new StatementBlock([]))];

        yield return ["Do Until...Loop", (SyntaxNode)new DoUntilLoopStatementNode(Id(2), loc, Name(Id(2, 0), "Flag"), new StatementBlock([]))];

        yield return ["For...Next", (SyntaxNode)new ForStatementNode(
            Id(3), loc, Name(Id(3, 0), "i"), IntLiteral(Id(3, 1), 1), IntLiteral(Id(3, 2), 10), IntLiteral(Id(3, 3), 2), new StatementBlock([]))];

        yield return ["For Each...Next", (SyntaxNode)new ForEachStatementNode(Id(4), loc, Name(Id(4, 0), "Item"), Name(Id(4, 1), "Items"), new StatementBlock([]))];

        yield return ["Select Case (all 3 range clause kinds + Case Else)", (SyntaxNode)new SelectCaseStatementNode(
            Id(5), loc, Name(Id(5, 0), "N"),
            [new CaseExpressionStatementNode(Id(5, 1), loc,
                [
                    new CaseValueRangeClauseNode(Id(5, 1, 0), loc, IntLiteral(Id(5, 1, 0, 0), 1)),
                    new CaseComparisonRangeClauseNode(Id(5, 1, 1), loc, Tokens.CompareGreaterThanOp, IntLiteral(Id(5, 1, 1, 0), 5)),
                    new CaseToRangeClauseNode(Id(5, 1, 2), loc, IntLiteral(Id(5, 1, 2, 0), 1), IntLiteral(Id(5, 1, 2, 1), 10)),
                ],
                new StatementBlock([]))],
            new CaseElseClauseStatementNode(Id(5, 2), loc, new StatementBlock([])))];

        yield return ["With...End With", (SyntaxNode)new WithStatementNode(Id(6), loc, Name(Id(6, 0), "Target"), new StatementBlock([]))];

        yield return ["Call (explicit, callee is an IndexExpression)", (SyntaxNode)new CallStatementNode(
            Id(7), loc,
            new IndexExpressionNode(Id(7, 0), loc, Name(Id(7, 0, 0), "Foo"), [IntLiteral(Id(7, 0, 1), 1)]),
            [], IsExplicitCall: true)];

        yield return ["Call (bare, with its own Arguments)", (SyntaxNode)new CallStatementNode(
            Id(8), loc, Name(Id(8, 0), "Foo"), [IntLiteral(Id(8, 1), 1)], IsExplicitCall: false)];

        yield return ["Index expression (named + missing + AddressOf arguments)", (SyntaxNode)new IndexExpressionNode(
            Id(9), loc, Name(Id(9, 0), "Foo"),
            [
                new NamedArgumentNode(Id(9, 1), loc, "Bar", IntLiteral(Id(9, 1, 0), 5)),
                new MissingArgumentNode(Id(9, 2), loc),
                new AddressOfExpressionNode(Id(9, 3), loc, Name(Id(9, 3, 0), "Callback")),
            ])];

        yield return ["Member access (owner + with-relative)", (SyntaxNode)new MemberAccessExpressionNode(Id(10), loc, Name(Id(10, 0), "Foo"), Name(Id(10, 1), "Bar"))];

        yield return ["Dictionary access (with-relative, no owner)", (SyntaxNode)new DictionaryAccessExpressionNode(Id(11), loc, null, Name(Id(11, 0), "Bar"))];

        yield return ["Assignment (Set, target is a member access)", (SyntaxNode)new AssignmentStatementNode(
            Id(12), loc, Tokens.Set, true,
            new MemberAccessExpressionNode(Id(12, 0), loc, Name(Id(12, 0, 0), "Foo"), Name(Id(12, 0, 1), "Bar")),
            Name(Id(12, 1), "Value"))];
    }

    public static string GetNodeFamilyName(MethodInfo method, object[] data) => (string)data[0];

    [TestMethod]
    [DynamicData(nameof(NodeFamiliesWithNarrowerAbstractProperties), DynamicDataDisplayName = nameof(GetNodeFamilyName))]
    // regression: a property/element declared as a narrower abstract SyntaxNode subtype
    // (ExpressionNode, CaseRangeClauseNode) — rather than SyntaxNode itself — threw "Deserialization
    // of interface or abstract types is not supported" on read, because [JsonPolymorphic]/
    // [JsonDerivedType] is declared once on SyntaxNode and System.Text.Json does not apply an
    // ancestor's polymorphism attributes to a narrower type that carries none of its own. This affects
    // essentially every statement/expression node built this session — pinned here per family (not
    // just one example type) with the same re-serialize-and-compare-text rigor as the other round-trip
    // tests in this file, independent of whichever node type happens to first exercise it via a real
    // parsed fixture.
    public void NodeFamiliesWithNarrowerAbstractProperties_RoundTripStably(string label, SyntaxNode node)
    {
        var json = JsonSerializer.Serialize(node, Options);
        var rehydrated = JsonSerializer.Deserialize<SyntaxNode>(json, Options);

        Assert.AreEqual(json, JsonSerializer.Serialize(rehydrated, Options), $"{label} did not round-trip stably.");
    }

    [TestMethod]
    public void LiteralValue_RoundTripsAsTypeAndScalar()
    {
        // VBTypedValueJsonConverter: a literal is (type, scalar), not the runtime graph
        // (VBType.DefaultValue -> TypeInfo -> DefaultValue ... would otherwise cycle).
        var literal = new LiteralExpressionNode(new("file:///t.bas", [0]), TestLocations.TestLocation, new VBIntegerValue((short)42));

        var json = JsonSerializer.Serialize<SyntaxNode>(literal, Options);
        var back = (LiteralExpressionNode)JsonSerializer.Deserialize<SyntaxNode>(json, Options)!;

        Assert.IsInstanceOfType<VBIntegerValue>(back.StaticValue);
        Assert.AreEqual((short)42, ((VBIntegerValue)back.StaticValue).Value);
        Assert.AreEqual(json, JsonSerializer.Serialize<SyntaxNode>(back, Options));
    }

    [TestMethod]
    public void ParsedModuleWithConstantsAndPrecompilerTrivia_RoundTripsStably()
    {
        const string content = """
            Option Explicit
            #Const RDDEBUG = 1
            Public Const Answer As Long = 42
            #If RDDEBUG Then
            Public Const Mode As String = "debug"
            #Else
            Public Const Mode As String = "release"
            #End If
            """;

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var treeJson = JsonSerializer.Serialize(result.SyntaxTree, Options);
        Assert.AreEqual(treeJson, JsonSerializer.Serialize(JsonSerializer.Deserialize<ModuleNode>(treeJson, Options), Options));

        Assert.IsNotEmpty(result.PrecompilerTrivia);
        var triviaJson = JsonSerializer.Serialize(result.PrecompilerTrivia, Options);
        Assert.AreEqual(triviaJson, JsonSerializer.Serialize(JsonSerializer.Deserialize<ImmutableArray<SyntaxNode>>(triviaJson, Options), Options));
    }
}
