using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Flow;
using RDCore.SDK.Semantics.Instructions;
using RDCore.SDK.Semantics.Static;
using RDCore.SDK.Semantics.Static.Abstract;
using RDCore.SDK.Workspace;
using RDCore.Symbols;

namespace RDCore.Tests.Semantics.Flow;

/// <summary>
/// Where the variables of a procedure are given a value and read, and which values are still live, or still reach, at each instruction: the data flow that tells
/// what code moved to another procedure must be given and must give back - from the definitions and uses on the code paths, never from where the names are on the page.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL 5.0.3 Semantic Analysis")]
public sealed class DataFlowTests
{
    private static readonly Uri Root = new("file://rdcore-test");

    private static Uri ModuleUri(string name) => new UriBuilder(Root) { Fragment = name }.Uri;

    private const string Others = """
        Private Type Point
            X As Long
            Y As Long
        End Type

        Private field As Long

        Private Sub Bump(ByRef v As Long)
            v = v + 1
        End Sub

        Private Function G() As Long
            G = 1
        End Function
        """;

    private sealed record class Analyzed(InstructionList Instructions, ProcedureSemanticModel Model, DefinitionsAndUses Accesses, ControlFlowGraph Graph, int FirstBodyLine)
    {
        public LiveVariables Live => LiveVariables.Of(Graph, Accesses);

        public ReachingDefinitions Reaching => ReachingDefinitions.Of(Graph, Accesses);

        public SemanticId Variable(string name)
            => Accesses.Variables.Single(variable => variable.Uri.Fragment.EndsWith($".{name}", StringComparison.OrdinalIgnoreCase));

        // the instructions of the lines of the body from `first` to `last` (zero-based, the first line of the body is 0).
        public IReadOnlySet<int> Lines(int first, int last)
            => Instructions.Items
                .Where(instruction => instruction.Location is { } location
                    && location.Range.Start.Line - FirstBodyLine >= first && location.Range.Start.Line - FirstBodyLine <= last)
                .Select(instruction => instruction.Offset)
                .ToHashSet();

        public int Line(int line) => Lines(line, line).Min();
    }

    private static Analyzed Analyze(string body, string header = "Public Sub Run(ByVal n As Long)", string end = "End Sub", bool optionExplicit = true)
    {
        var moduleLevel = Others.ReplaceLineEndings("\r\n");
        var prefix = $"Attribute VB_Name = \"Main\"\r\n{(optionExplicit ? "Option Explicit" : "' no Option Explicit")}\r\n{moduleLevel}\r\n{header}\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Main.bas"), $"{prefix}{body.ReplaceLineEndings("\r\n")}\r\n{end}\r\n");
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        var composition = WorkspaceSymbolResolver.ComposeWithScopes(Root, [(ModuleUri("Main"), ModuleType.StdModule, parse)], new IntrinsicSymbolResolver());
        Assert.IsTrue(composition.ScopeTree.TryGetScope(ModuleUri("Main.Run"), out var scope));
        var run = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single(member => member.Name == "Run");
        var block = new StatementBlock([.. run.Children]);
        var model = StatementStaticSemanticsEvaluator.Analyze(new SemanticId(ModuleUri("Main.Run")), block, kind: run.MemberKind,
            context: new StaticEvaluationContext(composition.Resolver, scope));
        Assert.IsTrue(model.IsFullyAnalyzed, string.Join("; ", model.CompileErrors.Select(error => error.ToString())));

        var instructions = InstructionListLowering.Lower(block, default, run.MemberKind).InstructionList;
        var graph = ControlFlowGraph.Of(instructions, model);
        return new Analyzed(instructions, model, DefinitionsAndUses.Of(instructions, model, composition.ScopeTree), graph, prefix.Split("\r\n").Length - 1);
    }

    // legacy Rubberduck passed x ByVal here: nothing reads it after the selection on the page, and the loop read it at the top on the next iteration. The write was lost,
    // and the loop never ended.
    [TestMethod]
    public void AVariableWrittenInALoopsBody_AndReadAtItsTop_IsLiveAfterTheBody()
    {
        var analyzed = Analyze("Dim x As Long\nDo While x < 10\nx = x + 1\nLoop");
        var x = analyzed.Variable("x");
        var body = analyzed.Lines(2, 2);

        Assert.Contains(x, analyzed.Live.LiveAfter(body), "the next iteration reads it");
        Assert.IsTrue(analyzed.Accesses.Definitions.Any(definition => definition.Variable == x && body.Contains(definition.Offset)), "and the body writes it");
        Assert.IsTrue(analyzed.Reaching.ReachingInto(body).Any(definition => definition.Variable == x), "and reads what was there before");
    }

    [TestMethod]
    public void AVariableReplacedBeforeItIsRead_IsNotLive()
    {
        var analyzed = Analyze("Dim x As Long\nx = 1\nx = 2\nDebug.Print x");

        Assert.DoesNotContain(analyzed.Variable("x"), analyzed.Live.LiveAfter(analyzed.Lines(1, 1)), "the value written is never read");
        Assert.Contains(analyzed.Variable("x"), analyzed.Live.LiveAfter(analyzed.Lines(2, 2)));
    }

    [TestMethod]
    public void AVariableReadAfterTheRegionOnSomePath_IsLive()
    {
        var analyzed = Analyze("Dim x As Long\nx = 1\nIf n > 0 Then\nDebug.Print x\nEnd If");

        Assert.Contains(analyzed.Variable("x"), analyzed.Live.LiveAfter(analyzed.Lines(1, 1)));
    }

    [TestMethod]
    public void AnArgumentPassedByReference_IsADefinition_ThatReplacesNothing()
    {
        var analyzed = Analyze("Dim x As Long\nx = 1\nBump x\nDebug.Print x");
        var x = analyzed.Variable("x");

        var passed = analyzed.Accesses.DefinitionsAt(analyzed.Line(2)).Single(definition => definition.Variable == x);
        Assert.AreEqual(VariableDefinitionKind.ByRefArgument, passed.Kind);
        Assert.IsFalse(passed.IsComplete, "the procedure may not write it");
        Assert.IsTrue(analyzed.Accesses.UsesAt(analyzed.Line(2)).Any(use => use.Variable == x), "and it may read it");
        CollectionAssert.AreEquivalent(new[] { analyzed.Line(1), analyzed.Line(2) },
            analyzed.Reaching.ReachingBefore(analyzed.Line(3), x).Select(definition => definition.Offset).ToArray(), "what was there may still be");
    }

    [TestMethod]
    [DataRow("Bump (x)", DisplayName = "in parentheses: its value is passed")]
    [DataRow("Debug.Print G() + x", DisplayName = "an operand")]
    public void AnArgumentPassedByValue_IsOnlyRead(string statement)
    {
        var analyzed = Analyze($"Dim x As Long\n{statement}");
        var x = analyzed.Variable("x");

        Assert.IsFalse(analyzed.Accesses.DefinitionsAt(analyzed.Line(1)).Any(definition => definition.Variable == x));
        Assert.IsTrue(analyzed.Accesses.UsesAt(analyzed.Line(1)).Any(use => use.Variable == x));
    }

    [TestMethod]
    [DataRow("Dim a(1 To 3) As Long\na(2) = 5", "a", DisplayName = "an element of an array")]
    [DataRow("Dim p As Point\np.X = 5", "p", DisplayName = "a member of a Type")]
    [DataRow("Dim p As Point\nWith p\n.Y = 5\nEnd With", "p", DisplayName = "a member of a Type, in a With block")]
    [DataRow("Dim s As String\ns = \"abc\"\nMid(s, 2, 1) = \"x\"", "s", DisplayName = "a part of a string")]
    [DataRow("Dim a() As Long\nReDim a(3)\nReDim Preserve a(5)", "a", DisplayName = "ReDim Preserve")]
    public void AWriteToAPart_IsADefinition_ThatKeepsTheRest(string body, string name)
    {
        var analyzed = Analyze(body);
        var variable = analyzed.Variable(name);
        var last = analyzed.Instructions.Items.Last(instruction => analyzed.Accesses.DefinitionsAt(instruction.Offset).Any(definition => definition.Variable == variable)).Offset;

        var write = analyzed.Accesses.DefinitionsAt(last).Single(definition => definition.Variable == variable);
        Assert.IsFalse(write.IsComplete);
        Assert.Contains(variable, analyzed.Live.LiveBefore(last), "the rest of the value is what was there");
    }

    [TestMethod]
    [DataRow("Dim a() As Long\nReDim a(3)", VariableDefinitionKind.Redimension, DisplayName = "ReDim")]
    [DataRow("Dim a() As Long\nErase a", VariableDefinitionKind.Redimension, DisplayName = "Erase")]
    [DataRow("Dim s As String\nLine Input #1, s", VariableDefinitionKind.FileInput, DisplayName = "Line Input #")]
    [DataRow("Dim s As String\nInput #1, s", VariableDefinitionKind.FileInput, DisplayName = "Input #")]
    [DataRow("Dim s As String\nGet #1, , s", VariableDefinitionKind.FileInput, DisplayName = "Get #")]
    [DataRow("Dim s As String\nLSet s = \"x\"", VariableDefinitionKind.Assignment, DisplayName = "LSet")]
    public void AStatementThatReplacesTheValue_IsACompleteDefinition(string body, VariableDefinitionKind kind)
    {
        var analyzed = Analyze(body);
        var variable = analyzed.Accesses.Variables.Single();
        var at = analyzed.Instructions.Items.Last().Offset;

        var definition = analyzed.Accesses.DefinitionsAt(at).Single(candidate => candidate.Variable == variable);
        Assert.AreEqual(kind, definition.Kind);
        Assert.IsTrue(definition.IsComplete);
        Assert.DoesNotContain(variable, analyzed.Live.LiveBefore(at), "what was there before is not read");
    }

    [TestMethod]
    public void AMemberOfAnObject_IsTheObjectsToWrite_AndAReadOfTheVariable()
    {
        var analyzed = Analyze("Dim c As Collection\nSet c = New Collection\nc.Add 1");
        var c = analyzed.Variable("c");

        Assert.IsFalse(analyzed.Accesses.DefinitionsAt(analyzed.Line(2)).Any(definition => definition.Variable == c));
        Assert.IsTrue(analyzed.Accesses.UsesAt(analyzed.Line(2)).Any(use => use.Variable == c));
    }

    [TestMethod]
    public void TheCounterOfAForLoop_IsDefinedByTheLoop_AndLiveAfterIt()
    {
        var analyzed = Analyze("Dim i As Long\nFor i = 1 To n\nDebug.Print i\nNext\nDebug.Print i");
        var i = analyzed.Variable("i");

        Assert.IsTrue(analyzed.Accesses.DefinitionsAt(analyzed.Line(1)).Any(definition => definition is { Kind: VariableDefinitionKind.LoopControl, IsComplete: true } && definition.Variable == i));
        var next = analyzed.Instructions.Items.Single(instruction => instruction.Kind is InstructionKind.ForNext).Offset;
        Assert.IsTrue(analyzed.Accesses.DefinitionsAt(next).Any(definition => definition.Variable == i), "Next advances it");
        Assert.IsTrue(analyzed.Accesses.UsesAt(next).Any(use => use.Variable == i), "from what it was");
        Assert.Contains(i, analyzed.Live.LiveAfter(analyzed.Lines(2, 2)), "the next iteration reads it");
    }

    [TestMethod]
    public void TheResultOfAFunction_IsWritten_AndOutlivesTheActivation()
    {
        var analyzed = Analyze("Run = n * 2", header: "Public Function Run(ByVal n As Long) As Long", end: "End Function");
        var result = analyzed.Model.Procedure;

        Assert.IsTrue(analyzed.Accesses.DefinitionsAt(analyzed.Line(0)).Any(definition => definition.Variable == result && definition.IsComplete));
        Assert.Contains(result, analyzed.Accesses.OutliveTheActivation);
        Assert.Contains(result, analyzed.Live.LiveAfter(analyzed.Lines(0, 0)), "the caller reads it");
    }

    [TestMethod]
    public void WhatOutlivesTheActivation_IsLiveAtItsEnd()
    {
        var analyzed = Analyze("Static kept As Long\nDim temporary As Long\nr = 1\nv = 1\nkept = 1\ntemporary = 1\nfield = 1",
            header: "Public Sub Run(ByRef r As Long, ByVal v As Long)");

        var live = analyzed.Live.LiveBefore(analyzed.Graph.Exit);
        CollectionAssert.AreEquivalent(new[] { analyzed.Variable("r"), analyzed.Variable("kept"), analyzed.Variable("field") }, live.ToArray());
    }

    [TestMethod]
    public void TheLocalsOfAStaticSub_OutliveTheActivation()
    {
        var analyzed = Analyze("Dim kept As Long\nkept = kept + 1", header: "Public Static Sub Run(ByVal n As Long)");

        CollectionAssert.AreEquivalent(new[] { analyzed.Variable("kept") }, analyzed.Live.LiveBefore(analyzed.Graph.Exit).ToArray(), "the next call reads it");
    }

    [TestMethod]
    public void TheElementsOfAParamArray_AreTheCallers()
    {
        var analyzed = Analyze("rest(0) = 1", header: "Public Sub Run(ParamArray rest() As Variant)");

        Assert.Contains(analyzed.Variable("rest"), analyzed.Accesses.OutliveTheActivation);
        Assert.Contains(analyzed.Variable("rest"), analyzed.Live.LiveAfter(analyzed.Lines(0, 0)), "the caller reads what is written to them");
    }

    [TestMethod]
    public void AnUndeclaredVariable_IsAVariableAsWell()
    {
        // with no Option Explicit, a name assigned is a local declared by the assignment, a Variant.
        var analyzed = Analyze("total = n\ntotal = total + 1\nDebug.Print total", optionExplicit: false);
        var total = analyzed.Variable("total");

        Assert.IsTrue(analyzed.Accesses.DefinitionsAt(analyzed.Line(0)).Any(definition => definition.Variable == total && definition.IsComplete));
        Assert.Contains(total, analyzed.Live.LiveAfter(analyzed.Lines(1, 1)));
        Assert.DoesNotContain(total, analyzed.Accesses.OutliveTheActivation, "it is a local of the procedure");
    }

    [TestMethod]
    public void AnInstructionThatRaisesIntoAHandler_ReplacesNothing_OnTheWayThere()
    {
        var analyzed = Analyze("Dim x As Long\nOn Error GoTo Handler\nx = 1\nx = G()\nExit Sub\nHandler:\nDebug.Print x");
        var x = analyzed.Variable("x");
        var handler = analyzed.Line(6);

        CollectionAssert.Contains(analyzed.Reaching.ReachingBefore(handler, x).Select(definition => definition.Offset).ToArray(), analyzed.Line(2),
            "x = G() may fail before it assigns");
        Assert.Contains(x, analyzed.Live.LiveAfter(analyzed.Lines(2, 2)), "so the handler may read what x = 1 wrote");
    }

    [TestMethod]
    public void UnderOnErrorResumeNext_AnAssignmentMayNotHappen()
    {
        var analyzed = Analyze("Dim x As Long\nx = 1\nOn Error Resume Next\nx = G()\nDebug.Print x");
        var x = analyzed.Variable("x");

        CollectionAssert.Contains(analyzed.Reaching.ReachingBefore(analyzed.Line(4), x).Select(definition => definition.Offset).ToArray(), analyzed.Line(1));
    }

    [TestMethod]
    public void AResume_RunsTheFaultStatementAgain_WhichReadsWhatTheHandlerWrote()
    {
        var analyzed = Analyze("Dim d As Long\nOn Error GoTo Handler\nDebug.Print n / d\nExit Sub\nHandler:\nd = 1\nResume");
        var d = analyzed.Variable("d");

        Assert.Contains(d, analyzed.Live.LiveAfter(analyzed.Lines(5, 5)), "Resume executes the division again");
    }

    [TestMethod]
    public void EveryVariable_HasADefinitionAtTheStart()
    {
        var analyzed = Analyze("Dim x As Long\nDebug.Print x + n");

        foreach (var variable in new[] { analyzed.Variable("x"), analyzed.Variable("n") })
        {
            var reaching = analyzed.Reaching.ReachingBefore(analyzed.Line(1), variable).Single();
            Assert.AreEqual(VariableDefinitionKind.Entry, reaching.Kind, "the initial value, and the argument");
        }
    }
}
