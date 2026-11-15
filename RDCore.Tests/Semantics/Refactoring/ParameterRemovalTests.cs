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
/// A parameter is removed from its declaration and its argument from every call, with the procedures that change with it; and it is not removed where that
/// would change what the code does.
/// </summary>
[TestClass]
public sealed class ParameterRemovalTests
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

    private static ParameterRemovalPlan Remove(RenameContext context, string module, string line, string text)
    {
        var (target, refusal) = ParameterRemoval.Prepare(context, ModuleUri(module), At(context, module, line, text));
        Assert.IsNotNull(target, refusal?.ToMessage(culture: CultureInfo.InvariantCulture));
        return ParameterRemoval.Plan(context, target);
    }

    // the text of the module once the plan's edits are made.
    private static string Removed(RenameContext context, ParameterRemovalPlan plan, string module)
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

    private static ParameterRemovalRefusalKind? RefusalOf(RenameContext context, string module, string line, string text) => Remove(context, module, line, text).Refusal?.Kind;

    private static readonly string Tools = Module("Tools",
        "Public Sub Go(ByVal a As Long, ByVal b As Long)",
        "    Debug.Print a",
        "End Sub",
        "Public Function Twice(ByVal x As Long, ByVal unused As Long) As Long",
        "    Twice = x * 2",
        "End Function",
        "Public Function Seed() As Long",
        "End Function");

    [TestMethod]
    public void AParameter_IsRemovedFromItsDeclaration_AndFromEveryCall_ByPositionOrByName()
    {
        var context = Compose(("Tools", Tools), ("Main", Module("Main",
            "Public Sub One()",
            "    Go 1, 2",
            "    Call Go(1, 2)",
            "    Go b:=2, a:=1",
            "    Tools.Go 1, 2",
            "End Sub")));

        var plan = Remove(context, "Tools", "Public Sub Go", "b As");

        Assert.AreEqual(Tools.Replace("ByVal a As Long, ByVal b As Long", "ByVal a As Long"), Removed(context, plan, "Tools"));
        Assert.AreEqual(Module("Main", "Public Sub One()", "    Go 1", "    Call Go(1)", "    Go a:=1", "    Tools.Go 1", "End Sub"), Removed(context, plan, "Main"));
    }

    [TestMethod]
    public void TheParameterOfAFunction_IsRemovedFromACallInAnExpression()
    {
        var context = Compose(("Tools", Tools), ("Main", Module("Main", "Public Sub One()", "    Debug.Print Twice(3, 4) + Twice(5, 6)", "End Sub")));

        var plan = Remove(context, "Tools", "Public Function Twice", "unused");

        Assert.AreEqual(Module("Main", "Public Sub One()", "    Debug.Print Twice(3) + Twice(5)", "End Sub"), Removed(context, plan, "Main"));
    }

    [TestMethod]
    [DataRow("    Log \"a\"", "    Log", DisplayName = "a call statement")]
    [DataRow("    Log (\"a\")", "    Log", DisplayName = "a call statement whose argument is in parentheses")]
    [DataRow("    Call Log(\"a\")", "    Call Log", DisplayName = "a Call statement")]
    public void TheOnlyArgumentOfACall_IsRemovedWithTheParenthesesThatHoldIt(string call, string removed)
    {
        var context = Compose(("Main", Module("Main", "Public Sub Log(ByVal message As String)", "End Sub", "Public Sub One()", call, "End Sub")));

        var plan = Remove(context, "Main", "Public Sub Log", "message");

        Assert.AreEqual(Module("Main", "Public Sub Log()", "End Sub", "Public Sub One()", removed, "End Sub"), Removed(context, plan, "Main"));
    }

    [TestMethod]
    [DataRow("    Pick 1, , 3", "    Pick 1, 3", DisplayName = "an omitted argument stays omitted")]
    [DataRow("    Pick 1, 2", "    Pick 1", DisplayName = "the last argument")]
    [DataRow("    Pick , 2", "    Pick", DisplayName = "the last argument, after an omitted one")]
    public void AnOptionalParameter_IsRemovedWhereItsArgumentIsPassed_AndOmitted(string call, string removed)
    {
        var context = Compose(("Main", Module("Main",
            "Public Sub Pick(Optional ByVal a As Long, Optional ByVal b As Long, Optional ByVal c As Long)", "    Debug.Print a; c", "End Sub",
            "Public Sub One()", call, "End Sub")));

        var plan = Remove(context, "Main", "Public Sub Pick", "b As");

        StringAssert.Contains(Removed(context, plan, "Main"), removed + "\r\n");
    }

    private static readonly string Store = Class("Store",
        "Private mValue As String",
        "Public Property Get Value(ByVal unused As Long) As String",
        "    Value = mValue",
        "End Property",
        "Public Property Let Value(ByVal unused As Long, ByVal v As String)",
        "    mValue = v",
        "End Property");

    [TestMethod]
    [DataRow("Public Property Get Value", DisplayName = "from the Property Get")]
    [DataRow("Public Property Let Value", DisplayName = "from the Property Let")]
    public void TheParameterOfAProperty_IsRemovedFromEachAccessor_AndFromTheirCalls(string accessor)
    {
        var context = Compose(("Store", Store), ("Main", Module("Main", "Public Sub One()", "    Dim s As New Store", "    s.Value(1) = \"a\"", "    Debug.Print s.Value(1)", "End Sub")));

        var plan = Remove(context, "Store", accessor, "unused");

        Assert.AreEqual(Store.Replace("ByVal unused As Long, ", string.Empty).Replace("(ByVal unused As Long)", "()"), Removed(context, plan, "Store"));
        Assert.AreEqual(Module("Main", "Public Sub One()", "    Dim s As New Store", "    s.Value = \"a\"", "    Debug.Print s.Value", "End Sub"), Removed(context, plan, "Main"));
    }

    [TestMethod]
    public void TheValueOfAPropertyLet_IsNotRemoved()
        => Assert.AreEqual(ParameterRemovalRefusalKind.Value, RefusalOf(Compose(("Store", Store)), "Store", "Public Property Let Value", "v As"));

    private static readonly string Shape = Class("IShape", "Public Sub Draw(ByVal scale As Double)", "End Sub");

    private static readonly string Square = Class("Square", "Implements IShape", "Private Sub IShape_Draw(ByVal factor As Double)", "End Sub");

    [TestMethod]
    [DataRow("IShape", "Public Sub Draw", "scale", DisplayName = "from the member of the interface")]
    [DataRow("Square", "Private Sub IShape_Draw", "factor", DisplayName = "from the procedure that implements it")]
    public void TheParameterOfAMemberThatIsImplemented_IsRemovedFromTheProceduresThatImplementIt(string module, string line, string text)
    {
        // MS-VBAL §5.3.1.9: an implementation has the parameters of the member it implements.
        var context = Compose(("IShape", Shape), ("Square", Square), ("Main", Module("Main", "Public Sub One()", "    Dim s As IShape", "    Set s = New Square", "    s.Draw 2", "End Sub")));

        var plan = Remove(context, module, line, text);

        Assert.AreEqual(Shape.Replace("ByVal scale As Double", string.Empty), Removed(context, plan, "IShape"));
        Assert.AreEqual(Square.Replace("ByVal factor As Double", string.Empty), Removed(context, plan, "Square"));
        Assert.AreEqual(Module("Main", "Public Sub One()", "    Dim s As IShape", "    Set s = New Square", "    s.Draw", "End Sub"), Removed(context, plan, "Main"));
    }

    [TestMethod]
    public void AParameterThatAnImplementationUses_IsNotRemoved()
    {
        var context = Compose(("IShape", Shape), ("Square", Class("Square", "Implements IShape", "Private Sub IShape_Draw(ByVal factor As Double)", "    Debug.Print factor", "End Sub")));

        Assert.AreEqual(ParameterRemovalRefusalKind.Used, RefusalOf(context, "IShape", "Public Sub Draw", "scale"));
    }

    [TestMethod]
    public void AParameterThatIsUsed_IsNotRemoved_AndTheRefusalSaysWhere()
    {
        var context = Compose(("Tools", Tools));

        var plan = Remove(context, "Tools", "Public Sub Go", "a As");

        Assert.AreEqual(ParameterRemovalRefusalKind.Used, plan.Refusal?.Kind);
        StringAssert.Contains(plan.Refusal!.ToMessage(culture: CultureInfo.InvariantCulture), "Tools 3:17");
    }

    [TestMethod]
    [DataRow("    Go 1, Seed()", DisplayName = "a function's result")]
    [DataRow("    Go 1, Seed", DisplayName = "a function called without parentheses")]
    [DataRow("    Go 1, 2 + 3", DisplayName = "an operation, which can raise an error")]
    public void AnArgumentWhoseEvaluationDoesSomething_IsNotRemoved(string call)
    {
        var context = Compose(("Tools", Tools), ("Main", Module("Main", "Public Sub One()", call, "End Sub")));

        Assert.AreEqual(ParameterRemovalRefusalKind.Evaluated, RefusalOf(context, "Tools", "Public Sub Go", "b As"));
    }

    [TestMethod]
    [DataRow("    Go 1, n", DisplayName = "a local")]
    [DataRow("    Go 1, Limit", DisplayName = "a constant")]
    [DataRow("    Go 1, -2", DisplayName = "a negative literal")]
    [DataRow("    Go 1, Color.Red", DisplayName = "a member of an Enum, qualified")]
    public void AnArgumentThatOnlyReadsAValue_IsRemoved(string call)
    {
        var context = Compose(("Tools", Tools), ("Main", Module("Main",
            "Private Const Limit As Long = 3", "Private Enum Color", "    Red", "End Enum",
            "Public Sub One()", "    Dim n As Long", call, "End Sub")));

        StringAssert.Contains(Removed(context, Remove(context, "Tools", "Public Sub Go", "b As"), "Main"), "    Go 1\r\n");
    }

    [TestMethod]
    public void AParameterOfAProcedureCalledLateBound_IsNotRemoved()
    {
        var context = Compose(
            ("Widget", Class("Widget", "Public Sub Go(ByVal a As Long, ByVal b As Long)", "    Debug.Print a", "End Sub")),
            ("Main", Module("Main", "Public Sub One()", "    Dim o As Object", "    Set o = New Widget", "    o.Go 1, 2", "End Sub")));

        Assert.AreEqual(ParameterRemovalRefusalKind.LateBound, RefusalOf(context, "Widget", "Public Sub Go", "b As"));
    }

    [TestMethod]
    public void AParamArray_IsNotRemoved()
    {
        var context = Compose(("Main", Module("Main", "Public Sub Log(ParamArray items() As Variant)", "End Sub")));

        Assert.AreEqual(ParameterRemovalRefusalKind.ParamArray, RefusalOf(context, "Main", "Public Sub Log", "items"));
    }

    private static readonly string Spinner = Class("Spinner", "Public Event Spun(ByVal times As Long)");

    private static readonly string Watcher = Class("Watcher", "Private WithEvents source As Spinner", "Private Sub source_Spun(ByVal times As Long)", "End Sub");

    [TestMethod]
    [DataRow("Spinner", "Public Event Spun", ParameterRemovalRefusalKind.Event, DisplayName = "of an event")]
    [DataRow("Watcher", "Private Sub source_Spun", ParameterRemovalRefusalKind.Handler, DisplayName = "of a handler, which takes the event's")]
    public void TheParameterOfAnEvent_OrOfItsHandler_IsNotRemovedYet(string module, string line, ParameterRemovalRefusalKind refusal)
        => Assert.AreEqual(refusal, RefusalOf(Compose(("Spinner", Spinner), ("Watcher", Watcher)), module, line, "times"));

    [TestMethod]
    public void WhereNoParameterIsDeclared_ThereIsNothingToRemove()
    {
        var context = Compose(("Tools", Tools));

        Assert.IsNull(ParameterRemoval.Prepare(context, ModuleUri("Tools"), At(context, "Tools", "    Debug.Print a", "a")).Target, "a use is no declaration");
        Assert.IsNull(ParameterRemoval.Prepare(context, ModuleUri("Tools"), At(context, "Tools", "Public Sub Go", "Go")).Target);
    }

    [TestMethod]
    public void ARefusal_SaysWhyInTheLanguageOfTheReader()
    {
        var context = Compose(("Main", Module("Main", "Public Sub Log(ParamArray items() As Variant)", "End Sub")));

        var refusal = Remove(context, "Main", "Public Sub Log", "items").Refusal!;

        Assert.AreEqual("« items » est un ParamArray : le supprimer n'est pas pris en charge.", refusal.ToMessage(culture: CultureInfo.GetCultureInfo("fr-CA")));
        Assert.AreEqual("Supprimer le paramètre « items »", ParameterRemovalRefusal.Title("items", CultureInfo.GetCultureInfo("fr-CA")));
    }
}
