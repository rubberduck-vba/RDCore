using RDCore.LanguageServer.Services.Refactoring;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.Runtime.LanguageCore.Static;
using RDCore.Symbols;
using RDCore.Tests.Runtime.Libraries;
using System.Globalization;
using RDCore.Runtime.LanguageCore.Refactoring;
using MemberKind = RDCore.SDK.Runtime.Libraries.MemberKind;

namespace RDCore.Tests.Semantics.Refactoring;

/// <summary>
/// A rename renames what refers to the symbol, wherever it is written, and nothing else; and it refuses a new name that would change what the code means.
/// </summary>
[TestClass]
public sealed class SymbolRenameTests
{
    private static readonly Uri Root = new("file://rdcore-test");

    private static readonly string Class1 = Lines(
        "VERSION 1.0 CLASS",
        "BEGIN",
        "  MultiUse = -1  'True",
        "END",
        "Attribute VB_Name = \"Class1\"",
        "Option Explicit",
        "Private mValue As String",
        "Public Property Get Value() As String",
        "Attribute Value.VB_UserMemId = 0",
        "    Value = mValue",
        "End Property",
        "Public Property Let Value(ByVal v As String)",
        "    mValue = v",
        "End Property");

    private static string Lines(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private static string Module(string name, params string[] lines) => Lines([$"Attribute VB_Name = \"{name}\"", .. lines]);

    // the workspace of the modules, by name: a .cls when the source is a class's.
    private static RenameContext Compose(params (string Name, string Source)[] modules) => ComposeReferencing([], modules);

    // the workspace of the modules, referencing the standard library and a host's: what calls a procedure by its name in a string.
    private static RenameContext ComposeWithHost(params (string Name, string Source)[] modules)
        => ComposeReferencing(
            [
                .. new LibrarySymbolProvider(Root, ReferencedLibraries.Load(new InMemoryLibrarySource(), [], [StandardLibrary.LibraryName])).ProvideSymbols(),
                .. new LibrarySymbolReader(Root).Read([Host]),
            ], modules);

    // a host's library: the members a host calls a procedure by its name with - a macro it runs, a procedure it runs at a time or on a key, a control's action.
    private static readonly LibraryDescription Host = new()
    {
        Name = "Host",
        Origin = new LibraryOrigin { Guid = "{00000000-0000-0000-0000-0000000000AA}", Version = "1.0", ExportedBy = "tests" },
        Classes =
        [
            new ClassDescription
            {
                Name = "Application",
                Members =
                [
                    new MemberDescription
                    {
                        Name = "Run", Kind = MemberKind.Method, Type = "Variant",
                        Parameters = [new ParameterDescription { Name = "Macro", Type = "Variant", IsOptional = true }],
                    },
                    new MemberDescription
                    {
                        Name = "OnTime", Kind = MemberKind.Method,
                        Parameters =
                        [
                            new ParameterDescription { Name = "EarliestTime", Type = "Variant" },
                            new ParameterDescription { Name = "Procedure", Type = "String" },
                        ],
                    },
                ],
            },
            new ClassDescription
            {
                Name = "Button",
                Members =
                [
                    new MemberDescription { Name = "OnAction", Kind = MemberKind.PropertyGet, Type = "String" },
                    new MemberDescription
                    {
                        Name = "OnAction", Kind = MemberKind.PropertyLet,
                        Parameters = [new ParameterDescription { Name = "RHS", Type = "String", IsByVal = true }],
                    },
                ],
            },
        ],
    };

    private static RenameContext ComposeReferencing(IReadOnlyList<RDCore.SDK.Model.Symbols.Abstract.Symbol> libraries, (string Name, string Source)[] modules, string? projectName = null)
    {
        var parsed = modules.Select(module =>
        {
            var isClass = module.Source.StartsWith("VERSION", StringComparison.Ordinal);
            var parse = new ModuleParser().Parse(new Uri($"file:///c:/ws/{module.Name}{(isClass ? ".cls" : ".bas")}"), module.Source);
            Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
            return (Uri: new UriBuilder(Root) { Fragment = module.Name }.Uri, Type: isClass ? ModuleType.ClassModule : ModuleType.StdModule, Parse: parse, module.Source);
        }).ToList();

        var composition = WorkspaceSymbolResolver.ComposeWithScopes(Root, [.. parsed.Select(module => (module.Uri, module.Type, module.Parse))], new IntrinsicSymbolResolver(),
            projectName: projectName, libraries: libraries.Count == 0 ? null : libraries);
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

    private static RenamePlan Rename(RenameContext context, string module, string line, string text, string newName)
    {
        var preparation = SymbolRename.Prepare(context, ModuleUri(module), At(context, module, line, text));
        Assert.IsNotNull(preparation.Target, preparation.Refusal?.ToMessage(culture: CultureInfo.InvariantCulture));
        return SymbolRename.Plan(context, preparation.Target, newName);
    }

    // the text of the module once the rename's edits are made.
    private static string Renamed(RenameContext context, RenamePlan plan, string module)
    {
        Assert.IsTrue(plan.IsValid, plan.Refusal?.ToMessage(culture: CultureInfo.InvariantCulture));
        var lines = context.Modules.Single(candidate => SymbolUri.ModuleNameOf(candidate.Module) == module).Source.Split("\r\n");
        foreach (var edit in plan.Edits.Where(edit => SymbolUri.ModuleNameOf(edit.Module) == module).OrderByDescending(edit => edit.Range.Start))
        {
            var line = lines[edit.Range.Start.Line];
            lines[edit.Range.Start.Line] = line[..edit.Range.Start.Character] + edit.NewText + line[edit.Range.End.Character..];
        }

        return string.Join("\r\n", lines);
    }

    [TestMethod]
    public void ALocal_IsRenamedWhereItsProcedureRefersToIt_AndNotInAnotherProcedure()
    {
        var context = Compose(("Main", Module("Main",
            "Public Sub One()",
            "    Dim x As Long",
            "    x = x + 1 ' x",
            "    Debug.Print \"x\", x",
            "End Sub",
            "Public Sub Two()",
            "    Dim x As Long",
            "    x = 2",
            "End Sub")));

        var plan = Rename(context, "Main", "x = x + 1", "x", "count");

        Assert.AreEqual(Module("Main",
            "Public Sub One()",
            "    Dim count As Long",
            "    count = count + 1 ' x",
            "    Debug.Print \"x\", count",
            "End Sub",
            "Public Sub Two()",
            "    Dim x As Long",
            "    x = 2",
            "End Sub"), Renamed(context, plan, "Main"));
    }

    [TestMethod]
    public void TheCounterOfAForLoop_IsRenamedInTheNextThatNamesIt()
    {
        var context = Compose(("Main", Module("Main",
            "Public Sub Count()",
            "    Dim x As Integer",
            "    For x = 1 To 10",
            "        Debug.Print \"Hello RDCore!\"",
            "    Next x",
            "End Sub")));

        var plan = Rename(context, "Main", "For x = 1", "x", "tehDuck");

        Assert.AreEqual(Module("Main",
            "Public Sub Count()",
            "    Dim tehDuck As Integer",
            "    For tehDuck = 1 To 10",
            "        Debug.Print \"Hello RDCore!\"",
            "    Next tehDuck",
            "End Sub"), Renamed(context, plan, "Main"));
    }

    [TestMethod]
    public void TheElementOfAForEachLoop_IsRenamedInTheNextThatNamesIt()
    {
        var context = Compose(("Main", Module("Main",
            "Public Sub List(ByVal items As Collection)",
            "    Dim item As Variant",
            "    For Each item In items",
            "        Debug.Print item",
            "    Next item",
            "End Sub")));

        var plan = Rename(context, "Main", "For Each item", "item", "entry");

        Assert.AreEqual(Module("Main",
            "Public Sub List(ByVal items As Collection)",
            "    Dim entry As Variant",
            "    For Each entry In items",
            "        Debug.Print entry",
            "    Next entry",
            "End Sub"), Renamed(context, plan, "Main"));
    }

    [TestMethod]
    public void TheCounterOfAnOuterLoop_IsRenamedAfterTheCommaOfANextThatEndsBoth_FromTheNext()
    {
        var context = Compose(("Main", Module("Main",
            "Public Sub Grid()",
            "    Dim i As Long, j As Long",
            "    For i = 1 To 3",
            "        For j = 1 To 3",
            "            Debug.Print i * j",
            "    Next j, i",
            "End Sub")));

        var plan = Rename(context, "Main", "Next j, i", "i", "row");

        Assert.AreEqual(Module("Main",
            "Public Sub Grid()",
            "    Dim row As Long, j As Long",
            "    For row = 1 To 3",
            "        For j = 1 To 3",
            "            Debug.Print row * j",
            "    Next j, row",
            "End Sub"), Renamed(context, plan, "Main"));
    }

    [TestMethod]
    public void TheCounterOfALoop_WhoseNextNamesNoVariable_IsRenamedWhereItIsWritten()
    {
        var context = Compose(("Main", Module("Main",
            "Public Sub Count()",
            "    Dim x As Integer",
            "    For x = 1 To 10",
            "        Debug.Print x",
            "    Next",
            "End Sub")));

        var plan = Rename(context, "Main", "For x = 1", "x", "tehDuck");

        Assert.AreEqual(Module("Main",
            "Public Sub Count()",
            "    Dim tehDuck As Integer",
            "    For tehDuck = 1 To 10",
            "        Debug.Print tehDuck",
            "    Next",
            "End Sub"), Renamed(context, plan, "Main"));
    }

    [TestMethod]
    public void AModuleVariable_IsRenamedInEveryModule_QualifiedOrNot()
    {
        var context = Compose(
            ("Main", Module("Main", "Public strName As String", "Public Sub One()", "    strName = \"a\"", "End Sub")),
            ("Other", Module("Other", "Public Sub Two()", "    Debug.Print strName, Main.strName", "End Sub")));

        var plan = Rename(context, "Other", "Debug.Print", "strName", "Name1");

        Assert.AreEqual(Module("Main", "Public Name1 As String", "Public Sub One()", "    Name1 = \"a\"", "End Sub"), Renamed(context, plan, "Main"));
        Assert.AreEqual(Module("Other", "Public Sub Two()", "    Debug.Print Name1, Main.Name1", "End Sub"), Renamed(context, plan, "Other"));
    }

    [TestMethod]
    public void AProperty_IsRenamedWithEachAccessor_ItsMemberAccesses_AndItsAttribute()
    {
        var context = Compose(("Class1", Class1), ("Main", Module("Main",
            "Public Sub One()",
            "    Dim c As Class1",
            "    Set c = New Class1",
            "    c.Value = \"a\"",
            "    With c",
            "        .Value = .Value & \"b\"",
            "    End With",
            "End Sub")));

        var plan = Rename(context, "Main", "c.Value", "Value", "Text");

        Assert.AreEqual(Class1.Replace(" Value", " Text"), Renamed(context, plan, "Class1"), "and not mValue, which is another name");
        Assert.AreEqual(Module("Main",
            "Public Sub One()",
            "    Dim c As Class1",
            "    Set c = New Class1",
            "    c.Text = \"a\"",
            "    With c",
            "        .Text = .Text & \"b\"",
            "    End With",
            "End Sub"), Renamed(context, plan, "Main"));
    }

    [TestMethod]
    public void AParameter_IsRenamedWithTheNamedArgumentsOfTheCallsOfItsProcedure()
    {
        var context = Compose(("Main", Module("Main",
            "Public Sub Show(ByVal count As Long)",
            "    Debug.Print count",
            "End Sub",
            "Public Sub One(ByVal count As Long)",
            "    Show count:=count",
            "    Show count:=1",
            "End Sub")));

        var plan = Rename(context, "Main", "Public Sub Show", "count", "times");

        Assert.AreEqual(Module("Main",
            "Public Sub Show(ByVal times As Long)",
            "    Debug.Print times",
            "End Sub",
            "Public Sub One(ByVal count As Long)",
            "    Show times:=count",
            "    Show times:=1",
            "End Sub"), Renamed(context, plan, "Main"));
    }

    [TestMethod]
    public void AConstant_IsRenamedInTheExpressionOfAnotherConstant()
    {
        var context = Compose(("Main", Module("Main",
            "Private Const Size2 As Long = 3",
            "Private Const Size As Long = 2 * Size2",
            "Public Sub One()",
            "    Debug.Print Size2",
            "End Sub")));

        var plan = Rename(context, "Main", "Debug.Print", "Size2", "Width");

        Assert.AreEqual(Module("Main",
            "Private Const Width As Long = 3",
            "Private Const Size As Long = 2 * Width",
            "Public Sub One()",
            "    Debug.Print Width",
            "End Sub"), Renamed(context, plan, "Main"));
    }

    [TestMethod]
    public void AFieldOfAType_AndAMemberOfAnEnum_AreRenamedWhereTheyAreUsed()
    {
        var context = Compose(("Main", Module("Main",
            "Private Enum Color",
            "    Red = 1",
            "    Green",
            "End Enum",
            "Private Type Point",
            "    X As Long",
            "End Type",
            "Public Sub One()",
            "    Dim p As Point",
            "    p.X = Green + Color.Red",
            "End Sub")));

        var field = Rename(context, "Main", "p.X", "X", "Left1");
        var member = Rename(context, "Main", "p.X", "Green", "Blue");

        Assert.AreEqual(Module("Main",
            "Private Enum Color", "    Red = 1", "    Green", "End Enum", "Private Type Point", "    Left1 As Long", "End Type",
            "Public Sub One()", "    Dim p As Point", "    p.Left1 = Green + Color.Red", "End Sub"), Renamed(context, field, "Main"));
        Assert.AreEqual(Module("Main",
            "Private Enum Color", "    Red = 1", "    Blue", "End Enum", "Private Type Point", "    X As Long", "End Type",
            "Public Sub One()", "    Dim p As Point", "    p.X = Blue + Color.Red", "End Sub"), Renamed(context, member, "Main"));
    }

    [TestMethod]
    [DataRow("IShape", "Public Sub Draw", "Draw", DisplayName = "at its declaration")]
    [DataRow("Square", "Private Sub IShape_Draw", "Draw", DisplayName = "at the part of an implementation's name that is the member's")]
    [DataRow("Square", "    IShape_Draw", "Draw", DisplayName = "where an implementation is called")]
    [DataRow("Main", "s.Draw", "Draw", DisplayName = "where it is called")]
    public void TheMemberOfAClassThatIsImplemented_IsRenamedWithTheProceduresThatImplementIt(string module, string line, string text)
    {
        // MS-VBAL §5.3.1.9: an implementation is named after the interface and its member.
        var context = Compose(("IShape", Shape), ("Square", Square), ("Main", Module("Main", "Public Sub One()", "    Dim s As IShape", "    s.Draw", "End Sub")));

        var plan = Rename(context, module, line, text, "Paint");

        Assert.AreEqual(Shape.Replace("Draw", "Paint"), Renamed(context, plan, "IShape"));
        Assert.AreEqual(Square.Replace("IShape_Draw", "IShape_Paint"), Renamed(context, plan, "Square"));
        Assert.AreEqual(Module("Main", "Public Sub One()", "    Dim s As IShape", "    s.Paint", "End Sub"), Renamed(context, plan, "Main"));
    }

    [TestMethod]
    public void ThePropertyOfAClassThatIsImplemented_IsRenamedWithTheImplementation_WhereItAssignsItsResult()
    {
        var context = Compose(("IShape", Shape), ("Square", Square));

        var plan = Rename(context, "Square", "Private Property Get IShape_Area", "Area", "Size");

        Assert.AreEqual(Shape.Replace("Area", "Size"), Renamed(context, plan, "IShape"));
        Assert.AreEqual(Square.Replace("IShape_Area", "IShape_Size"), Renamed(context, plan, "Square"));
    }

    [TestMethod]
    public void ThePartOfAnImplementationsNameThatIsTheInterfaces_RenamesTheInterface()
    {
        var context = Compose(("IShape", Shape), ("Square", Square));

        var preparation = SymbolRename.Prepare(context, ModuleUri("Square"), At(context, "Square", "Private Sub IShape_Draw", "IShape"));

        Assert.AreEqual("IShape", preparation.Target?.Name);
        Assert.AreEqual(Square.Replace("IShape", "IFigure"), Renamed(context, SymbolRename.Plan(context, preparation.Target!, "IFigure"), "Square"));
    }

    [TestMethod]
    public void AnImplementationThatWouldTakeTheNameOfAnotherMember_RefusesTheRenameOfTheMember()
    {
        var context = Compose(("IShape", Shape), ("Square", Square.Replace("Public Sub Show()", "Public Sub IShape_Paint()\r\nEnd Sub\r\nPublic Sub Show()")));

        var preparation = SymbolRename.Prepare(context, ModuleUri("IShape"), At(context, "IShape", "Public Sub Draw", "Draw"));

        Assert.AreEqual(RenameRefusalKind.Conflict, SymbolRename.Plan(context, preparation.Target!, "Paint").Refusal?.Kind);
    }

    private static readonly string Spinner = Lines(
        "VERSION 1.0 CLASS", "BEGIN", "  MultiUse = -1  'True", "END", "Attribute VB_Name = \"Spinner\"",
        "Public Event Spun()",
        "Public Sub Go()", "    RaiseEvent Spun", "End Sub");

    private static readonly string Watcher = Lines(
        "VERSION 1.0 CLASS", "BEGIN", "  MultiUse = -1  'True", "END", "Attribute VB_Name = \"Watcher\"",
        "Private WithEvents source As Spinner",
        "Public Sub Start()", "    Set source = New Spinner", "    source_Spun", "End Sub",
        "Private Sub source_Spun()", "End Sub");

    [TestMethod]
    [DataRow("Spinner", "Public Event Spun", "Spun", DisplayName = "at its declaration")]
    [DataRow("Spinner", "RaiseEvent Spun", "Spun", DisplayName = "where it is raised")]
    [DataRow("Watcher", "Private Sub source_Spun", "Spun", DisplayName = "at the part of a handler's name that is the event's")]
    public void AnEvent_IsRenamedWhereItIsRaised_WithItsHandlers(string module, string line, string text)
    {
        // MS-VBAL §5.3.1.8: a handler is named after the WithEvents variable and the event.
        var context = Compose(("Spinner", Spinner), ("Watcher", Watcher));

        var plan = Rename(context, module, line, text, "Turned");

        Assert.AreEqual(Spinner.Replace("Spun", "Turned"), Renamed(context, plan, "Spinner"));
        Assert.AreEqual(Watcher.Replace("source_Spun", "source_Turned"), Renamed(context, plan, "Watcher"));
    }

    [TestMethod]
    [DataRow("Private WithEvents source", "source", DisplayName = "at its declaration")]
    [DataRow("Private Sub source_Spun", "source", DisplayName = "at the part of a handler's name that is the variable's")]
    public void AWithEventsVariable_IsRenamedWithItsHandlers(string line, string text)
    {
        var context = Compose(("Spinner", Spinner), ("Watcher", Watcher.Replace("Public Sub Start()", "Private Sub source_Other()\r\nEnd Sub\r\nPublic Sub Start()")));

        var plan = Rename(context, "Watcher", line, text, "watched");

        // a procedure named after the variable that handles none of its events is named as any other.
        Assert.AreEqual(Watcher.Replace("source", "watched").Replace("Public Sub Start()", "Private Sub source_Other()\r\nEnd Sub\r\nPublic Sub Start()"), Renamed(context, plan, "Watcher"));
    }

    [TestMethod]
    public void AHandlerThatWouldTakeTheNameOfAnotherMember_RefusesTheRenameOfTheEvent()
    {
        var context = Compose(("Spinner", Spinner), ("Watcher", Watcher.Replace("Public Sub Start()", "Public Sub source_Turned()\r\nEnd Sub\r\nPublic Sub Start()")));

        var preparation = SymbolRename.Prepare(context, ModuleUri("Spinner"), At(context, "Spinner", "Public Event Spun", "Spun"));

        Assert.AreEqual(RenameRefusalKind.Conflict, SymbolRename.Plan(context, preparation.Target!, "Turned").Refusal?.Kind);
    }

    [TestMethod]
    public void AClassProcedureWithAnUnderscore_ThatNothingCallsByThatName_IsRenamed()
    {
        var context = Compose(
            ("Widget", Lines("VERSION 1.0 CLASS", "BEGIN", "  MultiUse = -1  'True", "END", "Attribute VB_Name = \"Widget\"", "Public Sub Do_It()", "End Sub")),
            ("Main", Module("Main", "Public Sub One()", "    Dim w As New Widget", "    w.Do_It", "End Sub")));

        var plan = Rename(context, "Widget", "Public Sub Do_It", "Do_It", "DoIt");

        Assert.AreEqual(Module("Main", "Public Sub One()", "    Dim w As New Widget", "    w.DoIt", "End Sub"), Renamed(context, plan, "Main"));
    }

    [TestMethod]
    [DataRow("Private Sub Class_Initialize()", "Class_Initialize", DisplayName = "the class's own event")]
    [DataRow("Private Sub UserForm_Click()", "UserForm_Click", DisplayName = "a form's own event")]
    public void AClassProcedureCalledByItsName_AsAHandlerOfItsOwnEvents_IsNotRenamed(string declaration, string name)
    {
        var context = Compose(
            ("Spinner", Lines("VERSION 1.0 CLASS", "BEGIN", "  MultiUse = -1  'True", "END", "Attribute VB_Name = \"Spinner\"", "Public Event Spun()")),
            ("Watcher", Lines("VERSION 1.0 CLASS", "BEGIN", "  MultiUse = -1  'True", "END", "Attribute VB_Name = \"Watcher\"", "Private WithEvents source As Spinner",
                declaration, "End Sub")));

        Assert.AreEqual(RenameRefusalKind.NamedByConvention, SymbolRename.Prepare(context, ModuleUri("Watcher"), At(context, "Watcher", declaration, name)).Refusal?.Kind);
    }

    [TestMethod]
    public void ANameWrittenWithATypeCharacter_KeepsIt()
    {
        var context = Compose(("Main", Module("Main", "Public Sub One()", "    Dim s$", "    s$ = \"a\"", "    Debug.Print s", "End Sub")));

        var plan = Rename(context, "Main", "Debug.Print", "s", "text");

        Assert.AreEqual(Module("Main", "Public Sub One()", "    Dim text$", "    text$ = \"a\"", "    Debug.Print text", "End Sub"), Renamed(context, plan, "Main"));
    }

    [TestMethod]
    public void TheKeyOfADictionaryAccess_IsNotRenamed()
    {
        var context = Compose(("Main", Module("Main",
            "Public Sub One()",
            "    Dim key As Long",
            "    Dim c As Collection",
            "    Set c = New Collection",
            "    Debug.Print c!key, key",
            "End Sub")));

        var plan = Rename(context, "Main", "Dim key", "key", "index");

        Assert.AreEqual(Module("Main",
            "Public Sub One()",
            "    Dim index As Long",
            "    Dim c As Collection",
            "    Set c = New Collection",
            "    Debug.Print c!key, index",
            "End Sub"), Renamed(context, plan, "Main"));
    }

    [TestMethod]
    public void ARenameThatOnlyChangesTheCase_IsMade()
    {
        var context = Compose(("Main", Module("Main", "Public Sub One()", "    Dim total As Long", "    total = 1", "End Sub")));

        var plan = Rename(context, "Main", "total = 1", "total", "Total");

        Assert.AreEqual(Module("Main", "Public Sub One()", "    Dim Total As Long", "    Total = 1", "End Sub"), Renamed(context, plan, "Main"));
    }

    [TestMethod]
    public void ANameDeclaredWhereTheSymbolIs_IsRefused()
    {
        var context = Compose(("Main", Module("Main", "Public Sub One()", "    Dim a As Long", "    Dim b As Long", "    a = b", "End Sub")));

        var plan = Rename(context, "Main", "Dim a", "a", "B");

        Assert.AreEqual(RenameRefusalKind.Conflict, plan.Refusal?.Kind);
    }

    [TestMethod]
    public void ANameThatMeansSomethingNearerWhereTheSymbolIsUsed_IsRefused()
    {
        var context = Compose(("Main", Module("Main",
            "Private total As Long",
            "Public Sub One()",
            "    Dim count As Long",
            "    count = total",
            "End Sub")));

        var plan = Rename(context, "Main", "Private total", "total", "count");

        Assert.AreEqual(RenameRefusalKind.Conflict, plan.Refusal?.Kind, "the procedure's count would be the one it reads");
    }

    [TestMethod]
    public void ANameTheSymbolWouldBecomeNearerThan_IsRefused()
    {
        var context = Compose(("Main", Module("Main",
            "Private total As Long",
            "Public Sub One()",
            "    Dim count As Long",
            "    count = total",
            "End Sub")));

        var plan = Rename(context, "Main", "Dim count", "count", "total");

        Assert.AreEqual(RenameRefusalKind.Conflict, plan.Refusal?.Kind, "the procedure's total would be the local, and no longer the module's");
    }

    [TestMethod]
    public void ANameThatIsNoIdentifier_IsRefused()
    {
        var context = Compose(("Main", Module("Main", "Public Sub One()", "    Dim a As Long", "End Sub")));
        var target = SymbolRename.Prepare(context, ModuleUri("Main"), At(context, "Main", "Dim a", "a")).Target!;

        Assert.AreEqual(RenameRefusalKind.Reserved, SymbolRename.Plan(context, target, "Next").Refusal?.Kind);
        Assert.AreEqual(RenameRefusalKind.NotAnIdentifier, SymbolRename.Plan(context, target, "2nd").Refusal?.Kind);
        Assert.AreEqual(RenameRefusalKind.NotAnIdentifier, SymbolRename.Plan(context, target, "a b").Refusal?.Kind);
        Assert.AreEqual(RenameRefusalKind.Empty, SymbolRename.Plan(context, target, string.Empty).Refusal?.Kind);
        Assert.IsTrue(SymbolRename.Plan(context, target, "Données").IsValid, "a letter of another script is a letter");
    }

    [TestMethod]
    public void WhatTheWorkspaceDoesNotDeclare_IsNotRenamed()
    {
        var context = Compose(("Main", Module("Main", "Public Sub One()", "    Debug.Print Left(\"abc\", 1)", "End Sub")));

        var preparation = SymbolRename.Prepare(context, ModuleUri("Main"), At(context, "Main", "Debug.Print", "Left"));

        Assert.AreEqual(RenameRefusalKind.NotDeclaredByWorkspace, preparation.Refusal?.Kind);
    }

    [TestMethod]
    [DataRow("Main", "Tools.Go", "Tools", DisplayName = "where it qualifies a member")]
    [DataRow("Tools", "VB_Name", "Tools", DisplayName = "in its VB_Name")]
    public void AStandardModule_IsRenamedInItsVBName_AndWhereItQualifiesItsMembers(string module, string line, string text)
    {
        // MS-VBAL §4.2
        var context = Compose(
            ("Tools", Module("Tools", "Public Sub Go()", "End Sub")),
            ("Main", Module("Main", "Public Sub One()", "    Tools.Go", "    Call Tools.Go", "End Sub")));

        var plan = Rename(context, module, line, text, "Helpers");

        Assert.AreEqual(Module("Helpers", "Public Sub Go()", "End Sub"), Renamed(context, plan, "Tools"));
        Assert.AreEqual(Module("Main", "Public Sub One()", "    Helpers.Go", "    Call Helpers.Go", "End Sub"), Renamed(context, plan, "Main"));
        Assert.AreEqual(ModuleUri("Tools").AbsoluteUri, plan.Module?.AbsoluteUri, "the module whose file is renamed with it");
    }

    [TestMethod]
    public void AClassWithADefaultInstance_IsRenamedWhereItsDefaultInstanceIsUsed()
    {
        string Counter(string name) => Lines(
            "VERSION 1.0 CLASS", "BEGIN", "  MultiUse = -1  'True", "END",
            $"Attribute VB_Name = \"{name}\"", "Attribute VB_PredeclaredId = True", "Public Sub Bump()", "End Sub");
        var context = Compose(("Counter", Counter("Counter")), ("Main", Module("Main", "Public Sub One()", "    Counter.Bump", "    Dim c As Counter", "End Sub")));

        var plan = Rename(context, "Main", "Counter.Bump", "Counter", "Tally");

        Assert.AreEqual(Counter("Tally"), Renamed(context, plan, "Counter"));
        Assert.AreEqual(Module("Main", "Public Sub One()", "    Tally.Bump", "    Dim c As Tally", "End Sub"), Renamed(context, plan, "Main"));
    }

    [TestMethod]
    [DataRow(false, DisplayName = "a standard module")]
    [DataRow(true, DisplayName = "a class module, after its VERSION block")]
    public void AModuleWithNoVBName_GainsItsVBName_AndIsRenamedInItAfterwards(bool isClass)
    {
        // MS-VBAL §4.2: a module whose source does not name it is named by its file.
        string[] header = isClass ? ["VERSION 1.0 CLASS", "BEGIN", "  MultiUse = -1  'True", "END"] : [];
        var user = isClass
            ? Module("Main", "Public Sub One()", "    Dim t As New Tools", "    t.Go", "End Sub")
            : Module("Main", "Public Sub One()", "    Tools.Go", "End Sub");
        var context = Compose(("Tools", Lines([.. header, "Option Explicit", "Public Sub Go()", "End Sub"])), ("Main", user));

        var plan = Rename(context, "Main", isClass ? "Dim t" : "Tools.Go", "Tools", "Helpers");

        var renamed = Renamed(context, plan, "Tools");
        Assert.AreEqual(Lines([.. header, "Attribute VB_Name = \"Helpers\"", "Option Explicit", "Public Sub Go()", "End Sub"]), renamed);
        Assert.AreEqual(ModuleUri("Tools").AbsoluteUri, plan.Module?.AbsoluteUri, "its file is renamed with it");

        var named = Compose(("Helpers", renamed), ("Main", Renamed(context, plan, "Main")));
        var again = Rename(named, "Helpers", "VB_Name", "Helpers", "Kit");
        Assert.AreEqual(Lines([.. header, "Attribute VB_Name = \"Kit\"", "Option Explicit", "Public Sub Go()", "End Sub"]), Renamed(named, again, "Helpers"));
    }

    [TestMethod]
    public void AClass_IsRenamedWhereItsNameIsWritten()
    {
        var context = Compose(("Class1", Class1), ("Main", Module("Main", "Public Sub One()", "    Dim c As Class1", "    Set c = New Class1", "End Sub")));

        var plan = Rename(context, "Main", "Set c", "Class1", "Account");

        Assert.AreEqual(Class1.Replace("\"Class1\"", "\"Account\""), Renamed(context, plan, "Class1"));
        Assert.AreEqual(Module("Main", "Public Sub One()", "    Dim c As Account", "    Set c = New Account", "End Sub"), Renamed(context, plan, "Main"));
    }

    private static readonly string Shape = Lines(
        "VERSION 1.0 CLASS", "BEGIN", "  MultiUse = -1  'True", "END", "Attribute VB_Name = \"IShape\"",
        "Public Sub Draw()", "End Sub",
        "Public Property Get Area() As Double", "End Property");

    private static readonly string Square = Lines(
        "VERSION 1.0 CLASS", "BEGIN", "  MultiUse = -1  'True", "END", "Attribute VB_Name = \"Square\"",
        "Implements IShape",
        "Private Sub IShape_Draw()", "End Sub",
        "Private Property Get IShape_Area() As Double", "    IShape_Area = 1", "End Property",
        "Public Sub Show()", "    IShape_Draw", "End Sub");

    [TestMethod]
    public void AClass_IsRenamedWhereItsNameIsWritten_WithTheProceduresThatImplementIt()
    {
        // MS-VBAL §5.2.4.2, §5.3.1.9: an implementation is named after the interface.
        var context = Compose(
            ("IShape", Shape),
            ("Square", Square),
            ("Main", Module("Main",
                "Public Sub One()",
                "    Dim s As IShape",
                "    Set s = New Square",
                "    If TypeOf s Is IShape Then s.Draw",
                "End Sub")));

        var plan = Rename(context, "Main", "Dim s As IShape", "IShape", "IFigure");

        Assert.AreEqual(Shape.Replace("\"IShape\"", "\"IFigure\""), Renamed(context, plan, "IShape"));
        Assert.AreEqual(Square.Replace("IShape", "IFigure"), Renamed(context, plan, "Square"));
        Assert.AreEqual(Module("Main",
            "Public Sub One()",
            "    Dim s As IFigure",
            "    Set s = New Square",
            "    If TypeOf s Is IFigure Then s.Draw",
            "End Sub"), Renamed(context, plan, "Main"));
    }

    [TestMethod]
    [DataRow("Main", DisplayName = "another module of the project")]
    [DataRow("Size", DisplayName = "a Public Enum of the project")]
    public void AModule_IsNotRenamedToTheNameOfAnotherModule_OrOfAPublicType(string newName)
    {
        // MS-VBAL §4.2, §5.2.3.4
        var context = Compose(
            ("Tools", Module("Tools", "Public Sub Go()", "End Sub")),
            ("Main", Module("Main", "Public Enum Size", "    Small", "End Enum", "Public Sub One()", "    Tools.Go", "End Sub")));

        var preparation = SymbolRename.Prepare(context, ModuleUri("Main"), At(context, "Main", "Tools.Go", "Tools"));

        Assert.AreEqual(RenameRefusalKind.Conflict, SymbolRename.Plan(context, preparation.Target!, newName).Refusal?.Kind);
    }

    [TestMethod]
    [DataRow(StandardLibrary.LibraryName, DisplayName = "the standard library")]
    [DataRow("Host", DisplayName = "a referenced library")]
    public void AModule_IsNotRenamedToTheNameOfALibraryItsProjectReferences(string newName)
    {
        // MS-VBAL §4.2: a module is not named after a project its project references.
        var context = ComposeWithHost(("Tools", Module("Tools", "Public Sub Go()", "End Sub")), ("Main", Module("Main", "Public Sub One()", "    Tools.Go", "End Sub")));
        var preparation = SymbolRename.Prepare(context, ModuleUri("Main"), At(context, "Main", "Tools.Go", "Tools"));

        Assert.AreEqual(RenameRefusalKind.Conflict, SymbolRename.Plan(context, preparation.Target!, newName).Refusal?.Kind);
    }

    [TestMethod]
    public void AModule_IsNotRenamedToANameLongerThanAModuleNameCanBe()
    {
        // MS-VBAL §4.2: at most 31 characters.
        var context = Compose(("Tools", Module("Tools", "Public Sub Go()", "End Sub")), ("Main", Module("Main", "Public Sub One()", "    Tools.Go", "End Sub")));
        var preparation = SymbolRename.Prepare(context, ModuleUri("Main"), At(context, "Main", "Tools.Go", "Tools"));

        Assert.IsTrue(SymbolRename.Plan(context, preparation.Target!, new string('A', 31)).IsValid);
        Assert.AreEqual(RenameRefusalKind.ModuleNameTooLong, SymbolRename.Plan(context, preparation.Target!, new string('A', 32)).Refusal?.Kind);
    }

    [TestMethod]
    public void AnImplementationThatWouldTakeTheNameOfAnotherMember_RefusesTheRename()
    {
        var context = Compose(
            ("IShape", Shape),
            ("Square", Square.Replace("Public Sub Show()", "Public Sub IFigure_Draw()\r\nEnd Sub\r\nPublic Sub Show()")));

        var preparation = SymbolRename.Prepare(context, ModuleUri("Square"), At(context, "Square", "Implements IShape", "IShape"));

        Assert.AreEqual(RenameRefusalKind.Conflict, SymbolRename.Plan(context, preparation.Target!, "IFigure").Refusal?.Kind);
    }

    [TestMethod]
    public void AForm_IsRenamedInItsDesigner_ItsVBName_AndWhereItIsUsed()
    {
        // the designer's text names the form too (Begin ... Dialog); its caption is text, and stays as it is.
        string Form(string name) => Lines(
            "VERSION 5.00", $"Begin {{C62A69F0-16DC-11CE-9E98-00AA00574A4F}} {name}", "   Caption = \"Dialog\"", "End",
            $"Attribute VB_Name = \"{name}\"", "Attribute VB_PredeclaredId = True", "Public Sub Go()", "End Sub");
        var context = Compose(("Dialog", Form("Dialog")), ("Main", Module("Main", "Public Sub One()", "    Dialog.Go", "End Sub")));

        var plan = Rename(context, "Main", "Dialog.Go", "Dialog", "Prompt");

        Assert.AreEqual(Form("Prompt"), Renamed(context, plan, "Dialog"));
        Assert.AreEqual(Module("Main", "Public Sub One()", "    Prompt.Go", "End Sub"), Renamed(context, plan, "Main"));
        Assert.AreEqual(ModuleUri("Dialog").AbsoluteUri, plan.Module?.AbsoluteUri, "the module whose files are renamed with it");
    }

    [TestMethod]
    public void AFormWithNoVBName_GainsIt_AfterItsDesignerAndTheBlocksOfItsControls()
    {
        string[] designer = ["VERSION 5.00", "Begin VB.Form Picker", "   Caption = \"Pick\"", "   Begin VB.CommandButton Ok", "      Caption = \"OK\"", "   End", "End"];
        // a form's file declares its default instance (MS-VBAL §5.2.4.1.2), which Picker.Go uses; its VB_Name goes first of the attributes.
        const string predeclared = "Attribute VB_PredeclaredId = True";
        var context = Compose(
            ("Picker", Lines([.. designer, predeclared, "Public Sub Go()", "End Sub"])),
            ("Main", Module("Main", "Public Sub One()", "    Picker.Go", "End Sub")));

        var plan = Rename(context, "Main", "Picker.Go", "Picker", "Chooser");

        Assert.AreEqual(
            Lines([.. designer.Select(line => line.Replace("VB.Form Picker", "VB.Form Chooser", StringComparison.Ordinal)), "Attribute VB_Name = \"Chooser\"", predeclared, "Public Sub Go()", "End Sub"]),
            Renamed(context, plan, "Picker"));
    }

    [TestMethod]
    [DataRow("Public Enum Color", "Color", DisplayName = "at its declaration")]
    [DataRow("ByVal c As Color", "Color", DisplayName = "in an As clause")]
    [DataRow("Pick = Color.Red", "Color", DisplayName = "before a member")]
    public void AnEnum_IsRenamedWhereItsNameIsWritten_InEveryModule(string line, string text)
    {
        var context = Compose(
            ("Main", Module("Main",
                "Public Enum Color",
                "    Red = 1",
                "End Enum",
                "Public Function Pick(ByVal c As Color) As Color",
                "    Pick = Color.Red",
                "End Function")),
            ("Other", Module("Other",
                "Private Colors() As Color",
                "Public Sub Two()",
                "    Dim c As Main.Color",
                "    ReDim Colors(1) As Color",
                "    c = Pick(Red)",
                "End Sub")));

        var plan = Rename(context, "Main", line, text, "Shade");

        Assert.AreEqual(Module("Main",
            "Public Enum Shade",
            "    Red = 1",
            "End Enum",
            "Public Function Pick(ByVal c As Shade) As Shade",
            "    Pick = Shade.Red",
            "End Function"), Renamed(context, plan, "Main"));
        Assert.AreEqual(Module("Other",
            "Private Colors() As Shade",
            "Public Sub Two()",
            "    Dim c As Main.Shade",
            "    ReDim Colors(1) As Shade",
            "    c = Pick(Red)",
            "End Sub"), Renamed(context, plan, "Other"));
    }

    [TestMethod]
    public void AType_IsRenamedWhereItsNameIsWritten_AndNotAVariableOfTheSameName()
    {
        // MS-VBAL §5.2.3.3: a variable may have the name of a Type; it is not the type.
        var context = Compose(("Main", Module("Main",
            "Private Type Point",
            "    X As Long",
            "End Type",
            "Private Type Segment",
            "    A As Point",
            "End Type",
            "Private Point As Long",
            "Private Function Origin() As Point",
            "    Point = 1",
            "End Function")));

        var plan = Rename(context, "Main", "A As Point", "Point", "Location");

        Assert.AreEqual(Module("Main",
            "Private Type Location",
            "    X As Long",
            "End Type",
            "Private Type Segment",
            "    A As Location",
            "End Type",
            "Private Point As Long",
            "Private Function Origin() As Location",
            "    Point = 1",
            "End Function"), Renamed(context, plan, "Main"));
    }

    [TestMethod]
    [DataRow("Main", "Segment", DisplayName = "a Type of its module")]
    [DataRow("Main", "Shade", DisplayName = "an Enum of its module")]
    [DataRow("Main", "Other", DisplayName = "a module of its project, for a Public type")]
    [DataRow("Main", "Size", DisplayName = "a Public type of another module, for a Public type")]
    public void AType_IsNotRenamedToTheNameOfAnotherTypeItCannotShareItWith(string module, string newName)
    {
        // MS-VBAL §5.2.3.3, §5.2.3.4
        var context = Compose(
            ("Main", Module("Main", "Public Type Point", "    X As Long", "End Type", "Private Type Segment", "    A As Point", "End Type", "Private Enum Shade", "    Red", "End Enum")),
            ("Other", Module("Other", "Public Enum Size", "    Small", "End Enum")));

        var preparation = SymbolRename.Prepare(context, ModuleUri(module), At(context, module, "Public Type Point", "Point"));

        Assert.AreEqual(RenameRefusalKind.Conflict, SymbolRename.Plan(context, preparation.Target!, newName).Refusal?.Kind);
    }

    [TestMethod]
    [DataRow(StandardLibrary.LibraryName, DisplayName = "the standard library")]
    [DataRow("Host", DisplayName = "a referenced library")]
    public void APublicType_IsNotRenamedToTheNameOfALibraryItsProjectReferences(string newName)
    {
        // MS-VBAL §5.2.3.3, §5.2.3.4: no project or library of the environment.
        var context = ComposeWithHost(("Main", Module("Main", "Public Type Point", "    X As Long", "End Type")));

        var preparation = SymbolRename.Prepare(context, ModuleUri("Main"), At(context, "Main", "Public Type Point", "Point"));

        Assert.AreEqual(RenameRefusalKind.Conflict, SymbolRename.Plan(context, preparation.Target!, newName).Refusal?.Kind);
    }

    [TestMethod]
    public void APrivateType_IsRenamedToTheNameOfAPublicTypeOfAnotherModule_WhenNoUseOfThatOneIsInItsModule()
    {
        var context = Compose(
            ("Main", Module("Main", "Private Type Point", "    X As Long", "End Type", "Private p As Point")),
            ("Other", Module("Other", "Public Enum Size", "    Small", "End Enum", "Private s As Size")));

        var plan = Rename(context, "Main", "Private p As Point", "Point", "Size");

        Assert.AreEqual(Module("Main", "Private Type Size", "    X As Long", "End Type", "Private p As Size"), Renamed(context, plan, "Main"));
    }

    [TestMethod]
    public void AType_IsNotRenamedToANameThatMeansAnotherTypeWhereItIsUsed()
    {
        var context = Compose(
            ("Main", Module("Main", "Private Type Point", "    X As Long", "End Type", "Private p As Point", "Private s As Size")),
            ("Other", Module("Other", "Public Enum Size", "    Small", "End Enum")));

        var preparation = SymbolRename.Prepare(context, ModuleUri("Main"), At(context, "Main", "Private p As Point", "Point"));

        var plan = SymbolRename.Plan(context, preparation.Target!, "Size");

        Assert.AreEqual(RenameRefusalKind.Conflict, plan.Refusal?.Kind, "Main's s would be a Point");
    }

    [TestMethod]
    public void AnEnum_IsNotRenamedToTheNameOfAValue_WhereAMemberIsQualifiedByIt()
    {
        var context = Compose(("Main", Module("Main",
            "Private Enum Color", "    Red", "End Enum",
            "Public Sub One()", "    Dim Shade As Long", "    Shade = Color.Red", "End Sub")));

        var preparation = SymbolRename.Prepare(context, ModuleUri("Main"), At(context, "Main", "Private Enum Color", "Color"));

        Assert.AreEqual(RenameRefusalKind.Conflict, SymbolRename.Plan(context, preparation.Target!, "Shade").Refusal?.Kind);
    }

    [TestMethod]
    public void WhereNoNameIsWritten_ThereIsNothingToRename()
    {
        var context = Compose(("Main", Module("Main", "Public Sub One()", "    Debug.Print 42", "End Sub")));

        var preparation = SymbolRename.Prepare(context, ModuleUri("Main"), At(context, "Main", "Debug.Print", "42"));

        Assert.AreEqual(RenameRefusalKind.NoName, preparation.Refusal?.Kind);
    }

    // a French project's names: what a refusal says names the module, the symbol and the place as the code is written, not as an address escapes them.
    [TestMethod]
    public void AConflictInAModuleNamedWithAnAccent_NamesWhatItIsAndWhere_AsTheCodeDoes()
    {
        var context = Compose(("Modèle", Module("Modèle", "Public Sub Calculer()", "    Dim a As Long", "    Dim Durée As Long", "    a = Durée", "End Sub")));

        var plan = Rename(context, "Modèle", "Dim a", "a", "durée");

        Assert.AreEqual(RenameRefusalKind.Conflict, plan.Refusal?.Kind);
        var message = plan.Refusal!.ToMessage(culture: CultureInfo.InvariantCulture);
        StringAssert.Contains(message, "Modèle.Calculer.Durée");
        Assert.IsFalse(message.Contains('%', StringComparison.Ordinal), message);
    }

    [TestMethod]
    public void TheMemberOfAClassThatAClassNamedWithAnAccentImplements_IsRenamedInTheImplementation()
    {
        var carré = Lines("VERSION 1.0 CLASS", "BEGIN", "  MultiUse = -1  'True", "END", "Attribute VB_Name = \"Carré\"", "Implements IForme", "Private Sub IForme_Dessiner()", "End Sub");
        var context = Compose(
            ("IForme", Lines("VERSION 1.0 CLASS", "BEGIN", "  MultiUse = -1  'True", "END", "Attribute VB_Name = \"IForme\"", "Public Sub Dessiner()", "End Sub")),
            ("Carré", carré));

        var plan = Rename(context, "IForme", "Public Sub Dessiner", "Dessiner", "Tracer");

        Assert.AreEqual(carré.Replace("IForme_Dessiner", "IForme_Tracer"), Renamed(context, plan, "Carré"));
    }

    [TestMethod]
    public void AFieldOfATypeNamedWithAnAccent_IsRenamedWhereItIsUsed()
    {
        var context = Compose(("Modèle", Module("Modèle",
            "Private Type Personne",
            "    Prénom As String",
            "End Type",
            "Public Sub Saluer()",
            "    Dim p As Personne",
            "    p.Prénom = \"Éloïse\"",
            "End Sub")));

        var plan = Rename(context, "Modèle", "p.Prénom", "Prénom", "Nom");

        Assert.AreEqual(Module("Modèle",
            "Private Type Personne", "    Nom As String", "End Type",
            "Public Sub Saluer()", "    Dim p As Personne", "    p.Nom = \"Éloïse\"", "End Sub"), Renamed(context, plan, "Modèle"));
    }

    [TestMethod]
    public void ARefusal_SaysWhyInTheLanguageOfTheReader()
    {
        var refusal = new RenameRefusal(RenameRefusalKind.Reserved, "Next");

        Assert.AreEqual("'Next' is a keyword of the language: nothing can be named 'Next'.", refusal.ToMessage(culture: CultureInfo.InvariantCulture));
        Assert.AreEqual("« Next » est un mot clé du langage : rien ne peut être nommé « Next ».", refusal.ToMessage(culture: new CultureInfo("fr-CA")));
    }

    // what a library calls by its name in a string is only known when the code runs: the string is renamed too, and its edit says so, for the user to confirm.
    [TestMethod]
    public void AProcedureCalledByItsNameInAString_IsRenamedThere_AsAnEditToConfirm()
    {
        var context = ComposeWithHost(
            ("Main", Module("Main",
                "Public Sub Foo()",
                "End Sub",
                "Public Sub Bar(ByVal app As Application, ByVal b As Button)",
                "    Dim o As Object",
                "    CallByName o, \"Foo\", VbMethod",
                "    app.Run \"Main.Foo\"",
                "    app.Run Macro:=\"'Book1.xlsm'!Foo\"",
                "    app.OnTime Now, \"Foo\"",
                "    b.OnAction = \"Main.Foo\"",
                "End Sub")));

        var plan = Rename(context, "Main", "Public Sub Foo", "Foo", "Baz");

        Assert.AreEqual(Module("Main",
            "Public Sub Baz()",
            "End Sub",
            "Public Sub Bar(ByVal app As Application, ByVal b As Button)",
            "    Dim o As Object",
            "    CallByName o, \"Baz\", VbMethod",
            "    app.Run \"Main.Baz\"",
            "    app.Run Macro:=\"'Book1.xlsm'!Baz\"",
            "    app.OnTime Now, \"Baz\"",
            "    b.OnAction = \"Main.Baz\"",
            "End Sub"), Renamed(context, plan, "Main"));
        Assert.AreEqual(1, plan.Edits.Count(edit => !edit.IsInString), "the declaration is the only edit that is not in a string");
        Assert.AreEqual(5, plan.Edits.Count(edit => edit.IsInString));
    }

    // another class with a member of the same name as Class1's.
    private static readonly string Class2 = Lines(
        "VERSION 1.0 CLASS",
        "BEGIN",
        "  MultiUse = -1  'True",
        "END",
        "Attribute VB_Name = \"Class2\"",
        "Option Explicit",
        "Public Value As String",
        "Private Sub Helper()",
        "End Sub");

    // which member a late-bound access calls is only known when the code runs: where the object may be of the renamed member's class, the access is renamed,
    // and its edit says so, for the user to confirm, and whether what the code says of the object backs the member.
    [TestMethod]
    [DataRow("Public Sub Bar(ByVal o As Object)", "", false, DisplayName = "a parameter, which the callers assign")]
    [DataRow("Public Sub Bar()", "    Dim o As Object|    Set o = New Class1", true, DisplayName = "a local Set to the class")]
    [DataRow("Public Sub Bar()", "    Dim o As Variant|    Set o = New Class2|    Set o = New Class1", true, DisplayName = "a Variant Set to either class")]
    [DataRow("Public Sub Bar()", "    Dim o As Object|    Set o = Make()", false, DisplayName = "a local Set to an Object a function returns")]
    [DataRow("Public Sub Bar()", "    Dim o As Object|    Set o = New Class2|    Take o", false, DisplayName = "a local given to a call, which may Set it")]
    [DataRow("Public Sub Bar()", "    Dim o As Object|    Set o = New Class1|    Take o", true, DisplayName = "a local Set to the class, given to a call")]
    [DataRow("Public Sub Bar(ByVal items As Collection)", "    Dim o As Object|    For Each o In items|    Next", false, DisplayName = "the variable of a For Each loop")]
    public void ALateBoundAccess_OfAnObjectThatMayBeOfTheClass_IsRenamed_AsAnEditToConfirm(string header, string assigned, bool isBacked)
    {
        var context = Compose(("Class1", Class1), ("Class2", Class2), ("Main", Module("Main",
            [
                header,
                .. assigned.Split('|', StringSplitOptions.RemoveEmptyEntries),
                "    o.Value = \"x\"",
                "    With o",
                "        Debug.Print .Value",
                "    End With",
                "End Sub",
                "Public Function Make() As Object",
                "End Function",
                "Public Sub Take(ByRef o As Object)",
                "End Sub",
            ])));

        var plan = Rename(context, "Class1", "Public Property Get Value", "Value", "Amount");

        Assert.IsTrue(plan.IsValid, plan.Refusal?.ToMessage(culture: CultureInfo.InvariantCulture));
        var lateBound = plan.Edits.Where(edit => edit.IsLateBound).ToList();
        Assert.HasCount(2, lateBound, "the access after a '.', and the one in the With block");
        Assert.IsTrue(lateBound.All(edit => SymbolUri.ModuleNameOf(edit.Module) == "Main" && !edit.IsInString));
        Assert.IsTrue(lateBound.All(edit => edit.IsBacked == isBacked), isBacked ? "the code says the object holds a Class1" : "nothing says what it holds");
        StringAssert.Contains(Renamed(context, plan, "Main"), "    o.Amount = \"x\"");
        StringAssert.Contains(Renamed(context, plan, "Main"), "        Debug.Print .Amount");
        Assert.IsFalse(plan.Edits.Any(edit => SymbolUri.ModuleNameOf(edit.Module) == "Class2"), "Class2's own Value is another member");
    }

    // where the assignments of the code say every class the object may be of, and the renamed member's is not one of them, the access is not renamed.
    [TestMethod]
    [DataRow("    Dim o As Object|    Set o = New Class2", DisplayName = "a local Set to another class only")]
    [DataRow("    Dim o As Object|    Set o = New Class2|    Set o = Nothing", DisplayName = "a local Set to another class, then Nothing")]
    [DataRow("    Dim o As Object", DisplayName = "a local never Set, which holds Nothing")]
    [DataRow("    Set o = New Class2", DisplayName = "a Private variable of the module, Set to another class only")]
    public void ALateBoundAccess_OfAnObjectOfAnotherClassOnly_IsNotRenamed(string assigned)
    {
        var context = Compose(("Class1", Class1), ("Class2", Class2), ("Main", Module("Main",
            [
                "Option Explicit",
                "Private o As Object",
                "Public Sub Bar()",
                .. assigned.Split('|', StringSplitOptions.RemoveEmptyEntries),
                "    o.Value = \"x\"",
                "End Sub",
            ])));

        var plan = Rename(context, "Class1", "Public Property Get Value", "Value", "Amount");

        Assert.IsTrue(plan.IsValid, plan.Refusal?.ToMessage(culture: CultureInfo.InvariantCulture));
        Assert.IsFalse(plan.Edits.Any(edit => edit.IsLateBound || SymbolUri.ModuleNameOf(edit.Module) == "Main"));
    }

    // a member that code outside of its class can't call - a Private one, or one of a standard module - is never what a late-bound access calls.
    [TestMethod]
    public void ALateBoundAccess_OfAPrivateMember_OrOfAStandardModulesMember_IsNotRenamed()
    {
        var context = Compose(("Class2", Class2), ("Helpers", Module("Helpers",
                "Public Sub Run()",
                "End Sub")),
            ("Main", Module("Main",
                "Public Sub Bar(ByVal o As Object)",
                "    o.Helper",
                "    o.Run",
                "End Sub")));

        var helper = Rename(context, "Class2", "Private Sub Helper", "Helper", "Assist");
        var run = Rename(context, "Helpers", "Public Sub Run", "Run", "Go");

        Assert.IsFalse(helper.Edits.Any(edit => edit.IsLateBound), "a Private member is not found late-bound");
        Assert.IsFalse(run.Edits.Any(edit => edit.IsLateBound), "an object is never a standard module");
    }

    // what is known of the classes an object may be of is open: a source the workspace is given says what the code does not.
    [TestMethod]
    public void ALateBoundAccess_IsNotRenamed_WhereASourceSaysTheObjectIsOfAnotherClassOnly()
    {
        var context = Compose(("Class1", Class1), ("Class2", Class2), ("Main", Module("Main",
            "Public Sub Bar(ByVal o As Object)",
            "    o.Value = \"x\"",
            "End Sub")));
        var class2 = context.Resolver.ResolveType("Class2", RDCore.SDK.Model.Symbols.Abstract.ScopeKind.Local, ModuleUri("Main")).Symbol!.SemanticId;
        var class1 = context.Resolver.ResolveType("Class1", RDCore.SDK.Model.Symbols.Abstract.ScopeKind.Local, ModuleUri("Main")).Symbol!.SemanticId;
        var known = context with { ClassSources = [new KnownClasses(new PossibleClasses([class2], IsComplete: true))] };
        var seen = context with { ClassSources = [new KnownClasses(new PossibleClasses([class1], IsComplete: false))] };

        var open = Rename(context, "Class1", "Public Property Get Value", "Value", "Amount");
        var closed = Rename(known, "Class1", "Public Property Get Value", "Value", "Amount");
        var backed = Rename(seen, "Class1", "Public Property Get Value", "Value", "Amount");

        Assert.AreEqual(1, open.Edits.Count(edit => edit.IsLateBound && !edit.IsBacked), "the parameter may be of any class");
        Assert.IsFalse(closed.Edits.Any(edit => edit.IsLateBound), "the source rules Class1 out");
        Assert.AreEqual(1, backed.Edits.Count(edit => edit.IsLateBound && edit.IsBacked), "the source says the parameter holds a Class1, among others");
    }

    // a source that knows the classes of every expression it is asked about.
    private sealed class KnownClasses(PossibleClasses classes) : IPossibleClassesSource
    {
        public PossibleClasses? ClassesOf(PossibleClassesQuery query) => classes;
    }

    // a string is renamed only where a library calls a procedure by it: not a message, not the name of a local, not a member of another module.
    [TestMethod]
    public void AStringThatDoesNotCallTheProcedure_IsNotRenamed()
    {
        var context = ComposeWithHost(
            ("Main", Module("Main",
                "Public Sub Foo()",
                "End Sub",
                "Public Sub Bar(ByVal app As Application)",
                "    MsgBox \"Foo\"",
                "    app.Run \"Other.Foo\"",
                "    Debug.Print \"Foo\"",
                "End Sub")),
            ("Other", Module("Other", "Public Sub Foo()", "End Sub")));

        var plan = Rename(context, "Main", "Public Sub Foo", "Foo", "Baz");

        Assert.IsTrue(plan.IsValid, plan.Refusal?.ToMessage(culture: CultureInfo.InvariantCulture));
        Assert.IsFalse(plan.Edits.Any(edit => edit.IsInString), "no string calls Main.Foo");
        Assert.HasCount(1, plan.Edits);
    }

    // a local is not called by its name: a string that names it is a string.
    [TestMethod]
    public void ALocalNamedByAString_IsRenamed_AndTheStringIsNot()
    {
        var context = ComposeWithHost(("Main", Module("Main",
            "Public Sub Bar()",
            "    Dim Foo As Object",
            "    CallByName Foo, \"Foo\", VbMethod",
            "End Sub")));

        var plan = Rename(context, "Main", "Dim Foo", "Foo", "Baz");

        Assert.AreEqual(Module("Main",
            "Public Sub Bar()",
            "    Dim Baz As Object",
            "    CallByName Baz, \"Foo\", VbMethod",
            "End Sub"), Renamed(context, plan, "Main"));
    }

    [TestMethod]
    public void TheConfirmation_IsAskedInTheLanguageOfTheReader()
    {
        Assert.AreEqual("'Foo' is called by its name in 2 strings, the first at Main 5:17. Rename it there too?",
            RefactoringTitles.RenameInStringQuestion("Foo", 2, "Main 5:17", CultureInfo.InvariantCulture));
        Assert.AreEqual("« Foo » est appelé par son nom dans une chaîne, à Main 5:17. Le renommer là aussi?",
            RefactoringTitles.RenameInStringQuestion("Foo", 1, "Main 5:17", new CultureInfo("fr-CA")));
    }

    // a procedure that names its labels in every statement that transfers execution to a label, and another that has a label of the same name.
    private static readonly string Labelled = Module("Main",
        "Public Sub Main()",
        "    On Error GoTo Failed",
        "    Dim n As Long",
        "Retry:",
        "    n = n + 1",
        "    If n < 3 Then GoTo Retry",
        "    On n GoTo Retry, Done",
        "    On n GoSub Done, Retry",
        "    GoSub Retry",
        "Done:",
        "    Exit Sub",
        "Failed:",
        "    Resume Retry",
        "End Sub",
        "Public Sub Other()",
        "Retry:",
        "    GoTo Retry",
        "End Sub");

    [TestMethod]
    [DataRow("Retry:", DisplayName = "at its definition")]
    [DataRow("If n < 3 Then GoTo Retry", DisplayName = "at a GoTo that names it")]
    public void ALabel_IsRenamedWhereItsProcedureNamesIt_AndNotInAnotherProcedure(string line)
    {
        var context = Compose(("Main", Labelled));

        var plan = Rename(context, "Main", line, "Retry", "Again");

        Assert.AreEqual(Module("Main",
            "Public Sub Main()",
            "    On Error GoTo Failed",
            "    Dim n As Long",
            "Again:",
            "    n = n + 1",
            "    If n < 3 Then GoTo Again",
            "    On n GoTo Again, Done",
            "    On n GoSub Done, Again",
            "    GoSub Again",
            "Done:",
            "    Exit Sub",
            "Failed:",
            "    Resume Again",
            "End Sub",
            "Public Sub Other()",
            "Retry:",
            "    GoTo Retry",
            "End Sub"), Renamed(context, plan, "Main"));
    }

    [TestMethod]
    public void TheLabelOfAnErrorHandler_IsRenamedInItsOnErrorGoTo()
    {
        var context = Compose(("Main", Labelled));

        var plan = Rename(context, "Main", "On Error GoTo Failed", "Failed", "CleanFail");

        Assert.AreEqual(2, plan.Edits.Length, "the definition and the On Error GoTo");
        StringAssert.Contains(Renamed(context, plan, "Main"), "    On Error GoTo CleanFail\r\n");
    }

    [TestMethod]
    public void ALabelWrittenInBrackets_StaysInBrackets()
    {
        var context = Compose(("Main", Module("Main",
            "Public Sub Main()",
            "    GoTo [Try Again]",
            "[Try Again]:",
            "    Exit Sub",
            "End Sub")));

        var plan = Rename(context, "Main", "GoTo [Try Again]", "Try", "Again");

        Assert.AreEqual(Module("Main",
            "Public Sub Main()",
            "    GoTo [Again]",
            "[Again]:",
            "    Exit Sub",
            "End Sub"), Renamed(context, plan, "Main"));
    }

    [TestMethod]
    public void ALineNumber_IsNotRenamed()
    {
        var context = Compose(("Main", Module("Main",
            "Public Sub Main()",
            "10  Debug.Print Erl",
            "    GoTo 10",
            "End Sub")));

        Assert.AreEqual(RenameRefusalKind.NoName, SymbolRename.Prepare(context, ModuleUri("Main"), At(context, "Main", "GoTo 10", "10")).Refusal?.Kind);
        Assert.AreEqual(RenameRefusalKind.NoName, SymbolRename.Prepare(context, ModuleUri("Main"), At(context, "Main", "10  Debug", "10")).Refusal?.Kind);
    }

    [TestMethod]
    [DataRow("done", RenameRefusalKind.Conflict, DisplayName = "another label of the procedure")]
    [DataRow("Randomize", RenameRefusalKind.Reserved, DisplayName = "Randomize")]
    public void ALabel_IsNotRenamedTo_ANameItCannotHave(string newName, RenameRefusalKind refusal)
    {
        var context = Compose(("Main", Labelled));

        var plan = Rename(context, "Main", "Retry:", "Retry", newName);

        Assert.AreEqual(refusal, plan.Refusal?.Kind);
    }

    [TestMethod]
    public void TheLabelsARangeDefines_AreRenamable()
    {
        var context = Compose(("Main", Labelled));
        var start = At(context, "Main", "Retry:", "Retry");

        var targets = SymbolRename.TargetsIn(context, ModuleUri("Main"), new SourceRange(start with { Character = 0 }, start with { Line = start.Line + 1 }));

        Assert.AreEqual("Retry", targets.Single(target => target.IsLabel).Name);
    }

    private static readonly (string Name, string Source)[] Qualified =
    [
        ("Tools", Module("Tools",
            "Public Sub Go()",
            "End Sub")),
        ("Main", Module("Main",
            "Public Sub Main()",
            "    Billing.Tools.Go",
            "    Billing.Go",
            "End Sub")),
    ];

    [TestMethod]
    public void AProject_IsRenamedInTheQualifiersThatNameIt()
    {
        var context = ComposeReferencing([], Qualified, projectName: "Billing");

        var plan = Rename(context, "Main", "Billing.Tools.Go", "Billing", "Invoicing");

        Assert.AreEqual("Billing", plan.Project, "the project file is renamed in it too");
        Assert.AreEqual(Module("Main",
            "Public Sub Main()",
            "    Invoicing.Tools.Go",
            "    Invoicing.Go",
            "End Sub"), Renamed(context, plan, "Main"));
    }

    [TestMethod]
    public void AProject_IsNotRenamedTo_TheNameOfOneOfItsModules()
    {
        var context = ComposeReferencing([], Qualified, projectName: "Billing");

        var plan = Rename(context, "Main", "Billing.Tools.Go", "Billing", "Tools");

        Assert.AreEqual(RenameRefusalKind.Conflict, plan.Refusal?.Kind);
    }
}
