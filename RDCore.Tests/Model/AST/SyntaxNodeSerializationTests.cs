using RDCore.Parsing;
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
        var elseBlock = new ElseBlockStatementNode(Id(1), loc, []);
        var pcName = new PrecompilerNameExpressionNode(Id(2, 0), loc, "RDDEBUG");
        var pcElse = new PrecompilerElseBlockStatementNode(Id(2, 1), loc, []);
        var trivia = new PrecompilerTriviaNode(Id(2), loc, [pcName, pcElse], "#If RDDEBUG Then");

        SyntaxNode module = new ModuleNode(Id(), loc, [typedDecl, elseBlock, trivia], ModuleType.StdModule);

        var json = JsonSerializer.Serialize(module, Options);
        var rehydrated = JsonSerializer.Deserialize<SyntaxNode>(json, Options);

        Assert.IsInstanceOfType<ModuleNode>(rehydrated);
        Assert.AreEqual(json, JsonSerializer.Serialize(rehydrated, Options));
        Assert.AreEqual(3, ((ModuleNode)rehydrated!).Children.Length);
        Assert.IsInstanceOfType<PrecompilerTriviaNode>(((ModuleNode)rehydrated).Children[2]);
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

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), ModuleType.StdModule, content);
        Assert.IsTrue(result.IsSuccess, result.SyntaxErrors.Length == 0 ? "" : result.SyntaxErrors[0]!.Description);

        var treeJson = JsonSerializer.Serialize(result.SyntaxTree, Options);
        Assert.AreEqual(treeJson, JsonSerializer.Serialize(JsonSerializer.Deserialize<ModuleNode>(treeJson, Options), Options));

        Assert.IsNotEmpty(result.PrecompilerTrivia);
        var triviaJson = JsonSerializer.Serialize(result.PrecompilerTrivia, Options);
        Assert.AreEqual(triviaJson, JsonSerializer.Serialize(JsonSerializer.Deserialize<ImmutableArray<SyntaxNode>>(triviaJson, Options), Options));
    }
}
