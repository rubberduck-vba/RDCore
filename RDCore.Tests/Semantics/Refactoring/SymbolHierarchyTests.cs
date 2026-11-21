using RDCore.LanguageServer.Services.Refactoring;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.Symbols;
using RDCore.Runtime.LanguageCore.Refactoring;

namespace RDCore.Tests.Semantics.Refactoring;

/// <summary>
/// The call hierarchy of the workspace says which procedures call which, and where; its type hierarchy, which classes implement which
/// (<strong>LSP 3.17</strong> §Call Hierarchy, §Type Hierarchy).
/// </summary>
[TestClass]
public sealed class SymbolHierarchyTests
{
    private static readonly Uri Root = new("file://rdcore-test");

    private static string Class(string name, params string[] lines)
        => Lines(["VERSION 1.0 CLASS", "BEGIN", "  MultiUse = -1  'True", "END", $"Attribute VB_Name = \"{name}\"", "Option Explicit", .. lines]);

    private static readonly string IShape = Class("IShape", "Public Function Area() As Double", "End Function");

    private static string Shape(string name) => Class(name, "Implements IShape", "Private Function IShape_Area() As Double", "End Function");

    private static readonly string Module1 = Lines(
        "Attribute VB_Name = \"Module1\"",
        "Option Explicit",
        "Public Sub Main()",
        "    Helper",
        "    Debug.Print Twice(2)",
        "End Sub",
        "Public Sub Helper()",
        "    Debug.Print Twice(3) + Twice(4)",
        "End Sub",
        "Public Function Twice(ByVal n As Long) As Long",
        "    Twice = n * 2",
        "End Function");

    // procedures that call themselves, directly or through another, and functions whose name in their body is their result.
    private static readonly string Module2 = Lines(
        "Attribute VB_Name = \"Module2\"",
        "Option Explicit",
        "Public Sub Countdown(ByVal n As Long)",
        "    If n > 0 Then Countdown n - 1",
        "End Sub",
        "Public Function Factorial(ByVal n As Long) As Long",
        "    If n <= 1 Then",
        "        Factorial = 1",
        "    Else",
        "        Factorial = n * Factorial(n - 1)",
        "    End If",
        "End Function",
        "Public Function Answer() As Long",
        "    Answer = 41",
        "    Answer = Answer + 1",
        "End Function",
        "Public Function Filled() As Collection",
        "    Set Filled = New Collection",
        "    Filled.Add 1",
        "End Function",
        "Public Sub Ping(ByVal n As Long)",
        "    If n > 0 Then Pong n - 1",
        "End Sub",
        "Public Sub Pong(ByVal n As Long)",
        "    Ping n",
        "End Sub");

    // a function named as one of Module2's, which calls that one.
    private static readonly string Module3 = Lines(
        "Attribute VB_Name = \"Module3\"",
        "Option Explicit",
        "Public Function Answer() As Long",
        "    Answer = Module2.Answer",
        "End Function");

    private static string Lines(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private static RenameContext Workspace()
    {
        (string Name, string Source)[] modules =
            [("IShape", IShape), ("Square", Shape("Square")), ("Circle", Shape("Circle")), ("Module1", Module1), ("Module2", Module2), ("Module3", Module3)];
        var parsed = modules.Select(module =>
        {
            var isClass = module.Source.StartsWith("VERSION", StringComparison.Ordinal);
            var parse = new ModuleParser().Parse(new Uri($"file:///c:/ws/{module.Name}{(isClass ? ".cls" : ".bas")}"), module.Source);
            Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
            return (Uri: ModuleUri(module.Name), Type: isClass ? ModuleType.ClassModule : ModuleType.StdModule, Parse: parse, module.Source);
        }).ToList();

        var composition = WorkspaceSymbolResolver.ComposeWithScopes(Root, [.. parsed.Select(module => (module.Uri, module.Type, module.Parse))], new IntrinsicSymbolResolver());
        return new RenameContext(composition.Resolver, composition.ScopeTree, [.. parsed.Select(module => new RenameModule(module.Uri, module.Parse.SyntaxTree!, module.Source))]);
    }

    private static Uri ModuleUri(string name) => new UriBuilder(Root) { Fragment = name }.Uri;

    private static SourcePosition At(RenameContext context, string module, string line, string text)
    {
        var lines = context.Modules.Single(candidate => candidate.Module.Fragment == "#" + module).Source.Split("\r\n");
        var index = Array.FindIndex(lines, candidate => candidate.Contains(line, StringComparison.Ordinal));
        return new SourcePosition(index, lines[index].IndexOf(text, StringComparison.Ordinal) + 1);
    }

    private static HierarchyItem Calls(RenameContext context, string line, string text, string module = "Module1")
        => SymbolHierarchy.PrepareCalls(context, ModuleUri(module), At(context, module, line, text))
            ?? throw new AssertFailedException($"'{text}' is no procedure");

    private static string[] Names(IReadOnlyList<HierarchyCall> calls)
        => [.. calls.Select(call => $"{call.Item.Module.Fragment.TrimStart('#')}.{call.Item.Symbol.Name}")];

    [TestMethod]
    public void AProcedureThatCallsItself_IsItsOwnCallee_AndItsOwnCaller()
    {
        var context = Workspace();
        var countdown = Calls(context, "Public Sub Countdown", "Countdown", "Module2");

        CollectionAssert.AreEqual(new[] { "Module2.Countdown" }, Names(SymbolHierarchy.OutgoingCalls(context, countdown)));
        CollectionAssert.AreEqual(new[] { "Module2.Countdown" }, Names(SymbolHierarchy.IncomingCalls(context, countdown)));
    }

    [TestMethod]
    public void AFunctionThatCallsItself_IsItsOwnCallee_WhereItIsCalled_AndNotWhereItIsGivenItsResult()
    {
        var context = Workspace();
        var outgoing = SymbolHierarchy.OutgoingCalls(context, Calls(context, "Public Function Factorial", "Factorial", "Module2"));

        CollectionAssert.AreEqual(new[] { "Module2.Factorial" }, Names(outgoing));
        Assert.AreEqual(At(context, "Module2", "Factorial = n * Factorial(n - 1)", "Factorial(n").Character - 1, outgoing[0].Ranges.Single().Start.Character);
    }

    [TestMethod]
    public void AFunctionGivenItsResult_AndReadingIt_CallsNothing()
        => Assert.IsEmpty(SymbolHierarchy.OutgoingCalls(Workspace(), Calls(Workspace(), "Public Function Answer", "Answer", "Module2")));

    [TestMethod]
    public void AFunctionThatCallsAMemberOfItsResult_CallsNothingOfItsOwn()
        => Assert.IsEmpty(SymbolHierarchy.OutgoingCalls(Workspace(), Calls(Workspace(), "Public Function Filled", "Filled", "Module2")));

    [TestMethod]
    public void AProcedureThatCallsItselfThroughAnother_IsTheOthersCallee()
    {
        var context = Workspace();

        CollectionAssert.AreEqual(new[] { "Module2.Pong" },
            Names(SymbolHierarchy.OutgoingCalls(context, Calls(context, "Public Sub Ping", "Ping", "Module2"))));
        CollectionAssert.AreEqual(new[] { "Module2.Ping" },
            Names(SymbolHierarchy.OutgoingCalls(context, Calls(context, "Public Sub Pong", "Pong", "Module2"))));
    }

    [TestMethod]
    public void AFunctionNamedAsAnotherModulesFunction_CallsThatOne_AndNotItself()
    {
        var context = Workspace();
        var answer = Calls(context, "Public Function Answer", "Answer", "Module3");

        CollectionAssert.AreEqual(new[] { "Module2.Answer" }, Names(SymbolHierarchy.OutgoingCalls(context, answer)));
        Assert.IsEmpty(SymbolHierarchy.IncomingCalls(context, answer));
    }

    [TestMethod]
    public void AProcedure_IsAnItem_DeclaredFromItsFirstLineToItsEnd()
    {
        var context = Workspace();
        var item = Calls(context, "Debug.Print Twice(2)", "Twice");

        Assert.AreEqual("Twice", item.Symbol.Name);
        Assert.AreEqual("#Module1", item.Module.Fragment);
        Assert.AreEqual(9, item.Range.Start.Line);
        Assert.AreEqual(11, item.Range.End.Line);
        Assert.AreEqual(new SourceRange(9, 16, 9, 21), item.SelectionRange);
    }

    [TestMethod]
    public void AVariable_IsNoItemOfTheCallHierarchy()
        => Assert.IsNull(SymbolHierarchy.PrepareCalls(Workspace(), ModuleUri("Module1"), At(Workspace(), "Module1", "Twice = n * 2", "n")));

    [TestMethod]
    public void TheIncomingCalls_AreTheProceduresThatCall_EachWithWhereItCalls_AndNotAFunctionGivenItsResult()
    {
        var context = Workspace();
        var incoming = SymbolHierarchy.IncomingCalls(context, Calls(context, "Debug.Print Twice(2)", "Twice"));

        CollectionAssert.AreEqual(new[] { "Main", "Helper" }, incoming.Select(call => call.Item.Symbol.Name).ToArray());
        Assert.HasCount(1, incoming[0].Ranges);
        Assert.HasCount(2, incoming[1].Ranges, "Twice(3) + Twice(4)");
    }

    [TestMethod]
    public void TheOutgoingCalls_AreWhatTheProcedureCalls()
    {
        var context = Workspace();
        var outgoing = SymbolHierarchy.OutgoingCalls(context, Calls(context, "Public Sub Main()", "Main"));

        CollectionAssert.AreEqual(new[] { "Helper", "Twice" }, outgoing.Select(call => call.Item.Symbol.Name).ToArray());
        Assert.AreEqual(new SourceRange(3, 4, 3, 10), outgoing[0].Ranges.Single());
    }

    [TestMethod]
    public void AnItem_IsFoundAgainByTheIdentityOfItsSymbol()
    {
        var context = Workspace();
        var item = Calls(context, "Debug.Print Twice(2)", "Twice");

        Assert.AreEqual(item, SymbolHierarchy.ItemOf(context, item.Symbol.SemanticId));
    }

    [TestMethod]
    public void AClass_HasTheClassesThatImplementIt_AsSubtypes_AndTheClassesItImplements_AsSupertypes()
    {
        var context = Workspace();
        var shape = SymbolHierarchy.PrepareTypes(context, ModuleUri("Square"), At(context, "Square", "Implements IShape", "IShape"));

        Assert.IsNotNull(shape);
        CollectionAssert.AreEqual(new[] { "Circle", "Square" }, SymbolHierarchy.Subtypes(context, shape).Select(item => item.Symbol.Name).ToArray());

        var square = SymbolHierarchy.Subtypes(context, shape).Single(item => item.Symbol.Name == "Square");
        CollectionAssert.AreEqual(new[] { "IShape" }, SymbolHierarchy.Supertypes(context, square).Select(item => item.Symbol.Name).ToArray());
        Assert.IsEmpty(SymbolHierarchy.Supertypes(context, shape));
    }
}
