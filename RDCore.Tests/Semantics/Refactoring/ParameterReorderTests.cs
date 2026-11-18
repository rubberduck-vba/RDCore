using RDCore.LanguageServer.Services.Refactoring;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.Symbols;
using System.Globalization;

namespace RDCore.Tests.Semantics.Refactoring;

/// <summary>
/// The parameters of a procedure are written in another order, with the arguments of every call and the procedures that change with it; and not where the
/// order is not one VBA declares, or where a call could be missed.
/// </summary>
[TestClass]
public sealed class ParameterReorderTests
{
    private static readonly Uri Root = new("file://rdcore-test");

    private static string Lines(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private static string Module(string name, params string[] lines) => Lines([$"Attribute VB_Name = \"{name}\"", .. lines]);

    private static string Class(string name, params string[] lines)
        => Lines(["VERSION 1.0 CLASS", "BEGIN", "  MultiUse = -1  'True", "END", $"Attribute VB_Name = \"{name}\"", .. lines]);

    // the workspace of the modules, by name: a .cls when the source is a class's.
    private static RenameContext Compose(params (string Name, string Source)[] modules)
    {
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

    // the position of the first `text` on the line that has `line`, in the module.
    private static SourcePosition At(RenameContext context, string module, string line, string text)
    {
        var lines = context.Modules.Single(candidate => SymbolUri.ModuleNameOf(candidate.Module) == module).Source.Split("\r\n");
        var index = Array.FindIndex(lines, candidate => candidate.Contains(line, StringComparison.Ordinal));
        Assert.IsTrue(index >= 0, $"no line has '{line}'");
        return new SourcePosition(index, lines[index].IndexOf(text, StringComparison.Ordinal) + 1);
    }

    private static ParameterReorderTarget Target(RenameContext context, string module, string line, string text)
    {
        var (target, refusal) = ParameterReorder.Prepare(context, ModuleUri(module), At(context, module, line, text));
        Assert.IsNotNull(target, refusal?.ToMessage(culture: CultureInfo.InvariantCulture));
        return target;
    }

    private static ParameterReorderPlan Reorder(RenameContext context, string module, string line, string text, params int[] order)
        => ParameterReorder.Plan(context, Target(context, module, line, text), order);

    // the text of the module once the plan's edits are made.
    private static string Reordered(RenameContext context, ParameterReorderPlan plan, string module)
    {
        Assert.IsTrue(plan.IsValid, plan.Refusal?.ToMessage(culture: CultureInfo.InvariantCulture));
        var source = context.Modules.Single(candidate => SymbolUri.ModuleNameOf(candidate.Module) == module).Source;
        var lines = source.Split("\r\n");
        foreach (var edit in plan.Edits.Where(edit => SymbolUri.ModuleNameOf(edit.Module) == module).OrderByDescending(edit => edit.Range.Start))
        {
            var start = string.Join("\r\n", lines.Take(edit.Range.Start.Line)).Length + (edit.Range.Start.Line > 0 ? 2 : 0) + edit.Range.Start.Character;
            var end = string.Join("\r\n", lines.Take(edit.Range.End.Line)).Length + (edit.Range.End.Line > 0 ? 2 : 0) + edit.Range.End.Character;
            source = source[..start] + edit.NewText + source[end..];
            lines = source.Split("\r\n");
        }

        return source;
    }

    private static readonly string Tools = Module("Tools",
        "Public Sub Go(ByVal a As Long, ByVal b As String)",
        "End Sub",
        "Public Function Twice(ByVal x As Long, ByVal y As Long) As Long",
        "End Function",
        "Public Function Seed() As Long",
        "End Function");

    [TestMethod]
    public void TheParameters_AreWrittenInTheNewOrder_AndTheArgumentsOfEveryCall()
    {
        var context = Compose(("Tools", Tools), ("Main", Module("Main",
            "Public Sub One()",
            "    Go 1, \"x\"",
            "    Call Go(2, \"y\")",
            "    Tools.Go 3, \"z\"",
            "    Debug.Print Twice(4, 5) + Twice(6, 7)",
            "End Sub")));

        var go = Reorder(context, "Tools", "Public Sub Go", "Go", 1, 0);
        var twice = Reorder(context, "Tools", "Public Function Twice", "y As", 1, 0);

        Assert.AreEqual(Tools.Replace("ByVal a As Long, ByVal b As String", "ByVal b As String, ByVal a As Long"), Reordered(context, go, "Tools"));
        Assert.AreEqual(Module("Main", "Public Sub One()", "    Go \"x\", 1", "    Call Go(\"y\", 2)", "    Tools.Go \"z\", 3",
            "    Debug.Print Twice(4, 5) + Twice(6, 7)", "End Sub"), Reordered(context, go, "Main"));
        StringAssert.Contains(Reordered(context, twice, "Main"), "    Debug.Print Twice(5, 4) + Twice(7, 6)\r\n");
    }

    [TestMethod]
    [DataRow("    Go b:=\"x\", a:=1", "    Go b:=\"x\", a:=1", DisplayName = "arguments passed by name stay as they are")]
    [DataRow("    Go 1, b:=\"x\"", "    Go \"x\", 1", DisplayName = "one passed by name before one passed by position is passed by position")]
    public void AnArgumentPassedByName_StaysWhereItIsWritten_UnlessOnePassedByPositionComesAfterItsParameter(string call, string reordered)
    {
        var context = Compose(("Tools", Tools), ("Main", Module("Main", "Public Sub One()", call, "End Sub")));

        var plan = Reorder(context, "Tools", "Public Sub Go", "Go", 1, 0);

        Assert.AreEqual(Module("Main", "Public Sub One()", reordered, "End Sub"), Reordered(context, plan, "Main"));
    }

    [TestMethod]
    [DataRow("    Pick 1", "    Pick , , 1", DisplayName = "an argument that moves after omitted ones")]
    [DataRow("    Pick 1, , 3", "    Pick 3, , 1", DisplayName = "an omitted argument stays omitted")]
    [DataRow("    Pick , 2", "    Pick , 2", DisplayName = "omitted arguments at the end are not written")]
    public void TheOmittedArguments_AreOmittedWhereTheirParametersGo(string call, string reordered)
    {
        var context = Compose(("Main", Module("Main",
            "Public Sub Pick(Optional ByVal a As Long, Optional ByVal b As Long, Optional ByVal c As Long)", "End Sub",
            "Public Sub One()", call, "End Sub")));

        var plan = Reorder(context, "Main", "Public Sub Pick", "Pick", 2, 1, 0);

        StringAssert.Contains(Reordered(context, plan, "Main"), $"\r\n{reordered}\r\n");
    }

    [TestMethod]
    public void TheAccessorsOfAProperty_AreReorderedTogether_AndTheValueStaysLast()
    {
        var context = Compose(
            ("Sheet", Class("Sheet",
                "Public Property Get Cell(ByVal row As Long, ByVal column As Long) As String",
                "End Property",
                "Public Property Let Cell(ByVal row As Long, ByVal column As Long, ByVal value As String)",
                "End Property")),
            ("Main", Module("Main", "Public Sub One()", "    Dim s As New Sheet", "    s.Cell(1, 2) = s.Cell(3, 4)", "End Sub")));

        var plan = Reorder(context, "Sheet", "Public Property Get Cell", "column", 1, 0);

        Assert.AreEqual(Class("Sheet",
            "Public Property Get Cell(ByVal column As Long, ByVal row As Long) As String",
            "End Property",
            "Public Property Let Cell(ByVal column As Long, ByVal row As Long, ByVal value As String)",
            "End Property"), Reordered(context, plan, "Sheet"));
        StringAssert.Contains(Reordered(context, plan, "Main"), "    s.Cell(2, 1) = s.Cell(4, 3)\r\n");
    }

    [TestMethod]
    [DataRow("Shape", "Public Sub Draw", DisplayName = "from the member of the interface")]
    [DataRow("Circle", "Private Sub Shape_Draw", DisplayName = "from the procedure that implements it")]
    public void TheMemberOfAnInterface_IsReorderedWithTheProceduresThatImplementIt(string module, string line)
    {
        var context = Compose(
            ("Shape", Class("Shape", "Public Sub Draw(ByVal x As Long, ByVal y As Long)", "End Sub")),
            ("Circle", Class("Circle", "Implements Shape", "Private Sub Shape_Draw(ByVal x As Long, ByVal y As Long)", "End Sub")));

        var plan = Reorder(context, module, line, "y As", 1, 0);

        StringAssert.Contains(Reordered(context, plan, "Shape"), "Public Sub Draw(ByVal y As Long, ByVal x As Long)");
        StringAssert.Contains(Reordered(context, plan, "Circle"), "Private Sub Shape_Draw(ByVal y As Long, ByVal x As Long)");
    }

    [TestMethod]
    public void AParamArray_StaysLast_WithTheArgumentsItTakes()
    {
        var context = Compose(("Main", Module("Main",
            "Public Sub Log(ByVal level As Long, ByVal source As String, ParamArray items() As Variant)", "End Sub",
            "Public Sub One()", "    Log 1, \"a\", 3, 4", "End Sub")));

        var plan = Reorder(context, "Main", "Public Sub Log", "Log", 1, 0, 2);
        var refused = Reorder(context, "Main", "Public Sub Log", "Log", 2, 0, 1);

        StringAssert.Contains(Reordered(context, plan, "Main"), "Public Sub Log(ByVal source As String, ByVal level As Long, ParamArray items() As Variant)");
        StringAssert.Contains(Reordered(context, plan, "Main"), "    Log \"a\", 1, 3, 4\r\n");
        Assert.AreEqual(ParameterReorderRefusalKind.ParamArray, refused.Refusal?.Kind);
    }

    [TestMethod]
    public void ARequiredParameter_AfterAnOptionalOne_IsRefused()
    {
        var context = Compose(("Main", Module("Main", "Public Sub Pick(ByVal a As Long, Optional ByVal b As Long)", "End Sub")));

        var plan = Reorder(context, "Main", "Public Sub Pick", "Pick", 1, 0);

        Assert.AreEqual(ParameterReorderRefusalKind.Optional, plan.Refusal?.Kind);
        Assert.AreEqual("a", plan.Refusal!.Name);
    }

    [TestMethod]
    [DataRow(new[] { 0 }, DisplayName = "too few")]
    [DataRow(new[] { 0, 0 }, DisplayName = "a parameter twice")]
    [DataRow(new[] { 0, 2 }, DisplayName = "a parameter it does not have")]
    public void AnOrderThatIsNotOneOfTheParameters_IsRefused(int[] order)
        => Assert.AreEqual(ParameterReorderRefusalKind.Order, Reorder(Compose(("Tools", Tools)), "Tools", "Public Sub Go", "Go", order).Refusal?.Kind);

    [TestMethod]
    public void ArgumentsThatDoMoreThanReadAValue_EvaluatedInAnotherOrder_AreAChangeTheUserAccepts()
    {
        var context = Compose(("Tools", Tools), ("Main", Module("Main", "Public Sub One()", "    Debug.Print Twice(Seed(), Seed() + 1)", "    Debug.Print Twice(1, Seed())", "End Sub")));

        var kept = Reorder(context, "Tools", "Public Function Twice", "Twice", 0, 1);
        var swapped = Reorder(context, "Tools", "Public Function Twice", "Twice", 1, 0);

        Assert.IsEmpty(kept.Changes, "the order they have");
        Assert.AreEqual(ParameterReorderRefusalKind.MayEvaluate, Assert.ContainsSingle(kept.Possible).Kind, "only the call with two");
        var change = Assert.ContainsSingle(swapped.Changes);
        Assert.AreEqual(ParameterReorderRefusalKind.Evaluated, change.Kind);
        Assert.AreEqual(At(context, "Main", "Twice(Seed(), Seed() + 1)", "Seed()").Line, change.Position.Line);
        StringAssert.Contains(Reordered(context, swapped, "Main"), "    Debug.Print Twice(Seed() + 1, Seed())\r\n");
    }

    [TestMethod]
    public void TheParametersOfAProcedureCalledLateBound_AreNotReordered()
    {
        var context = Compose(
            ("Widget", Class("Widget", "Public Sub Go(ByVal a As Long, ByVal b As Long)", "End Sub")),
            ("Main", Module("Main", "Public Sub One()", "    Dim o As Object", "    Set o = New Widget", "    o.Go 1, 2", "End Sub")));

        Assert.AreEqual(ParameterReorderRefusalKind.LateBound, Reorder(context, "Widget", "Public Sub Go", "Go", 1, 0).Refusal?.Kind);
    }

    private static readonly string Spinner = Class("Spinner", "Public Event Spun(ByVal times As Long, ByVal speed As Long)");

    private static readonly string Watcher = Class("Watcher", "Private WithEvents source As Spinner", "Private Sub source_Spun(ByVal times As Long, ByVal speed As Long)", "End Sub");

    [TestMethod]
    [DataRow("Spinner", "Public Event Spun", ParameterReorderRefusalKind.Event, DisplayName = "of an event")]
    [DataRow("Watcher", "Private Sub source_Spun", ParameterReorderRefusalKind.Handler, DisplayName = "of a handler, which takes the event's")]
    public void TheParametersOfAnEvent_OrOfItsHandler_AreNotReorderedYet(string module, string line, ParameterReorderRefusalKind refusal)
        => Assert.AreEqual(refusal, Reorder(Compose(("Spinner", Spinner), ("Watcher", Watcher)), module, line, "speed", 1, 0).Refusal?.Kind);

    [TestMethod]
    public void TheProcedure_IsFoundAtItsName_AtACallToIt_OrAtTheDeclarationOfAParameter_WithItsParametersAsTheyAreWritten()
    {
        var context = Compose(("Tools", Tools), ("Main", Module("Main", "Public Sub One()", "    Go 1, \"x\"", "End Sub")));

        var atTheCall = Target(context, "Main", "    Go 1", "Go");
        var atAParameter = Target(context, "Tools", "Public Sub Go", "b As");

        Assert.AreEqual("Go", atTheCall.Name);
        Assert.AreEqual(atTheCall.Procedure, atAParameter.Procedure);
        CollectionAssert.AreEqual(new[] { "a", "b" }, atTheCall.Parameters.ToArray());
        CollectionAssert.AreEqual(new[] { "ByVal a As Long", "ByVal b As String" }, atTheCall.Declarations.ToArray());
    }

    [TestMethod]
    public void AProcedureWithFewerThanTwoParameters_HasNothingToReorder()
    {
        var context = Compose(("Main", Module("Main", "Public Sub Log(ByVal message As String)", "End Sub", "Public Sub None()", "End Sub")));

        var (one, refusal) = ParameterReorder.Prepare(context, ModuleUri("Main"), At(context, "Main", "Public Sub Log", "Log"));
        var (none, _) = ParameterReorder.Prepare(context, ModuleUri("Main"), At(context, "Main", "Public Sub None", "None"));

        Assert.IsNull(one);
        Assert.IsNull(none);
        Assert.IsTrue(refusal!.FindsNothing);
    }

    [TestMethod]
    public void ARefusal_SaysWhyInTheLanguageOfTheReader()
    {
        var context = Compose(("Main", Module("Main", "Public Sub Pick(ByVal a As Long, Optional ByVal b As Long)", "End Sub")));

        var refusal = Reorder(context, "Main", "Public Sub Pick", "Pick", 1, 0).Refusal!;

        Assert.AreEqual("« a » est obligatoire, et viendrait après le paramètre facultatif « b » : les paramètres facultatifs viennent en dernier.",
            refusal.ToMessage(culture: CultureInfo.GetCultureInfo("fr-CA")));
        Assert.AreEqual("Réordonner les paramètres de « Pick »", ParameterReorderRefusal.Title("Pick", CultureInfo.GetCultureInfo("fr-CA")));
    }
}
