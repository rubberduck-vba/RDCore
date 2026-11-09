using RDCore.LanguageServer.Services.Refactoring;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Formatting;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.Symbols;
using RDCore.Tests.Cli;
using RDCore.Runtime.LanguageCore.Refactoring;

namespace RDCore.Tests.Parser;

/// <summary>
/// An expression nested past the limit (<c>VBC00060</c>, <strong>RD-VBAL §2.6.1</strong>) is kept as text, and isn't analyzed: what is nested in more than 2,500
/// expressions is an error node of the text it is written as (<see cref="UnbuiltExpressionTriviaNode"/>), and no stage after the parser - the load of the
/// environment host, the static pass, a hover, the references of a name, the hints of a call - is handed a tree deeper than the limit.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL 2.6.1 Syntax Errors")]
public sealed class ExpressionPastTheLimitTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly Uri Program = new UriBuilder(Root) { Fragment = "Program" }.Uri;

    // an assignment to what 40,000 argument lists after a name index: each list is an index expression its callee is nested in.
    private static readonly string[] Lines =
    [
        "Attribute VB_Name = \"Program\"",
        "Public Sub Add(ByVal amount As Long)",
        "End Sub",
        "",
        "Public Sub Main()",
        "    Dim a As Variant",
        "    Dim y As Long",
        $"    a{string.Concat(Enumerable.Repeat("(1)", 40_000))} = 1",
        "    y = 1",
        "    Add y",
        "End Sub",
    ];

    private static readonly string Source = string.Join("\r\n", Lines) + "\r\n";
    private static readonly SourcePosition DeclaredA = new(5, 8);
    private static readonly SourcePosition IndexedA = new(7, 4);
    private static readonly SourcePosition AssignedY = new(8, 4);

    // the module is read once for every test of it: a parse of its 120 KB takes seconds, and no test changes what it is.
    private static readonly Lazy<ModuleParseResult> Parsed = new(() => new ModuleParser().Parse(new Uri("file:///c:/ws/Program.bas"), Source));

    private static ModuleParseResult Parse() => Parsed.Value;

    private static string Describe(ModuleParseResult result)
        => string.Join("; ", result.SyntaxErrors.Select(error => $"{error.ErrorId} {error.Location.Range.Start.Line}:{error.Location.Range.Start.Character}"));

    private static RenameContext Workspace(ModuleParseResult parse)
    {
        var composition = WorkspaceSymbolResolver.ComposeWithScopes(Root, [(Program, ModuleType.StdModule, parse)], new IntrinsicSymbolResolver());
        return new RenameContext(composition.Resolver, composition.ScopeTree, [new RenameModule(Program, parse.SyntaxTree!, Source)]);
    }

    // the text at a range of a module.
    private static string TextAt(SourceRange range, string? text = null)
    {
        var lines = (text ?? Source).Split("\r\n");
        return range.Start.Line == range.End.Line
            ? lines[range.Start.Line][range.Start.Character..range.End.Character]
            : string.Join("\r\n", [lines[range.Start.Line][range.Start.Character..], .. lines[(range.Start.Line + 1)..range.End.Line], lines[range.End.Line][..range.End.Character]]);
    }

    // each expression of the tree, and how many expressions it is nested in as RD-VBAL 2.6.1 counts them: a name passed by name or ByVal, and what is
    // printed, are nested in no more than what holds them, and the arguments of a call statement in its call. The walk keeps a stack of its own.
    private static IEnumerable<(ExpressionNode Expression, int Nesting)> Expressions(SyntaxNode root)
    {
        var pending = new Stack<(SyntaxNode Node, int Nesting)>();
        pending.Push((root, 0));
        while (pending.TryPop(out var entry))
        {
            var inner = entry.Nesting;
            if (entry.Node is ExpressionNode expression)
            {
                yield return (expression, entry.Nesting);
                if (expression is not (NamedArgumentNode or ByValArgumentExpressionNode or ObjectPrintExpressionNode or PrintOutputItemNode
                    or PrintSpcClauseNode or PrintTabClauseNode))
                {
                    inner++;
                }
            }

            foreach (var child in entry.Node.ChildNodes())
            {
                pending.Push((child, entry.Node is CallStatementNode call && !ReferenceEquals(child, call.Callee) ? 1 : inner));
            }
        }
    }

    [TestMethod]
    public void AnExpressionPastTheLimit_IsExpressionNestedTooDeeply_Once_AtTheFirstExpressionPastIt()
    {
        var result = Parse();

        var error = Assert.ContainsSingle(result.SyntaxErrors, Describe(result));
        Assert.AreEqual((VBCompileErrorId.ExpressionNestedTooDeeply, IndexedA), ((VBCompileErrorId)error.ErrorId, error.Location.Range.Start), Describe(result));
    }

    // the callee of the index expression nested in 2,500 others, and its argument, are each nested in 2,501.
    [TestMethod]
    public void WhatIsNestedPastTheLimit_IsAnErrorNode_OfTheTextItIsWritten()
    {
        var tree = Parse().SyntaxTree!;

        var pruned = Expressions(tree).Select(entry => entry.Expression).OfType<UnbuiltExpressionTriviaNode>().OrderBy(node => node.SourceLocation.Range.Start).ToArray();

        Assert.HasCount(2, pruned);
        Assert.AreEqual($"a{string.Concat(Enumerable.Repeat("(1)", 40_000 - ModuleParser.MaxExpressionNesting - 1))}", pruned[0].Source);
        Assert.AreEqual("1", pruned[1].Source);
        foreach (var node in pruned)
        {
            Assert.AreEqual(TextAt(node.SourceLocation.Range), node.Source);
            Assert.IsEmpty(node.Children);
        }
    }

    [TestMethod]
    public void NoExpressionOfTheTree_IsNestedPastTheLimit_ButAnErrorNode()
    {
        var tree = Parse().SyntaxTree!;

        var past = Expressions(tree).Where(entry => entry.Nesting > ModuleParser.MaxExpressionNesting).Select(entry => entry.Expression).ToArray();

        Assert.IsNotEmpty(past);
        Assert.IsTrue(past.All(expression => expression is UnbuiltExpressionTriviaNode), string.Join(", ", past.Select(expression => expression.GetType().Name).Distinct()));
    }

    // 3,000 levels of each shape, in each place a statement holds an expression: what holds what is past the limit - an operator, a member access, a block, a
    // Case clause, a named argument, an item of a print list, a bound of a declared array (held by an ArrayDimensionBound, a struct of its own) - is rebuilt
    // around the error node.
    [TestMethod]
    [DataRow("x = {0}", "parentheses")]
    [DataRow("x = {0}", "members")]
    [DataRow("Set x = {0}", "fluent")]
    [DataRow("If x Then\r\n        x = {0}\r\n    End If", "chain")]
    [DataRow("Select Case x\r\n        Case 1, {0}\r\n    End Select", "parentheses")]
    [DataRow("F a:={0}", "indexes")]
    [DataRow("Debug.Print 1; {0}", "chain")]
    [DataRow("Dim y(1 To {0}) As Long", "parentheses")]
    [DataRow("Dim y({0} To 5, 2) As Long", "chain")]
    public void AnExpressionPastTheLimit_IsAnErrorNode_WhereverItIsWritten(string statement, string shape)
        => AssertPrunedWhereWritten(statement, shape);

    // what is kept as text is not analyzed (RD-VBAL 2.6.1): no check of the static pass reports on it, wherever it is written, and the error the parser
    // reported is the module's only one.
    [TestMethod]
    [DataRow("x = {0}", "parentheses")]
    [DataRow("x = 1 + {0}", "chain")]
    [DataRow("x = {0}", "members")]
    [DataRow("Set x = {0}", "fluent")]
    [DataRow("If x Then\r\n        x = {0}\r\n    End If", "chain")]
    [DataRow("Select Case x\r\n        Case 1, {0}\r\n    End Select", "parentheses")]
    [DataRow("F a:={0}", "parentheses")]
    [DataRow("Debug.Print 1; {0}", "chain")]
    public async Task AnExpressionPastTheLimit_IsExpressionNestedTooDeeply_Only_WhereverItIsWritten(string statement, string shape)
    {
        var program = string.Join("\r\n",
            "Attribute VB_Name = \"Program\"",
            "Public Sub Main()",
            "    Dim x As Variant",
            "    Dim b As Object",
            $"    {string.Format(statement, Deep(shape))}",
            "End Sub",
            "Public Sub F(Optional ByVal a As Variant)",
            "End Sub",
            "");

        var errors = await ModuleWorkspace.LoadErrorsWithSyntaxErrorsAsync(program);

        StringAssert.StartsWith(Assert.ContainsSingle(errors), RDCore.SDK.Exceptions.VBCompileError_ExpressionNestedTooDeeply, string.Join(" | ", errors));
    }

    // what the parse hands on is no deeper than the limit, whatever it is: the module as pruned, or what is salvaged of it when the prune fails (a holder it
    // could not rebuild), which is pruned too, or is nothing.
    [TestMethod]
    [DataRow("x = {0}")]
    [DataRow("Dim y(1 To {0}) As Long")]
    public void NoTreeTheParseHandsOn_HoldsAnExpressionPastTheLimit_ButAnErrorNode(string statement)
    {
        var text = $"Public Sub Main()\r\n    {string.Format(statement, new string('(', 3000) + "1" + new string(')', 3000))}\r\nEnd Sub\r\n";

        var tree = new ModuleParser().Parse(TestUri.TestModuleUri(), text).SyntaxTree;

        var past = tree is null ? [] : Expressions(tree).Where(entry => entry.Nesting > ModuleParser.MaxExpressionNesting).Select(entry => entry.Expression).ToArray();
        Assert.IsTrue(past.All(node => node is UnbuiltExpressionTriviaNode), string.Join(", ", past.Select(node => node.GetType().Name).Distinct()));
    }

    // 3,000 levels of a shape of expression.
    private static string Deep(string shape)
    {
        const int levels = 3000;
        return shape switch
        {
            "parentheses" => new string('(', levels) + "1" + new string(')', levels),
            "members" => "b" + string.Concat(Enumerable.Repeat(".c", levels)),
            "fluent" => "b" + string.Concat(Enumerable.Repeat(".Add(1)", levels / 2)),
            "chain" => ExpressionNestingTests.Chain(levels),
            _ => "a" + string.Concat(Enumerable.Repeat("(1)", levels)),
        };
    }

    private static void AssertPrunedWhereWritten(string statement, string shape)
    {
        var text = $"Public Sub Main()\r\n    {string.Format(statement, Deep(shape))}\r\nEnd Sub\r\n";

        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), text);

        Assert.AreEqual(VBCompileErrorId.ExpressionNestedTooDeeply, (VBCompileErrorId)Assert.ContainsSingle(result.SyntaxErrors, Describe(result)).ErrorId);
        var expressions = Expressions(result.SyntaxTree!).ToArray();
        var past = expressions.Where(entry => entry.Nesting > ModuleParser.MaxExpressionNesting).Select(entry => entry.Expression).ToArray();
        Assert.IsTrue(past.All(node => node is UnbuiltExpressionTriviaNode { Children.IsEmpty: true }), string.Join(", ", past.Select(node => node.GetType().Name).Distinct()));
        var pruned = expressions.Select(entry => entry.Expression).OfType<UnbuiltExpressionTriviaNode>().ToArray();
        Assert.IsNotEmpty(pruned);
        foreach (var node in pruned)
        {
            Assert.AreEqual(TextAt(node.SourceLocation.Range, text), node.Source);
        }
    }

    // the text of an error node is the module's as it is written - a line continuation and a string that holds quotes too - and crosses the wire as it is.
    [TestMethod]
    public void TheTextPastTheLimit_RoundTripsThroughThePlatformsJson_AsItIsWritten_ALineContinuationAndAStringInIt()
    {
        const int levels = 3000;
        var text = $"Public Sub Main()\r\n    x = {new string('(', levels)}\"a \"\"b\"\"\" & _\r\n        1{new string(')', levels)}\r\nEnd Sub\r\n";
        var tree = new ModuleParser().Parse(TestUri.TestModuleUri(), text).SyntaxTree!;

        var again = PlatformJson.Deserialize<SyntaxNode>(PlatformJson.Serialize<SyntaxNode>(tree));

        // the parser reads the lines of a module as ending in a line feed (ModuleParser.NormalizeSource): so does the text it keeps.
        var pruned = Expressions(again).Select(entry => entry.Expression).OfType<UnbuiltExpressionTriviaNode>().ToArray();
        Assert.IsNotEmpty(pruned);
        foreach (var node in pruned)
        {
            Assert.AreEqual(TextAt(node.SourceLocation.Range, text), node.Source.ReplaceLineEndings("\r\n"));
        }

        Assert.IsTrue(pruned.Any(node => node.Source.Contains("\"a \"\"b\"\"\" & _\n        1", StringComparison.Ordinal)), string.Join(" | ", pruned.Select(node => node.Source[..Math.Min(40, node.Source.Length)])));
    }

    // what the parsing server hands the language server and the environment host crosses the wire as JSON: the error node crosses it with its text.
    [TestMethod]
    public void TheTree_RoundTripsThroughThePlatformsJson_WithTheTextPastTheLimit()
    {
        var tree = Parse().SyntaxTree!;

        var json = PlatformJson.Serialize<SyntaxNode>(tree);
        var again = PlatformJson.Deserialize<SyntaxNode>(json);

        Assert.AreEqual(json, PlatformJson.Serialize(again));
        CollectionAssert.AreEqual(
            Expressions(tree).Select(entry => entry.Expression).OfType<UnbuiltExpressionTriviaNode>().Select(node => node.Source).ToArray(),
            Expressions(again).Select(entry => entry.Expression).OfType<UnbuiltExpressionTriviaNode>().Select(node => node.Source).ToArray());
        var names = Expressions(tree).Select(entry => entry.Expression).OfType<UnbuiltExpressionTriviaNode>().SelectMany(node => node.Names).ToArray();
        Assert.IsNotEmpty(names);
        CollectionAssert.AreEqual(names,
            Expressions(again).Select(entry => entry.Expression).OfType<UnbuiltExpressionTriviaNode>().SelectMany(node => node.Names).ToArray());
    }

    [TestMethod]
    public void TheModule_IsIndentedAsItIsWritten()
    {
        var parse = Parse();

        var edits = ModuleIndenter.Indent(parse, Source, CodeFormattingOptions.Default);

        Assert.AreEqual(Source, ModuleIndenter.Apply(Source, edits), string.Join("; ", edits.Select(edit => $"{edit.Line}:{edit.Character}-{edit.EndLine}:{edit.EndCharacter} '{edit.NewText}'")));
    }

    [TestMethod]
    public async Task TheModule_LoadsInTheEnvironmentHost_AndItsFactsAreEvaluated()
    {
        var model = await ModuleWorkspace.ModelAsync([], Source, syntaxErrors: true);

        Assert.IsTrue(model.Procedures.Any(procedure => !procedure.Expressions.IsEmpty), "the facts of the module's expressions");
    }

    // what is kept as text is not analyzed (RD-VBAL 2.6.1): it is no expression a constant's value or an array's bound is required to be, and the error the
    // parser reported is the module's only one.
    [TestMethod]
    [DataRow("Const K = {0}", "", DisplayName = "a constant of the module")]
    [DataRow("", "Const K = {0}", DisplayName = "a constant of the procedure")]
    [DataRow("Const K = 1 + {0}", "", DisplayName = "an operand of a constant's value")]
    [DataRow("", "Dim y(1 To {0}) As Long", DisplayName = "a bound of an array of the procedure")]
    [DataRow("Private y({0} To 5, 2) As Long", "", DisplayName = "a bound of an array of the module")]
    public async Task AConstantPastTheLimit_IsExpressionNestedTooDeeply_Only(string declarations, string body)
    {
        var deep = $"{new string('(', 3000)}1{new string(')', 3000)}";
        var program = string.Join("\r\n",
            "Attribute VB_Name = \"Program\"",
            string.Format(declarations, deep),
            "Public Sub Main()",
            string.Format(body, deep),
            "End Sub",
            "");

        var errors = await ModuleWorkspace.LoadErrorsWithSyntaxErrorsAsync(program);

        StringAssert.StartsWith(Assert.ContainsSingle(errors), RDCore.SDK.Exceptions.VBCompileError_ExpressionNestedTooDeeply, string.Join(" | ", errors));
    }

    // what is kept as text is no code: a run of the module is refused before any of it runs, with the error the parser reported.
    [TestMethod]
    public async Task ARunOfTheModule_IsRefused_BeforeAnyOfItRuns()
    {
        var program = string.Join("\r\n",
            "Attribute VB_Name = \"Program\"",
            "Public Sub Main()",
            "    Dim x As Variant",
            "    Debug.Print \"before\"",
            $"    x = {new string('(', 3000)}1{new string(')', 3000)}",
            "End Sub",
            "");

        var result = await ModuleWorkspace.RunWithSyntaxErrorsAsync(program);

        Assert.AreEqual(ExecutionOutcome.SyntaxError, result.Outcome, result.ErrorMessage);
        Assert.IsEmpty(result.Output);
        StringAssert.StartsWith(Assert.ContainsSingle(result.Diagnostics!), RDCore.SDK.Exceptions.VBCompileError_ExpressionNestedTooDeeply);
    }

    // the static pass recurses once for each level of an expression: what is left of the tree fits the stack of a large-stack thread, which 40,000 levels did not.
    [TestMethod]
    public void AHover_IsAnswered_ButNotOnANamePastTheLimit()
    {
        var context = Workspace(Parse());

        var (assigned, indexed) = LargeStackThread.Run(() => (
            RenameBinding.SymbolAt(context, Program, AssignedY)?.Symbol.Name,
            RenameBinding.SymbolAt(context, Program, IndexedA)?.Symbol.Name));

        Assert.AreEqual("y", assigned);
        Assert.IsNull(indexed, "a name past the limit is not analyzed");
    }

    [TestMethod]
    public void TheReferencesOfANameUsedPastTheLimit_AreItsDeclaration()
    {
        var context = Workspace(Parse());

        var (a, y) = LargeStackThread.Run(() => (
            SymbolNavigation.ReferencesAt(context, Program, DeclaredA)?.Sites.Select(site => site.Range.Start.Line).Distinct().ToArray(),
            SymbolNavigation.ReferencesAt(context, Program, AssignedY)?.Sites.Select(site => site.Range.Start.Line).Distinct().ToArray()));

        CollectionAssert.AreEqual(new[] { DeclaredA.Line }, a, "its declaration: the name past the limit is not analyzed");
        CollectionAssert.AreEquivalent(new[] { 6, 8, 9 }, y, "its declaration, the assignment, and the argument");
    }

    // what is found of a name may be incomplete where it is written past the limit: the name there is not analyzed, and could be it.
    [TestMethod]
    public void TheReferencesOfANameUsedPastTheLimit_MayBeIncomplete_WhereItIsWritten()
    {
        var context = Workspace(Parse());

        var (a, y) = LargeStackThread.Run(() => (SymbolNavigation.ReferencesAt(context, Program, DeclaredA)!, SymbolNavigation.ReferencesAt(context, Program, AssignedY)!));

        Assert.AreEqual(IndexedA, Assert.ContainsSingle(a.Unresolved).Range.Start);
        Assert.AreEqual("The references of 'a' may be incomplete: what 'a' refers to at Program 8:5 is not known.",
            a.IncompleteMessage(culture: System.Globalization.CultureInfo.InvariantCulture));
        Assert.IsEmpty(y.Unresolved);
        Assert.IsNull(y.IncompleteMessage());
    }

    // a rename of a name used past the limit would miss the name there: it is refused, where it is written.
    [TestMethod]
    public void ARenameOfANameUsedPastTheLimit_IsRefused_WhereItIsWritten()
    {
        var context = Workspace(Parse());

        var plan = LargeStackThread.Run(() => SymbolRename.Plan(context, SymbolRename.Prepare(context, Program, DeclaredA).Target!, "b"));

        Assert.IsEmpty(plan.Edits);
        Assert.AreEqual((RenameRefusalKind.Unresolved, IndexedA), (plan.Refusal?.Kind, plan.Refusal?.Position));
    }

    // a name is written past the limit where it is a token of the text, as the VBA lexer reads it: not where a longer name, a string, a date or a number holds
    // it (the VBE writes 1.0 as `1#`), nor where it is the key of a dictionary access, which is the text of a string. A comment after the statement is no part
    // of the expression.
    [TestMethod]
    [DataRow("ab & \"a\" & c!a & #1/1/2000# & &HA", "ab", 0)]
    [DataRow("ab & \"a\" & c!a & #1/1/2000# & &HA", "a", -1)]
    [DataRow("a * 1# + b * 2#", "b", 9)]
    [DataRow("s &hName", "hName", 3)]
    [DataRow("&HFF + c", "c", 7)]
    [DataRow("#1/2/2026# + d", "d", 13)]
    [DataRow("1@ + e", "e", 5)]
    [DataRow("a + 1", "hName", -1)]
    public void ANameWrittenPastTheLimit_IsATokenOfItsText(string expression, string renamed, int refusedAt)
    {
        const int levels = 3000;
        string[] declared = ["a", "ab", "b", "c", "d", "e", "hName", "s", "x"];
        var text = string.Join("\r\n",
            [
                "Attribute VB_Name = \"Program\"",
                "Public Sub Main()",
                .. declared.Select(name => $"    Dim {name} As Variant"),
                $"    x = {new string('(', levels)}{expression}{new string(')', levels)} ' hName",
                "End Sub",
                "",
            ]);
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Program.bas"), text);
        var composition = WorkspaceSymbolResolver.ComposeWithScopes(Root, [(Program, ModuleType.StdModule, parse)], new IntrinsicSymbolResolver());
        var context = new RenameContext(composition.Resolver, composition.ScopeTree, [new RenameModule(Program, parse.SyntaxTree!, text)]);
        var declaration = new SourcePosition(2 + Array.IndexOf(declared, renamed), 8);

        var plan = LargeStackThread.Run(() => SymbolRename.Plan(context, SymbolRename.Prepare(context, Program, declaration).Target!, "z"));

        Assert.AreEqual(VBCompileErrorId.ExpressionNestedTooDeeply, (VBCompileErrorId)Assert.ContainsSingle(parse.SyntaxErrors).ErrorId);
        if (refusedAt >= 0)
        {
            Assert.AreEqual((RenameRefusalKind.Unresolved, new SourcePosition(2 + declared.Length, 8 + levels + refusedAt)),
                (plan.Refusal?.Kind, plan.Refusal?.Position));
        }
        else
        {
            Assert.IsNull(plan.Refusal?.Kind);
            Assert.AreEqual(declaration, Assert.ContainsSingle(plan.Edits).Range.Start);
        }
    }

    [TestMethod]
    public void TheHintsOfACall_AreMade()
    {
        var context = Workspace(Parse());

        var hints = LargeStackThread.Run(() => ParameterHints.In(context, Program, new SourceRange(new(0, 0), new(Lines.Length, 0))));

        Assert.AreEqual("amount", Assert.ContainsSingle(hints).Name);
    }
}
