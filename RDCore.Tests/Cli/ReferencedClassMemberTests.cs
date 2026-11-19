using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.CLI.Host.Handlers;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.Runtime.LanguageCore.Symbols;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Support.Host;
using RDCore.Runtime.Support.Host.Handlers;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Platform.Components;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using RDCore.Symbols;
using RDCore.Tests.Platform;
using System.IO.Abstractions.TestingHelpers;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §4.1, §5.6.10, §5.6.12</strong>, RD-VBAL §2.2.3: the code of a project the project references runs in the project's session. A
/// member of a class that a referenced project exposes is bound from the referencing project, through a variable of the class or of an interface the
/// project exposes, and through the class's default instance; a name the class does not declare is still none of its members. A class of the project
/// with the name of the referenced project's keeps its own members. The referenced project's code binds its names as its own project sees them.
/// </summary>
[TestClass]
public sealed class ReferencedClassMemberTests
{
    private const string LibraryName = "NavalLib";
    private static readonly string Base = Path.Combine(Path.GetTempPath(), "rdcore-referenced-members");
    private static readonly string AppFolder = Path.Combine(Base, "App");
    private static readonly Uri AppRoot = Wire.FileUri(AppFolder);

    private static readonly Source[] Library =
    [
        new("Widget", ModuleType.ClassModule, ModuleWorkspace.ClassModule("Widget",
            "Attribute VB_PredeclaredId = True", "Attribute VB_Exposed = True", "Option Explicit",
            "Public Function Twice(ByVal N As Long) As Long", "    Twice = N * 2", "End Function",
            "Public Property Get Size() As Long", "End Property")),
        new("IShape", ModuleType.ClassModule, ModuleWorkspace.ClassModule("IShape",
            "Attribute VB_Exposed = True", "Option Explicit",
            "Public Property Get Name() As String", "End Property")),
        new("Switches", ModuleType.StdModule, ModuleWorkspace.StandardModule("Switches",
            "Option Explicit", "Public Enum ShipType", "    Cruiser = 3", "End Enum",
            "Public Function HasSwitch(ByVal Line As String) As Boolean", "    HasSwitch = Len(Line) > Secret()", "End Function",
            "Private Function Secret() As Long", "End Function",
            "Public Function Tag() As String", "    Tag = Left(\"abc\", 1)", "End Function")),
        new("Text", ModuleType.StdModule, ModuleWorkspace.StandardModule("Text",
            "Option Explicit", "Public Function Left(ByVal S As String, ByVal N As Long) As String", "    Left = \"own\"", "End Function")),
        new("Internals", ModuleType.StdModule, ModuleWorkspace.StandardModule("Internals",
            "Option Explicit", "Option Private Module", "Public Function Hidden() As Long", "End Function")),
        new("Board", ModuleType.ClassModule, ModuleWorkspace.ClassModule("Board",
            "Attribute VB_PredeclaredId = True", "Attribute VB_Exposed = False", "Option Explicit", "Public Sub Reset()", "End Sub")),
        new("GridCoord", ModuleType.ClassModule, ModuleWorkspace.ClassModule("GridCoord",
            "Attribute VB_PredeclaredId = True", "Attribute VB_Exposed = True", "Option Explicit", "Private mX As Long",
            "Public Function Create(ByVal X As Long, ByVal Y As Long) As GridCoord", "    Dim result As New GridCoord", "    result.X = X",
            "    Set Create = result", "End Function",
            "Public Property Get X() As Long", "    X = mX", "End Property",
            "Public Property Let X(ByVal Value As Long)", "    mX = Value", "End Property")),
    ];

    private static readonly Project NavalLib = new(LibraryName, Library, 1);

    // MS-VBAL §5.6.10: each project is a namespace of its own, and the referenced project's names are read from its own, after the project's.
    [TestMethod]
    public async Task AReferencedProjectsProcedureAndEnum_AreBound_UnqualifiedAndQualified()
    {
        var errors = await LoadErrorsAsync([],
            "Dim t As ShipType", "Dim u As NavalLib.ShipType", "t = Cruiser", "u = ShipType.Cruiser",
            "Debug.Print HasSwitch(\"a\"), Switches.HasSwitch(\"b\"), NavalLib.HasSwitch(\"c\"), NavalLib.Switches.HasSwitch(\"d\")");

        Assert.AreEqual(0, errors.Count, string.Join("; ", errors));
    }

    [TestMethod]
    public async Task AMemberOfAReferencedProjectsClass_IsBound_ThroughAVariable_AnInterface_AndTheDefaultInstance()
    {
        var errors = await LoadErrorsAsync([],
            "Dim w As Widget", "Dim s As IShape", "Dim q As NavalLib.Widget",
            "Debug.Print w.Twice(2), w.Size, q.Twice(1)", "Debug.Print Widget.Twice(3), Widget.Size", "Debug.Print s.Name");

        Assert.AreEqual(0, errors.Count, string.Join("; ", errors));
    }

    [TestMethod]
    public async Task ANameTheReferencedProjectsClassDoesNotDeclare_IsNoneOfItsMembers()
    {
        var errors = await LoadErrorsAsync([], "Dim w As Widget", "Debug.Print w.Nope");

        Assert.IsTrue(errors.Any(error => error.Contains("Nope", StringComparison.Ordinal)), string.Join("; ", errors));
    }

    // the project's Widget is defined after the referenced project's: each class is the one its members are defined under.
    [TestMethod]
    public async Task AClassOfTheProject_NamedLikeAReferencedProjectsClass_KeepsItsOwnMembers()
    {
        var own = new Source("Widget", ModuleType.ClassModule, ModuleWorkspace.ClassModule("Widget", "Option Explicit", "Public Sub Polish()", "End Sub"));

        var errors = await LoadErrorsAsync([own], "Dim w As Widget", "w.Polish", "Dim q As NavalLib.Widget", "Debug.Print q.Twice(1)");

        Assert.AreEqual(0, errors.Count, string.Join("; ", errors));
    }

    // MS-VBAL §4.1, §5.2.1.4, §5.2.3, §5.3.1.1, §5.2.4.1.1: the session defines every name of the referenced project, and the project sees only its
    // public entities: a Private member is accessible within its module only, a private module's members and a class that is not exposed within its
    // project only.
    [TestMethod]
    [DataRow("Debug.Print Secret()", "Secret", DisplayName = "a Private function")]
    [DataRow("Debug.Print Switches.Secret()", "Secret", DisplayName = "a Private function, qualified")]
    [DataRow("Debug.Print Hidden()", "Hidden", DisplayName = "a Public function of a private module")]
    [DataRow("Debug.Print Internals.Hidden()", "Internals", DisplayName = "a private module")]
    [DataRow("Board.Reset", "Board", DisplayName = "the default instance of a class that is not exposed")]
    public async Task ANameTheReferencedProjectDoesNotMakeAccessible_IsNotBound(string statement, string name)
    {
        var errors = await LoadErrorsAsync([], statement);

        Assert.IsTrue(errors.Any(error => error.Contains(name, StringComparison.Ordinal)), string.Join("; ", errors));
    }

    // a module of the project and one of the referenced project with the same name are each found at their own address.
    [TestMethod]
    public async Task TwoModulesWithOneName_AreEachFoundByTheirAddress_AndAnAddressOfNoModuleFindsNone()
    {
        var own = new Source("Widget", ModuleType.ClassModule, ModuleWorkspace.ClassModule("Widget", "Option Explicit", "Public Sub Polish()", "End Sub"));
        var (found, ownFound, unknown) = ((VBModuleSymbol?)null, (VBModuleSymbol?)null, true);

        await LoadAsync([own], [NavalLib], run: false, inspect: provider =>
        {
            var symbols = provider.Session.Symbols;
            _ = symbols.TryGetModule(new UriBuilder(NavalLib.Root) { Fragment = "Widget" }.Uri, out found);
            _ = symbols.TryGetModule(new UriBuilder(AppRoot) { Fragment = "Widget" }.Uri, out ownFound);
            unknown = symbols.TryGetModule(new UriBuilder(NavalLib.Root) { Fragment = "Nowhere" }.Uri, out var none) || none is not null;
        });

        Assert.AreEqual(LibraryName, found?.GetProperty(SymbolProperties.Library));
        Assert.AreEqual(NavalLib.Root.AbsoluteUri, found!.WorkspaceRoot.AbsoluteUri);
        Assert.IsNull(ownFound?.GetProperty(SymbolProperties.Library));
        Assert.AreEqual(AppRoot.AbsoluteUri, ownFound!.WorkspaceRoot.AbsoluteUri);
        Assert.IsFalse(unknown);
    }

    // the referenced project's procedure calls a Private function of its module, and its class's factory makes an object of the class and sets a Private
    // field of it.
    [TestMethod]
    public async Task AReferencedProjectsCode_Runs_WhenTheProjectCallsIntoIt()
    {
        var run = await RunAsync([],
            "Debug.Print HasSwitch(\"a\")", "Debug.Print NavalLib.Switches.HasSwitch(\"\")",
            "Debug.Print TypeName(GridCoord)", "Debug.Print GridCoord.Create(2, 3).X");

        Assert.AreEqual(ExecutionOutcome.Completed, run.Outcome, $"{run.ErrorMessage} {string.Join("; ", run.Diagnostics ?? [])}");
        CollectionAssert.AreEqual(new[] { "True", "False", "GridCoord", "2" }, run.Output.Select(line => line.Trim()).ToArray());
    }

    // MS-VBAL §5.6.10: the referenced project's code binds a name in its own project first, then in the libraries it references; the project's code, in
    // the standard library before the referenced project.
    [TestMethod]
    public async Task AReferencedProjectsCode_BindsItsOwnProjectsNames_BeforeTheStandardLibrarys()
    {
        var run = await RunAsync([], "Debug.Print Tag()", "Debug.Print Left(\"abc\", 1)");

        Assert.AreEqual(ExecutionOutcome.Completed, run.Outcome, $"{run.ErrorMessage} {string.Join("; ", run.Diagnostics ?? [])}");
        CollectionAssert.AreEqual(new[] { "own", "a" }, run.Output.Select(line => line.Trim()).ToArray());
    }

    // MS-VBAL §5.3.1.1: inside a function of the referenced project, the function's name is its result, written with its type character too.
    [TestMethod]
    public async Task AReferencedProjectsFunction_AssignsItsOwnName_WithAndWithoutItsTypeCharacter()
    {
        var labels = new Source("Labels", ModuleType.StdModule, ModuleWorkspace.StandardModule("Labels", "Option Explicit",
            "Public Function Label$(ByVal N As Long)", "    Label$ = \"n\" & N", "End Function",
            "Public Function Plain(ByVal N As Long) As String", "    Plain = \"p\" & N", "End Function"));

        var run = await RunWithAsync(labels, "Debug.Print Label$(3)", "Debug.Print Plain(4)");

        Assert.AreEqual(ExecutionOutcome.Completed, run.Outcome, $"{run.ErrorMessage} {string.Join("; ", run.Diagnostics ?? [])}");
        CollectionAssert.AreEqual(new[] { "n3", "p4" }, run.Output.Select(line => line.Trim()).ToArray());
    }

    // MS-VBAL §5.6.8, §5.4.3.9: New of the referenced project's class creates an object in the session, as one of the project's own class is, and Set
    // assigns it to a variable declared as the class.
    [TestMethod]
    public async Task AReferencedProjectsCode_CreatesAnObjectOfItsClass_AndSetsItToAVariableOfTheClass()
    {
        var factory = new Source("Factory", ModuleType.StdModule, ModuleWorkspace.StandardModule("Factory", "Option Explicit",
            "Public Function Fresh(ByVal X As Long) As GridCoord", "    Dim made As GridCoord", "    Set made = New GridCoord", "    made.X = X",
            "    Set Fresh = made", "End Function"));

        var run = await RunWithAsync(factory, "Debug.Print Fresh(7).X", "Debug.Print TypeName(Fresh(1))");

        Assert.AreEqual(ExecutionOutcome.Completed, run.Outcome, $"{run.ErrorMessage} {string.Join("; ", run.Diagnostics ?? [])}");
        CollectionAssert.AreEqual(new[] { "7", "GridCoord" }, run.Output.Select(line => line.Trim()).ToArray());
    }

    // MS-VBAL §5.6.12, §5.2.4.1.1: the referenced project's code names its own class that it does not expose, qualified by its project's name.
    [TestMethod]
    public async Task AReferencedProjectsCode_NamesItsClassItDoesNotExpose_QualifiedByItsProject()
    {
        var boards = new Source("Boards", ModuleType.StdModule, ModuleWorkspace.StandardModule("Boards", "Option Explicit",
            "Public Function Which() As String", $"    Dim b As {LibraryName}.Board", "    Set b = Board", "    b.Reset", "    Which = TypeName(b)",
            "End Function"));

        var run = await RunWithAsync(boards, "Debug.Print Which()");

        Assert.AreEqual(ExecutionOutcome.Completed, run.Outcome, $"{run.ErrorMessage} {string.Join("; ", run.Diagnostics ?? [])}");
        CollectionAssert.AreEqual(new[] { "Board" }, run.Output.Select(line => line.Trim()).ToArray());
    }

    // MS-VBAL §5.2.4.2, §5.6.10: the interface a class implements is the one its own project names. NavalLib's Ship implements NavalLib's IShape, and
    // the project's Hull the project's own IShape; the project has a Ship of its own too.
    [TestMethod]
    public async Task AClassOfEachProject_ImplementsTheInterfaceOfItsOwnProject_WhereBothHaveOneOfTheName()
    {
        var ship = new Source("Ship", ModuleType.ClassModule, ModuleWorkspace.ClassModule("Ship",
            "Attribute VB_Exposed = True", "Option Explicit", "Implements IShape",
            "Private Property Get IShape_Name() As String", "    IShape_Name = \"ship\"", "End Property"));
        var fleet = new Source("Fleet", ModuleType.StdModule, ModuleWorkspace.StandardModule("Fleet", "Option Explicit",
            "Public Function ShapeName() As String", "    Dim s As IShape", "    Set s = New Ship", "    ShapeName = s.Name", "End Function"));
        Source[] own =
        [
            new("IShape", ModuleType.ClassModule, ModuleWorkspace.ClassModule("IShape", "Option Explicit",
                "Public Property Get Label() As String", "End Property")),
            new("Hull", ModuleType.ClassModule, ModuleWorkspace.ClassModule("Hull", "Option Explicit", "Implements IShape",
                "Private Property Get IShape_Label() As String", "    IShape_Label = \"hull\"", "End Property")),
            new("Ship", ModuleType.ClassModule, ModuleWorkspace.ClassModule("Ship", "Option Explicit", "Public Sub Sail()", "End Sub")),
        ];

        var (errors, run) = await LoadAsync(own, [NavalLib with { Modules = [.. Library, ship, fleet] }], run: true,
            "Debug.Print ShapeName()", "Dim h As IShape", "Set h = New Hull", "Debug.Print h.Label");

        Assert.IsEmpty(errors, string.Join("; ", errors));
        Assert.AreEqual(ExecutionOutcome.Completed, run!.Outcome, $"{run.ErrorMessage} {string.Join("; ", run.Diagnostics ?? [])}");
        CollectionAssert.AreEqual(new[] { "ship", "hull" }, run.Output.Select(line => line.Trim()).ToArray());
    }

    // MS-VBAL §5.3.1.11: a ParamArray of the referenced project's function collects the arguments of the project's call.
    [TestMethod]
    public async Task AReferencedProjectsFunction_CollectsItsParamArray()
    {
        var sums = new Source("Sums", ModuleType.StdModule, ModuleWorkspace.StandardModule("Sums", "Option Explicit",
            "Public Function Total(ParamArray Values() As Variant) As Long", "    Dim Item As Variant", "    For Each Item In Values",
            "        Total = Total + Item", "    Next", "End Function"));

        var run = await RunWithAsync(sums, "Debug.Print Total(1, 2, 3)");

        Assert.AreEqual(ExecutionOutcome.Completed, run.Outcome, $"{run.ErrorMessage} {string.Join("; ", run.Diagnostics ?? [])}");
        CollectionAssert.AreEqual(new[] { "6" }, run.Output.Select(line => line.Trim()).ToArray());
    }

    // MS-VBAL §5.2.3.1.4, §5.2.4.1.1: an exposed class of the referenced project that is not creatable is created As New by the referenced project's code,
    // and by none of the project's.
    [TestMethod]
    public async Task AClassAReferencedProjectDoesNotMakeCreatable_IsCreatedAsNewInItsOwnProjectOnly()
    {
        var ledger = new Source("Ledger", ModuleType.ClassModule, ModuleWorkspace.ClassModule("Ledger",
            "Attribute VB_Creatable = False", "Attribute VB_Exposed = True", "Option Explicit", "Public Sub Post()", "End Sub"));
        var books = new Source("Books", ModuleType.StdModule, ModuleWorkspace.StandardModule("Books", "Option Explicit",
            "Public Function Opened() As String", "    Dim l As New Ledger", "    l.Post", "    Opened = TypeName(l)", "End Function"));
        var accounts = new Source("Accounts", ModuleType.StdModule, ModuleWorkspace.StandardModule("Accounts", "Option Explicit",
            "Public Sub Start()", $"    Dim l As New {LibraryName}.Ledger", "End Sub"));

        var (errors, _) = await LoadAsync([accounts], [NavalLib with { Modules = [.. Library, ledger, books] }], run: false);

        Assert.IsTrue(errors.Any(error => IsOf(error, "Accounts", "Ledger")), string.Join("; ", errors));
        Assert.IsFalse(errors.Any(error => error.StartsWith($"{LibraryName}.", StringComparison.Ordinal)), string.Join("; ", errors));
    }

    // MS-VBAL §4.1: no project references the project that references it, and the referenced project's code sees none of the project's names.
    [TestMethod]
    public async Task AReferencedProjectsCode_DoesNotSeeTheProjectsNames()
    {
        var own = new Source("AppTools", ModuleType.StdModule,
            ModuleWorkspace.StandardModule("AppTools", "Option Explicit", "Public Sub AppOnly()", "End Sub"));
        var caller = new Source("Caller", ModuleType.StdModule,
            ModuleWorkspace.StandardModule("Caller", "Option Explicit", "Public Sub Go()", "    AppOnly", "End Sub"));

        var (errors, _) = await LoadAsync([own], [NavalLib with { Modules = [.. Library, caller] }], run: false);

        Assert.IsTrue(errors.Any(error => IsOf(error, $"{LibraryName}.Caller", "AppOnly")), string.Join("; ", errors));
    }

    // RD-VBAL §2.6.1: a syntax error in the code of a referenced project refuses the run, as one in a module of the project does, and names the module.
    [TestMethod]
    public async Task ASyntaxErrorInAReferencedProjectsCode_RefusesTheRun_NamingTheModule()
    {
        var broken = new Source("Broken", ModuleType.StdModule,
            ModuleWorkspace.StandardModule("Broken", "Option Explicit", "Public Sub Go()", "    Debug.Print 1 +", "End Sub"), HasSyntaxErrors: true);

        var (_, run) = await LoadAsync([], [NavalLib with { Modules = [.. Library, broken] }], run: true, "Debug.Print HasSwitch(\"a\")");

        Assert.AreEqual(ExecutionOutcome.SyntaxError, run!.Outcome, string.Join("; ", run.Output));
        Assert.IsTrue(run.Diagnostics?.Any(error => error.StartsWith($"{LibraryName}.Broken: ", StringComparison.Ordinal)) is true,
            string.Join("; ", run.Diagnostics ?? []));
        Assert.IsEmpty(run.Output);
    }

    // MS-VBAL §4.1: a project reference is the referencing project's own. A project the project references and its referenced project does not, is no
    // name of the referenced project's code.
    [TestMethod]
    [Ignore("The session has no references of a referenced project's own: its code binds against every project and library the project references.")]
    public async Task AReferencedProjectsCode_DoesNotSeeAProjectOnlyTheProjectReferences()
    {
        var extras = new Project("Extras", [new("Sketches", ModuleType.StdModule,
            ModuleWorkspace.StandardModule("Sketches", "Option Explicit", "Public Function Sketch() As String", "End Function"))], 2);
        var caller = new Source("Drawing", ModuleType.StdModule,
            ModuleWorkspace.StandardModule("Drawing", "Option Explicit", "Public Sub Draw()", "    Debug.Print Sketch()", "End Sub"));

        var (errors, _) = await LoadAsync([], [NavalLib with { Modules = [.. Library, caller] }, extras], run: false);

        Assert.IsTrue(errors.Any(error => IsOf(error, $"{LibraryName}.Drawing", "Sketch")), string.Join("; ", errors));
    }

    // MS-VBAL §4.1: the referenced project's code binds against its own references. NavalLib references Charts and the project does not: NavalLib's code
    // that calls Plot loads, and the project's call into it runs.
    [TestMethod]
    [Ignore("The session has no references of a referenced project's own: its code that names one fails to load, and the run is refused.")]
    public async Task AReferencedProjectsCode_ThatNamesItsOwnReference_LoadsAndRuns()
    {
        var charts = new Project("Charts", [new("Plots", ModuleType.StdModule,
            ModuleWorkspace.StandardModule("Plots", "Option Explicit", "Public Function Plot() As Long", "    Plot = 7", "End Function"))], 1);
        var caller = new Source("Report", ModuleType.StdModule,
            ModuleWorkspace.StandardModule("Report", "Option Explicit", "Public Function Charted() As Long", "    Charted = Plot()", "End Function"));

        var (errors, run) = await LoadAsync([], [NavalLib with { Modules = [.. Library, caller], References = [charts] }], run: true,
            "Debug.Print Charted()");

        Assert.IsEmpty(errors, string.Join("; ", errors));
        Assert.AreEqual(ExecutionOutcome.Completed, run!.Outcome, $"{run.ErrorMessage} {string.Join("; ", run.Diagnostics ?? [])}");
        CollectionAssert.AreEqual(new[] { "7" }, run.Output.Select(line => line.Trim()).ToArray());
    }

    // MS-VBAL §4.1: references are not followed. The project referenced by the project's reference is a name of that reference's code, and of none of the
    // project's: NavalLib references Charts, and the project, which references NavalLib only, does not see Plot.
    [TestMethod]
    [Ignore("The session does not know which project references which: the names of every project defined in it are seen by every other.")]
    public async Task AProjectTheReferencedProjectReferences_IsNoNameOfTheProjectsCode()
    {
        var charts = new Project("Charts", [new("Plots", ModuleType.StdModule,
            ModuleWorkspace.StandardModule("Plots", "Option Explicit", "Public Function Plot() As Long", "    Plot = 7", "End Function"))], 2);
        var caller = new Source("Report", ModuleType.StdModule,
            ModuleWorkspace.StandardModule("Report", "Option Explicit", "Public Function Charted() As Long", "    Charted = Plot()", "End Function"));

        var (errors, _) = await LoadAsync([], [NavalLib with { Modules = [.. Library, caller] }, charts], run: false,
            "Debug.Print Charted()", "Debug.Print Plot()");

        Assert.IsFalse(errors.Any(error => error.StartsWith($"{LibraryName}.Report: ", StringComparison.Ordinal)), string.Join("; ", errors));
        Assert.IsTrue(errors.Any(error => IsOf(error, "Program", "Plot")), string.Join("; ", errors));
    }

    // the project's class Holder has a field of a Type of the referenced project, and an object of a class of it, which a function of the referenced project
    // makes. The Type is given a field, and the class a Class_Initialize, while the program waits after an object of Holder was made and let go of: the object
    // of Holder the run again makes has the record as the Type now declares it, and the object made for it runs the Class_Initialize.
    [TestMethod]
    public async Task ATypeAndAClassOfTheReferencedProjectEditedWhileTheProgramWaits_TheObjectOfTheRunAgainHasThem()
    {
        static Source Geometry(params string[] fields) => new("Geometry", ModuleType.StdModule, ModuleWorkspace.StandardModule("Geometry",
            ["Option Explicit", "Public Type Pt", .. fields, "End Type",
                "Public Function NewGauge() As Gauge", "    Set NewGauge = New Gauge", "End Function"]));
        static Source Gauge(params string[] body)
            => new("Gauge", ModuleType.ClassModule, ModuleWorkspace.ClassModule("Gauge", ["Attribute VB_Exposed = True", "Option Explicit", .. body]));
        var holder = new Source("Holder", ModuleType.ClassModule, ModuleWorkspace.ClassModule("Holder",
            "Option Explicit", "Private P As Pt", "Private G As Gauge",
            "Private Sub Class_Initialize()", "    Set G = NewGauge()", "End Sub",
            "Public Function Size() As Long", "    Size = Len(P)", "End Function"));
        var before = NavalLib with { Modules = [.. Library, Geometry("    X As Long"), Gauge()] };
        var after = NavalLib with
        {
            Modules =
            [
                .. Library, Geometry("    X As Long", "    Y As Long"),
                Gauge("Private Sub Class_Initialize()", "    Debug.Print \"gauge\"", "End Sub"),
            ],
        };

        var (stopped, again) = await RunAgainEditedAsync([holder], before, after,
            "Dim H As Holder", "Set H = New Holder", "Debug.Print H.Size", "Set H = Nothing", "Stop");

        CollectionAssert.AreEqual(new[] { "4" }, stopped.Output.Select(line => line.Trim()).ToArray());
        Assert.AreEqual(ExecutionOutcome.Suspended, again.Outcome, again.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "gauge", "8" }, again.Output.Select(line => line.Trim()).ToArray());
    }

    // an error of the module that names the name.
    private static bool IsOf(string error, string module, string name)
        => error.StartsWith($"{module}: ", StringComparison.Ordinal) && error.Contains(name, StringComparison.Ordinal);

    private sealed record class Source(string Name, ModuleType Type, string Text, bool HasSyntaxErrors = false)
    {
        public string FileName => $"{Name}.{(Type is ModuleType.ClassModule ? "cls" : "bas")}";
    }

    // a project the project references, in its folder beside the project's, its precedence, and the projects it references in turn, which the harness
    // does not define in the session, as the language server does not.
    private sealed record class Project(string Name, IReadOnlyList<Source> Modules, int Priority, IReadOnlyList<Project>? References = null)
    {
        public Uri Root => Wire.FileUri(Path.Combine(Base, Name));
    }

    private sealed record class Module(string Name, ModuleType Type, Uri Uri, ModuleParseResult Parse);

    private static async Task<IReadOnlyList<string>> LoadErrorsAsync(IReadOnlyList<Source> own, params string[] main)
        => (await LoadAsync(own, [NavalLib], run: false, main)).Errors;

    private static async Task<ExecuteResult> RunAsync(IReadOnlyList<Source> own, params string[] main)
        => (await LoadAsync(own, [NavalLib], run: true, main)).Run!;

    // the run of Program.Main, with one more module in the referenced project.
    private static async Task<ExecuteResult> RunWithAsync(Source referenced, params string[] main)
        => (await LoadAsync([], [NavalLib with { Modules = [.. Library, referenced] }], run: true, main)).Run!;

    // the referenced projects' modules are defined in the session first, as the language server defines them, then the project's own, then the code of
    // the project's own and of the referenced projects': what stopped a module from loading, each after its module's name, and the run of Program.Main.
    private static async Task<(IReadOnlyList<string> Errors, ExecuteResult? Run)> LoadAsync(
        IReadOnlyList<Source> ownModules, IReadOnlyList<Project> references, bool run, params string[] main)
        => await LoadAsync(ownModules, references, run, inspect: null, whenStopped: null, main: main);

    // the run of Program.Main under a debugger, as far as its Stop, and its run again once the referenced project reads as edited does: each of its
    // modules the edit changes is defined again and has its code loaded again while the program waits, as the language server does with an edit of its
    // document.
    private static async Task<(ExecuteResult Stopped, ExecuteResult Again)> RunAgainEditedAsync(
        IReadOnlyList<Source> own, Project referenced, Project edited, params string[] main)
    {
        ExecuteResult? stopped = null;
        var (errors, again) = await LoadAsync(own, [referenced], run: true, inspect: null, whenStopped: async (provider, result, runAgain) =>
        {
            stopped = result;
            Assert.AreEqual(ExecutionOutcome.Suspended, result.Outcome, result.ErrorMessage);
            var edits = Composed(edited);
            var changed = edits.Modules.Where(module => !referenced.Modules.Contains(edited.Modules.Single(source => source.Name == module.Name))).ToArray();
            foreach (var declareTypesOnly in new[] { true, false })
            {
                foreach (var module in changed)
                {
                    await DefineAsync(provider, AsReferenced(module, edits, withSymbols: true) with { DeclareTypesOnly = declareTypesOnly });
                }
            }

            foreach (var module in changed)
            {
                var code = AsReferenced(module, edits, withSymbols: false) with { ParseResultJson = PlatformJson.Serialize(module.Parse), CodeOnly = true };
                var loaded = await DefineAsync(provider, code);
                Assert.IsFalse(loaded.CodeErrors.Any(), $"{module.Name}: {string.Join("; ", loaded.CodeErrors)}");
            }

            return await runAgain().WaitAsync(TimeSpan.FromMinutes(5));
        }, main: main);

        Assert.AreEqual(0, errors.Count, string.Join("; ", errors));
        return (stopped!, again!);
    }

    // with whenStopped, the program runs under a debugger, and the run is what whenStopped answers, given the run so far and a function that runs it again.
    private static async Task<(IReadOnlyList<string> Errors, ExecuteResult? Run)> LoadAsync(
        IReadOnlyList<Source> ownModules, IReadOnlyList<Project> references, bool run, Action<EnvironmentSessionProvider>? inspect,
        Func<EnvironmentSessionProvider, ExecuteResult, Func<Task<ExecuteResult>>, Task<ExecuteResult>>? whenStopped = null, params string[] main)
    {
        var program = ModuleWorkspace.StandardModule("Program", ["Option Explicit", "Public Sub Main()", .. main, "End Sub"]);
        Source[] own = [.. ownModules, new("Program", ModuleType.StdModule, program)];
        var project = new ProjectFile(AppFolder, new RDCoreProject
        {
            Name = "App",
            Modules = [.. own.Select(module => new RDCoreModule { RelativeUri = module.FileName })],
        });

        var referenced = references.Select(Composed).ToArray();
        var app = Parsed(AppRoot, own);
        var appResolver = WorkspaceSymbolResolver.Compose(
            AppRoot, app.Select(module => (module.Uri, module.Type, module.Parse)), new IntrinsicSymbolResolver(), "App",
            libraries: [.. referenced.SelectMany(each => ReferencedProjectSymbols.Of(AppRoot, each.Reference.Name, each.Symbols, each.Reference.Priority))]);

        // the session reads the project's modules from its folder, as the host does.
        var files = own.ToDictionary(module => Path.Combine(AppFolder, module.FileName), module => new MockFileData(module.Text));
        var profile = new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false, ExecutionEngine: SdkEnvironmentOptions.DefaultExecutionEngine);
        using var provider = new EnvironmentSessionProvider(profile, new MockFileSystem(files), NullLogger<EnvironmentSessionProvider>.Instance);
        provider.Compose(project.ProjectInfo, AppRoot);

        foreach (var declareTypesOnly in new[] { true, false })
        {
            foreach (var each in referenced)
            {
                foreach (var module in each.Modules)
                {
                    await DefineAsync(provider, AsReferenced(module, each, withSymbols: true) with { DeclareTypesOnly = declareTypesOnly });
                }
            }

            foreach (var module in app)
            {
                await DefineAsync(provider, Request(module, Symbols(AppRoot, module, appResolver)) with { DeclareTypesOnly = declareTypesOnly });
            }
        }

        var errors = new List<string>();
        foreach (var module in app)
        {
            var code = Request(module, []) with { ParseResultJson = PlatformJson.Serialize(module.Parse), CodeOnly = true };
            errors.AddRange((await DefineAsync(provider, code)).CodeErrors.Select(error => $"{module.Name}: {error}"));
        }

        foreach (var each in referenced)
        {
            foreach (var module in each.Modules)
            {
                var code = AsReferenced(module, each, withSymbols: false) with { ParseResultJson = PlatformJson.Serialize(module.Parse), CodeOnly = true };
                errors.AddRange((await DefineAsync(provider, code)).CodeErrors.Select(error => $"{each.Reference.Name}.{module.Name}: {error}"));
            }
        }

        inspect?.Invoke(provider);
        if (!run)
        {
            return (errors, null);
        }

        var entry = app.Single(module => module.Name == "Program");
        Task<ExecuteResult> Execute()
            => new HostExecuteHandler(provider, Substitute.For<IErrorDetailsRecorder>(), NullLogger<HostExecuteHandler>.Instance)
                .Handle(new HostExecuteParams
                {
                    Json = PlatformJson.Serialize(new HostExecutePayload(entry.Uri, entry.Parse)),
                    ModuleName = "Program",
                    EntryPoint = "Main",
                    Debug = whenStopped is not null,
                }, CancellationToken.None);

        var result = await Execute();
        return (errors, whenStopped is null ? result : await whenStopped(provider, result, Execute));
    }

    // a referenced project's modules, parsed under its root, the names they declare bound among them, and the symbols each declares.
    private static Referenced Composed(Project reference)
    {
        var modules = Parsed(reference.Root, reference.Modules);
        var resolver = WorkspaceSymbolResolver.Compose(
            reference.Root, modules.Select(module => (module.Uri, module.Type, module.Parse)), new IntrinsicSymbolResolver(), reference.Name);
        return new(reference, modules, resolver, [.. modules.SelectMany(module => Symbols(reference.Root, module, resolver))]);
    }

    // the definition of a referenced project's module, as the language server sends it: with the symbols it declares, or for its code.
    private static DefineSymbolsParams AsReferenced(Module module, Referenced each, bool withSymbols)
        => Request(module, withSymbols ? Symbols(each.Reference.Root, module, each.Resolver) : []) with
        {
            Library = each.Reference.Name,
            LibraryPriority = each.Reference.Priority,
            PrecompilerTriviaJson = string.Empty,
        };

    private sealed record class Referenced(Project Reference, Module[] Modules, ISymbolResolver Resolver, Symbol[] Symbols);

    private static Module[] Parsed(Uri root, IEnumerable<Source> modules) => [.. modules.Select(module =>
    {
        var parse = new ModuleParser().Parse(new Uri(root, module.FileName), module.Text);
        Assert.AreEqual(!module.HasSyntaxErrors, parse.IsSuccess, $"{module.Name}: {string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose))}");
        return new Module(module.Name, module.Type, new UriBuilder(root) { Fragment = module.Name }.Uri, parse);
    })];

    private static IEnumerable<Symbol> Symbols(Uri root, Module module, ISymbolResolver resolver)
        => new SyntaxTreeSymbolProvider(root, module.Uri, module.Type, module.Parse, resolver, withImplicitDeclarations: true).ProvideSymbols();

    // a module of either project is defined in the session of the project, under the session's root.
    private static DefineSymbolsParams Request(Module module, IEnumerable<Symbol> symbols) => new()
    {
        WorkspaceRoot = AppRoot,
        ModuleUri = module.Uri,
        ModuleName = module.Name,
        ModuleType = module.Type,
        Symbols = SymbolDescriptorProjector.Project([.. symbols], module.Uri),
        Directives = ModuleKinds.DirectivesOf(module.Type, module.Parse.SyntaxTree!.GetModuleDirectives()),
        ImplementedInterfaceNames = module.Parse.SyntaxTree!.GetImplementedInterfaceNames(),
        ImplementedInterfaceRanges = module.Parse.SyntaxTree!.GetImplementedInterfaceRanges(),
        PrecompilerTriviaJson = DefineSymbolsParams.PrecompilerTriviaJsonOf(module.Parse),
        Replace = true,
    };

    private static Task<DefineSymbolsResult> DefineAsync(EnvironmentSessionProvider provider, DefineSymbolsParams request)
        => new DefineSymbolsHandler(
                provider, Substitute.For<IProjectAnalysisSessions>(), Substitute.For<IErrorDetailsRecorder>(), NullLogger<DefineSymbolsHandler>.Instance)
            .Handle(Wire.RoundTrip(request), CancellationToken.None);
}
