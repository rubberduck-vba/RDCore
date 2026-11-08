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
/// The cases a review of the flow analysis found it wrong on: each asserts the sound answer, which a missing edge or a missing definition loses.
/// </summary>
[TestClass]
public sealed class FlowSoundnessTests
{
    private static readonly Uri Root = new("file://rdcore-test");

    private static Uri ModuleUri(string name) => new UriBuilder(Root) { Fragment = name }.Uri;

    private const string Others = """
        Private Type Point
            X As Long
            Y As Long
        End Type

        Private Type Outer
            Inner As Point
            Arr(1 To 3) As Long
        End Type

        Private field As Long

        Private Sub Bump(ByRef v As Long)
            v = v + 1
        End Sub

        Private Sub TakesByVal(ByVal v As Long)
        End Sub

        Private Sub ShowField()
            Debug.Print field
        End Sub

        Private Function G() As Long
            G = 1
        End Function

        Private Function F(ByRef v As Long) As Long
            v = v + 1
            F = v
        End Function
        """;

    private sealed record class Analyzed(InstructionList Instructions, ProcedureSemanticModel Model, DefinitionsAndUses Accesses, ControlFlowGraph Graph, int FirstBodyLine)
    {
        public LiveVariables Live => LiveVariables.Of(Graph, Accesses);

        public ReachingDefinitions Reaching => ReachingDefinitions.Of(Graph, Accesses);

        public ErrorHandlingStates States => ErrorHandlingStates.Of(Graph);

        public SemanticId Variable(string name)
            => Accesses.Variables.Single(variable => variable.Uri.Fragment.EndsWith($".{name}", StringComparison.OrdinalIgnoreCase));

        public IReadOnlySet<int> Lines(int first, int last)
            => Instructions.Items
                .Where(instruction => instruction.Location is { } location
                    && location.Range.Start.Line - FirstBodyLine >= first && location.Range.Start.Line - FirstBodyLine <= last)
                .Select(instruction => instruction.Offset)
                .ToHashSet();

        public int Line(int line) => Lines(line, line).Min();

        public bool Defines(int line, SemanticId variable) => Lines(line, line).Any(offset => Accesses.DefinitionsAt(offset).Any(definition => definition.Variable == variable));

        // read by the instruction itself: not live because some other path, an error's, keeps it so.
        public bool Uses(int line, SemanticId variable) => Lines(line, line).Any(offset => Accesses.UsesAt(offset).Any(use => use.Variable == variable));
    }

    private static Analyzed Analyze(string body, string header = "Public Sub Run(ByVal n As Long)", string end = "End Sub", string others = Others, ModuleType moduleType = ModuleType.StdModule)
    {
        var moduleLevel = others.ReplaceLineEndings("\r\n");
        var attributes = moduleType is ModuleType.ClassModule
            ? "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\nAttribute VB_Name = \"Main\"\r\n"
            : "Attribute VB_Name = \"Main\"\r\n";
        var prefix = $"{attributes}Option Explicit\r\n{moduleLevel}\r\n{header}\r\n";
        var extension = moduleType is ModuleType.ClassModule ? "cls" : "bas";
        var parse = new ModuleParser().Parse(new Uri($"file:///c:/ws/Main.{extension}"), $"{prefix}{body.ReplaceLineEndings("\r\n")}\r\n{end}\r\n");
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        var composition = WorkspaceSymbolResolver.ComposeWithScopes(Root, [(ModuleUri("Main"), moduleType, parse)], new IntrinsicSymbolResolver());
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

    // the module or project that qualifies a member is no value and was never evaluated, so it had no fact: a procedure that named a member through one was
    // not analyzed whole, and no flow analysis of it ran at all.
    [TestMethod]
    [DataRow("Debug.Print Strings.Len(\"x\")", DisplayName = "a library module")]
    [DataRow("Debug.Print VBA.Strings.Len(\"x\")", DisplayName = "a library and its module")]
    [DataRow("Debug.Print Main.field", DisplayName = "the procedure's own module")]
    public void AProcedureThatQualifiesAMemberByItsModule_IsAnalyzedWhole(string body)
    {
        var analyzed = Analyze(body, others: PublicField);
        Assert.IsTrue(analyzed.Model.IsFullyAnalyzed);
    }

    // ---------------- soundness: errors ----------------

    [TestMethod]
    public void S01_AnErrorRaisedToTheCaller_ExposesAByRefParameter_AnExpressionThatMayFail()
    {
        // Run's caller, with a handler, sees r = 1 when 10 \ n divides by zero. No call: a call reads whatever outlives the activation, and would keep r live
        // whatever the exit edge does.
        var analyzed = Analyze("r = 1\nr = 10 \\ n", header: "Public Sub Run(ByRef r As Long, ByVal n As Long)");
        Assert.Contains(analyzed.Variable("r"), analyzed.Live.LiveAfter(analyzed.Lines(0, 0)));
    }

    [TestMethod]
    public void S02_AnErrorStatementRaisedToTheCaller_ExposesAByRefParameter()
    {
        var analyzed = Analyze("r = 1\nError 5", header: "Public Sub Run(ByRef r As Long)");
        Assert.Contains(analyzed.Variable("r"), analyzed.Live.LiveAfter(analyzed.Lines(0, 0)));
    }

    [TestMethod]
    public void S03_AnErrorStatementUnderResumeNext_ContinuesAtTheNextStatement()
    {
        var analyzed = Analyze("Dim x As Long\nOn Error Resume Next\nx = 1\nError 5\nDebug.Print x");
        Assert.Contains(analyzed.Variable("x"), analyzed.Live.LiveAfter(analyzed.Lines(2, 2)));
    }

    [TestMethod]
    public void S04_OnErrorGoToMinusOne_KeepsTheHandler_Liveness()
    {
        // Excel-verified: inside handler H, On Error GoTo -1 then a raise goes to H again.
        var analyzed = Analyze("Dim x As Long\nOn Error GoTo H\nx = G()\nExit Sub\nH:\nDebug.Print x\nOn Error GoTo -1\nx = 1\nx = G()\nExit Sub");
        Assert.Contains(analyzed.Variable("x"), analyzed.Live.LiveAfter(analyzed.Lines(7, 7)), "x = G() may raise into H, which reads x");
    }

    [TestMethod]
    public void S05_OnErrorGoToMinusOne_KeepsTheHandler_States()
    {
        var analyzed = Analyze("Dim x As Long\nOn Error GoTo H\nx = G()\nExit Sub\nH:\nDebug.Print x\nOn Error GoTo -1\nx = 1\nx = G()\nExit Sub");
        Assert.IsNotEmpty(analyzed.States.HandlersAt(analyzed.Line(8)), "H is still the handler");
    }

    [TestMethod]
    public void S06_StatementsReachedOnlyByResumeNext_AreProtectedByTheHandler()
    {
        var analyzed = Analyze("Dim x As Long\nOn Error GoTo H\nError 5\nx = 1\nx = G()\nExit Sub\nH:\nDebug.Print x\nResume Next");
        Assert.IsNotEmpty(analyzed.States.HandlersAt(analyzed.Line(4)), "the states know H is in effect");
        Assert.Contains(analyzed.Variable("x"), analyzed.Live.LiveAfter(analyzed.Lines(3, 3)), "x = G() may raise into H, which reads x");
    }

    [TestMethod]
    public void S07_AnErrorInsideTheHandler_ExposesAByRefParameterToTheCaller()
    {
        var analyzed = Analyze("On Error GoTo H\nr = 10 \\ n\nExit Sub\nH:\nr = 1\nr = 10 \\ n", header: "Public Sub Run(ByRef r As Long, ByVal n As Long)");
        Assert.Contains(analyzed.Variable("r"), analyzed.Live.LiveAfter(analyzed.Lines(4, 4)));
    }

    [TestMethod]
    public void S08_AnErrorInsideTheHandler_LeavesTheActivation()
    {
        var analyzed = Analyze("On Error GoTo H\nr = 10 \\ n\nExit Sub\nH:\nr = 10 \\ n", header: "Public Sub Run(ByRef r As Long, ByVal n As Long)");
        var edges = analyzed.States.Edges.SuccessorsOf(analyzed.Line(4));
        Assert.Contains(new ControlFlowEdge(analyzed.Line(4), analyzed.Graph.Exit, ControlFlowEdgeKind.Error), edges, "while the handler runs, an error is raised to the caller");
    }

    [TestMethod]
    public void S09_AnErrorStatementUnderResumeNext_ReachesTheNextStatement_InTheSameState()
    {
        // `Error 5` never completes, so the next statement is reached only by its error edge - and was unreachable.
        var analyzed = Analyze("Dim x As Long\nOn Error Resume Next\nError 5\nx = 1\nx = 10 \\ n\nDebug.Print x");
        Assert.IsNotEmpty(analyzed.States.At(analyzed.Line(3)), "x = 1 is reached");
        Assert.Contains(analyzed.Variable("x"), analyzed.Live.LiveAfter(analyzed.Lines(3, 3)), "10 \\ n may fail, and Debug.Print reads x = 1");
    }

    // a Return with no GoSub raises error 3, a Resume with no active error raises error 20.
    [TestMethod]
    [DataRow("Return", DisplayName = "a Return")]
    [DataRow("Resume Next", DisplayName = "a Resume Next")]
    public void S0A_AReturnOrAResume_CanRaise(string statement)
    {
        var analyzed = Analyze($"Dim x As Long\nOn Error Resume Next\nx = 1\n{statement}\nDebug.Print x");
        Assert.Contains(analyzed.Variable("x"), analyzed.Live.LiveAfter(analyzed.Lines(2, 2)), $"{statement} raises, and Resume Next goes on to read x");
    }

    // measured in MS-VBA (Excel-verified): an error in the expression of a Select Case goes on after End Select, with no Case run, the Case Else
    // included - as RD-VBA's runtime does.
    [TestMethod]
    public void S0B_AnErrorInTheExpressionOfASelectCase_UnderResumeNext_GoesOnAtEndSelect()
    {
        var analyzed = Analyze("Dim x As Long\nOn Error Resume Next\nSelect Case 10 \\ n\nCase Else\nx = 2\nEnd Select\nDebug.Print x");
        Assert.IsTrue(analyzed.Reaching.ReachingBefore(analyzed.Line(6), analyzed.Variable("x")).Any(definition => definition.Kind is VariableDefinitionKind.Entry),
            "the Case Else is skipped, and x is still 0");
    }

    // where Resume Next goes on after an error in a condition, as MS-VBA has it (Excel-verified): the line it raises on, and the line it goes on at, zero-based.
    [TestMethod]
    [DataRow("Select Case 10 \\ n\nCase 1\nx = 1\nCase Else\nx = 2\nEnd Select\nDebug.Print x", 0, 6, DisplayName = "the expression of a Select Case: after End Select")]
    [DataRow("Select Case n\nCase 10 \\ n\nx = 1\nCase Else\nx = 2\nEnd Select\nDebug.Print x", 1, 2, DisplayName = "a Case expression: that Case's body")]
    [DataRow("If 10 \\ n Then\nx = 1\nElse\nx = 2\nEnd If\nDebug.Print x", 0, 1, DisplayName = "an If condition: the Then branch")]
    [DataRow("If n Then\nx = 1\nElseIf 10 \\ n Then\nx = 2\nEnd If\nDebug.Print x", 2, 3, DisplayName = "an ElseIf condition: that branch")]
    [DataRow("Do While 10 \\ n\nx = 1\nLoop\nDebug.Print x", 0, 1, DisplayName = "a Do While condition: the body")]
    [DataRow("Do Until 10 \\ n\nx = 1\nLoop\nDebug.Print x", 0, 1, DisplayName = "a Do Until condition: the body")]
    [DataRow("While 10 \\ n\nx = 1\nWend\nDebug.Print x", 0, 1, DisplayName = "a While condition: the body")]
    // a post-test loop's condition is its LoopBack, which is of the loop statement and written where it starts: -1 names it.
    [DataRow("Do\nx = 1\nLoop Until 10 \\ n\nDebug.Print x", -1, 3, DisplayName = "a Loop Until condition: out of the loop")]
    [DataRow("Do\nx = 1\nLoop While 10 \\ n\nDebug.Print x", -1, 3, DisplayName = "a Loop While condition: out of the loop")]
    public void S0C_AnErrorInACondition_UnderResumeNext_GoesOnWhereMsVbaGoesOn(string code, int raises, int goesOn)
    {
        var analyzed = Analyze($"Dim x As Long\nOn Error Resume Next\n{code}");
        var at = raises < 0
            ? analyzed.Instructions.Items.Single(instruction => instruction.Kind is InstructionKind.LoopBack).Offset
            : analyzed.Instructions.Items.First(instruction => instruction.Location?.Range.Start.Line - analyzed.FirstBodyLine == raises + 2 && instruction.Node is not null).Offset;
        var errors = analyzed.States.Edges.SuccessorsOf(at).Where(edge => edge.Kind is ControlFlowEdgeKind.Error).Select(edge => edge.To).ToArray();
        CollectionAssert.AreEqual(new[] { analyzed.Line(goesOn + 2) }, errors);
    }

    // Excel-verified: an error in the header of a For goes on at the first statement of the body, untested, with the counter as it was (i keeps 7).
    [TestMethod]
    [DataRow("For i = 10 \\ n To 2", DisplayName = "the start")]
    [DataRow("For i = 1 To 10 \\ n", DisplayName = "the end")]
    [DataRow("For i = 1 To 3 Step 10 \\ n", DisplayName = "the step")]
    public void S0D_AnErrorInTheHeaderOfAFor_UnderResumeNext_EntersTheBody_WithTheCounterAsItWas(string header)
    {
        var analyzed = Analyze($"Dim i As Long\nOn Error Resume Next\ni = 7\n{header}\nDebug.Print i\nNext");
        var errors = analyzed.States.Edges.SuccessorsOf(analyzed.Line(3)).Where(edge => edge.Kind is ControlFlowEdgeKind.Error).Select(edge => edge.To).ToArray();

        CollectionAssert.AreEqual(new[] { analyzed.Line(4) }, errors, "the body, never past Next");
        Assert.IsTrue(analyzed.Reaching.ReachingBefore(analyzed.Line(4), analyzed.Variable("i")).Any(definition => definition.Offset == analyzed.Line(2)),
            "i = 7 reaches the body: the counter is not assigned");
    }

    // ---------------- soundness: definitions ----------------

    [TestMethod]
    public void S10_AnArgumentOfRaiseEvent_IsADefinition()
    {
        const string cls = """
            Public Event Changing(ByRef Cancel As Boolean)
            """;
        var analyzed = Analyze("Dim cancel As Boolean\nRaiseEvent Changing(cancel)\nIf cancel Then Exit Sub", others: cls, moduleType: ModuleType.ClassModule);
        Assert.IsTrue(analyzed.Defines(1, analyzed.Variable("cancel")), "a handler of the event may write it");
    }

    [TestMethod]
    public void S11_AMemberOfAnElementOfAnArrayOfTypes_IsAPartialDefinition()
    {
        var analyzed = Analyze("Dim a(1 To 3) As Point\na(1).X = 5\nDebug.Print a(1).X");
        Assert.IsTrue(analyzed.Defines(1, analyzed.Variable("a")));
    }

    [TestMethod]
    public void S12_AnElementOfAnArrayMemberOfAType_IsAPartialDefinition()
    {
        var analyzed = Analyze("Dim o As Outer\no.Arr(1) = 5\nDebug.Print o.Arr(1)");
        Assert.IsTrue(analyzed.Defines(1, analyzed.Variable("o")));
    }

    [TestMethod]
    public void S13_AMemberOfAnElement_InAWithBlock_IsAPartialDefinition()
    {
        var analyzed = Analyze("Dim a(1 To 3) As Point\nWith a(1)\n.X = 5\nEnd With\nDebug.Print a(1).X");
        Assert.IsTrue(analyzed.Defines(2, analyzed.Variable("a")));
    }

    [TestMethod]
    public void S14_AReadOfAMember_InAWithBlockOnAType_IsAUse()
    {
        var analyzed = Analyze("Dim p As Point\nWith p\n.Y = 1\nDebug.Print .Y\nEnd With");
        Assert.Contains(analyzed.Variable("p"), analyzed.Live.LiveAfter(analyzed.Lines(2, 2)), "Debug.Print .Y reads p.Y");
    }

    [TestMethod]
    public void S15_NestedWith_OnAMemberOfAType_DoesNotRecurseForever()
    {
        var analyzed = Analyze("Dim o As Outer\nWith o\nWith .Inner\n.X = 1\nEnd With\nEnd With");
        Assert.IsTrue(analyzed.Defines(3, analyzed.Variable("o")));
    }

    [TestMethod]
    public void S16_NestedWith_OnAnObject_DoesNotRecurseForever()
    {
        var analyzed = Analyze("Dim c As Collection\nSet c = New Collection\nWith c\nWith .Item(1)\n.Foo = 1\nEnd With\nEnd With");
        Assert.IsNotNull(analyzed.Accesses);
    }

    [TestMethod]
    public void S17_ALateBoundArgument_IsADefinition()
    {
        var analyzed = Analyze("Dim o As Object\nDim x As Long\no.Foo x\nDebug.Print x");
        Assert.IsTrue(analyzed.Defines(2, analyzed.Variable("x")));
    }

    [TestMethod]
    public void S18_AnArgumentOfANestedCall_IsADefinition()
    {
        var analyzed = Analyze("Dim x As Long\nBump F(x)\nDebug.Print x");
        Assert.IsTrue(analyzed.Defines(1, analyzed.Variable("x")));
    }

    [TestMethod]
    public void S19_TheResult_ReadAndWritten_IsLiveAfterAnEarlierWrite()
    {
        var analyzed = Analyze("Run = 1\nRun = Run + 1", header: "Public Function Run() As Long", end: "End Function");
        Assert.Contains(analyzed.Model.Procedure, analyzed.Live.LiveAfter(analyzed.Lines(0, 0)));
        Assert.IsTrue(analyzed.Accesses.UsesAt(analyzed.Line(1)).Any(use => use.Variable == analyzed.Model.Procedure), "Run + 1 reads the result");
    }

    [TestMethod]
    public void S20_AFieldReadByACallee_IsLive()
    {
        var analyzed = Analyze("field = 1\nShowField\nfield = 2");
        Assert.Contains(analyzed.Variable("field"), analyzed.Live.LiveAfter(analyzed.Lines(0, 0)), "ShowField reads it");
    }

    // what is written in the argument list of a function that returns an array is an argument, not an index.
    [TestMethod]
    public void S22_AnArgumentOfAFunctionThatReturnsAnArray_IsADefinition()
    {
        const string parse = Others + "\n\nPrivate Function Parse(ByRef pos As Long) As String()\n    pos = pos + 1\nEnd Function";
        var analyzed = Analyze("Dim parts() As String\nDim p As Long\nparts = Parse(p)\nDebug.Print p", others: parse);
        Assert.IsTrue(analyzed.Defines(2, analyzed.Variable("p")));
    }

    // MS-VBAL §5.3.1.2: every local variable of a Static procedure has module extent, as if it were declared Static: its value outlives the activation.
    [TestMethod]
    [DataRow("Public Static Sub Run(ByVal n As Long)", "End Sub", DisplayName = "a Static Sub")]
    [DataRow("Public Static Function Run(ByVal n As Long) As Long", "End Function", DisplayName = "a Static Function")]
    [DataRow("Static Sub Run(ByVal n As Long)", "End Sub", DisplayName = "a Static Sub with no access modifier")]
    public void S28_ALocalOfAStaticProcedure_OutlivesTheActivation(string header, string end)
    {
        var analyzed = Analyze("Dim k As Long\nk = k + n", header: header, end: end);
        Assert.Contains(analyzed.Variable("k"), analyzed.Accesses.OutliveTheActivation);
        Assert.DoesNotContain(analyzed.Variable("n"), analyzed.Accesses.OutliveTheActivation, "a ByVal parameter is not one of them");
    }

    // the second review's 3: a function that returns an array and has no parameters is called with none, and what is written after its name indexes the result.
    [TestMethod]
    public void S26_AnIndexOfWhatAParameterlessArrayFunctionReturns_IsNoDefinition()
    {
        const string rows = Others + "\n\nPrivate Function Rows() As Long()\nEnd Function";
        var analyzed = Analyze("Dim x As Long\nDim i As Long\nx = Rows(i)\nDebug.Print i", others: rows);
        Assert.IsFalse(analyzed.Defines(2, analyzed.Variable("i")));
    }

    // the second review's 2: an assignment of a literal that converts to the variable's type cannot raise (MS-VBAL §5.5.1.2); anything else may.
    [TestMethod]
    [DataRow("Dim x As Long\nx = 1", false, DisplayName = "a number to a Long")]
    [DataRow("Dim i As Integer\ni = -32768", false, DisplayName = "the least Integer")]
    [DataRow("Dim i As Integer\ni = True", false, DisplayName = "True to an Integer, which is -1")]
    [DataRow("Dim b As Byte\nb = 254.5", false, DisplayName = "a number that rounds to an even Byte")]
    [DataRow("Dim s As String\ns = 1", false, DisplayName = "a number to a String")]
    [DataRow("Dim v As Variant\nv = \"x\"", false, DisplayName = "a string to a Variant")]
    [DataRow("Dim b As Byte\nb = 300", true, DisplayName = "a number a Byte does not hold")]
    [DataRow("Dim b As Byte\nb = 255.5", true, DisplayName = "a number that rounds past a Byte")]
    [DataRow("Dim b As Byte\nb = True", true, DisplayName = "True to a Byte, which is -1")]
    [DataRow("Dim k As Long\nk = \"1\"", true, DisplayName = "a string to a number")]
    [DataRow("Dim x As Long\nx = G()", true, DisplayName = "a call")]
    [DataRow("Dim x As Long\nx = 1 + 1", true, DisplayName = "an expression that is not a literal")]
    public void S27_AnAssignmentOfALiteral_CanRaise_OnlyWhereTheTypeMayNotHoldIt(string body, bool canRaise)
    {
        var analyzed = Analyze(body);
        Assert.AreEqual(canRaise, analyzed.Graph.CanRaise(analyzed.Line(1)));
    }

    // a field named through its module, or through Me, is the field.
    [TestMethod]
    [DataRow("field = 1\nDebug.Print Main.field\nfield = 2", DisplayName = "read through the module")]
    public void S23_AFieldReadThroughItsModule_IsAUse(string body)
    {
        var analyzed = Analyze(body, others: PublicField);
        Assert.IsTrue(analyzed.Uses(1, analyzed.Variable("field")));
    }

    // a member of the module, as its qualified name, or Me, sees it: a Public one.
    private const string PublicField = "Public field As Long";

    [TestMethod]
    public void S24_AFieldWrittenThroughItsModule_IsADefinition()
    {
        var analyzed = Analyze("Main.field = 1\nDebug.Print field", others: PublicField);
        Assert.IsTrue(analyzed.Defines(0, analyzed.Variable("field")));
    }

    [TestMethod]
    public void S25_AFieldReadAndWrittenThroughMe_IsTheField()
    {
        var analyzed = Analyze("field = 1\nDebug.Print Me.field\nMe.field = 2", others: PublicField, moduleType: ModuleType.ClassModule);
        Assert.IsTrue(analyzed.Uses(1, analyzed.Variable("field")));
        Assert.IsTrue(analyzed.Defines(2, analyzed.Variable("field")));
    }

    [TestMethod]
    public void S21_AnArgumentOfADefaultMemberCall_OnAnObject_IsADefinition()
    {
        // Collection.Item(index): index is ByRef Variant in the type library.
        var analyzed = Analyze("Dim c As Collection\nDim k As Variant\nSet c = New Collection\nDebug.Print c(k)\nDebug.Print k");
        Assert.IsTrue(analyzed.Defines(3, analyzed.Variable("k")));
    }

    // ---------------- soundness: control flow ----------------

    [TestMethod]
    public void S30_GoSub_TheReturnGoesBackAfterTheSite()
    {
        var analyzed = Analyze("Dim x As Long\nGoSub S\nDebug.Print x\nExit Sub\nS:\nx = 1\nReturn");
        Assert.Contains(analyzed.Variable("x"), analyzed.Live.LiveAfter(analyzed.Lines(5, 5)));
    }

    [TestMethod]
    public void S31_OnGoSub_TheReturnGoesBackAfterTheSite()
    {
        var analyzed = Analyze("Dim x As Long\nOn n GoSub S, T\nDebug.Print x\nExit Sub\nS:\nx = 1\nReturn\nT:\nReturn");
        Assert.Contains(analyzed.Variable("x"), analyzed.Live.LiveAfter(analyzed.Lines(5, 5)));
    }

    [TestMethod]
    public void S32_OnGoTo_EachTarget()
    {
        var analyzed = Analyze("Dim x As Long\nx = 1\nOn n GoTo A, B\nExit Sub\nA:\nExit Sub\nB:\nDebug.Print x");
        Assert.Contains(analyzed.Variable("x"), analyzed.Live.LiveAfter(analyzed.Lines(1, 1)));
    }

    [TestMethod]
    public void S33_SelectCase_ACaseThatDoesNotAssign()
    {
        var analyzed = Analyze("Dim x As Long\nx = 1\nSelect Case n\nCase 1\nx = 2\nCase Else\nEnd Select\nDebug.Print x");
        Assert.Contains(analyzed.Variable("x"), analyzed.Live.LiveAfter(analyzed.Lines(1, 1)));
    }

    [TestMethod]
    public void S34_ExitDo_FromANestedFor()
    {
        var analyzed = Analyze("Dim x As Long, i As Long\nDo\nFor i = 1 To n\nx = x + i\nIf x > 5 Then Exit Do\nNext\nLoop\nDebug.Print x");
        Assert.Contains(analyzed.Variable("x"), analyzed.Live.LiveAfter(analyzed.Lines(3, 3)));
    }

    [TestMethod]
    public void S35_ABackEdgeToALineNumber()
    {
        var analyzed = Analyze("Dim x As Long\n10 x = x + 1\nIf x < n Then GoTo 10\nDebug.Print \"done\"");
        Assert.Contains(analyzed.Variable("x"), analyzed.Live.LiveAfter(analyzed.Lines(1, 1)));
    }

    [TestMethod]
    public void S36_ForEach_TheElementAfterTheLoop()
    {
        var analyzed = Analyze("Dim v As Variant\nDim c As Collection\nSet c = New Collection\nv = 0\nFor Each v In c\nNext\nDebug.Print v");
        Assert.Contains(analyzed.Variable("v"), analyzed.Live.LiveAfter(analyzed.Lines(3, 3)), "an empty collection assigns nothing");
    }

    [TestMethod]
    public void S37_Stop_ContinuesAfterIt()
    {
        var analyzed = Analyze("Dim x As Long\nx = 1\nStop\nDebug.Print x");
        Assert.Contains(analyzed.Variable("x"), analyzed.Live.LiveAfter(analyzed.Lines(1, 1)));
    }

    [TestMethod]
    public void S38_ReachingInto_ALoopBody_FromTheBodyItself()
    {
        var analyzed = Analyze("Dim x As Long, i As Long\nFor i = 1 To n\nDebug.Print x\nx = i\nNext");
        var body = analyzed.Lines(2, 3);
        Assert.IsTrue(analyzed.Reaching.ReachingInto(body).Any(definition => definition.Variable == analyzed.Variable("x")));
        Assert.Contains(analyzed.Variable("x"), analyzed.Live.LiveAfter(body), "the next iteration reads it");
    }

    // ---------------- precision ----------------

    // precision, not soundness: an argument counted as a possible definition makes a variable ByRef that could be ByVal.
    [TestMethod]
    [Ignore("PassedAsArgument does not consult the parameter, so an argument of a ByVal parameter counts as a possible definition")]
    public void P01_AnArgumentForAByValParameter_IsNoDefinition()
    {
        var analyzed = Analyze("Dim x As Long\nTakesByVal x");
        Assert.IsFalse(analyzed.Defines(1, analyzed.Variable("x")));
    }

    [TestMethod]
    [Ignore("PassedAsArgument does not consult the parameter, so an argument of a library function taken ByVal counts as a possible definition")]
    public void P02_AnArgumentOfAStdlibFunction_IsNoDefinition()
    {
        var analyzed = Analyze("Dim s As String\nDebug.Print Len(s)");
        Assert.IsFalse(analyzed.Defines(1, analyzed.Variable("s")));
    }
}

