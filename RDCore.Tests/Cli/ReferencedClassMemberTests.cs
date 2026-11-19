using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.CLI.Host.Handlers;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.Runtime.LanguageCore.Symbols;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Support.Host;
using RDCore.Runtime.Support.Host.Handlers;
using RDCore.SDK.Model;
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

    // a class of the referenced project with a Friend member of each kind, Public properties whose writers are Friend, the same with Private writers, and a
    // Private member.
    private static readonly Source Crew = new("Crew", ModuleType.ClassModule, ModuleWorkspace.ClassModule("Crew",
        "Attribute VB_Exposed = True", "Option Explicit", "Private mRank As Long", "Private mPost As Object",
        "Friend Sub Promote()", "    mRank = mRank + 1", "End Sub",
        "Friend Function Badge() As String", "    Badge = \"r\" & mRank", "End Function",
        "Friend Property Get Rank() As Long", "    Rank = mRank", "End Property",
        "Friend Property Let Rank(ByVal Value As Long)", "    mRank = Value", "End Property",
        "Public Property Get Grade() As Long", "    Grade = mRank", "End Property",
        "Friend Property Let Grade(ByVal Value As Long)", "    mRank = Value", "End Property",
        "Public Property Get Post() As Object", "    Set Post = mPost", "End Property",
        "Friend Property Set Post(ByVal Value As Object)", "    Set mPost = Value", "End Property",
        "Public Property Get Mark() As Long", "    Mark = mRank", "End Property",
        "Private Property Let Mark(ByVal Value As Long)", "    mRank = Value", "End Property",
        "Public Property Get Desk() As Object", "    Set Desk = mPost", "End Property",
        "Private Property Set Desk(ByVal Value As Object)", "    Set mPost = Value", "End Property",
        "Public Function Serial() As String", "    Serial = \"s\" & mRank", "End Function",
        "Private Sub Hide()", "End Sub"));

    // the referenced project's function that makes a Crew for the project, which cannot create one as New.
    private static readonly Source Barracks = new("Barracks", ModuleType.StdModule, ModuleWorkspace.StandardModule("Barracks", "Option Explicit",
        "Public Function Recruit() As Crew", "    Set Recruit = New Crew", "End Function"));

    // MS-VBAL §5.3.1.1: a Friend procedure is accessible within its own project. Another module of the referenced project calls each, early-bound, and it
    // runs.
    [TestMethod]
    public async Task TheFriendMembersOfAReferencedProjectsClass_AreCalledEarlyBound_ByAnotherModuleOfItsProject()
    {
        var roster = new Source("Roster", ModuleType.StdModule, ModuleWorkspace.StandardModule("Roster", "Option Explicit",
            "Public Function Drill() As String", "    Dim c As Crew", "    Set c = New Crew", "    c.Rank = 2", "    c.Promote",
            "    Drill = c.Badge() & \"/\" & c.Rank", "End Function"));

        var (errors, run) = await LoadAsync([], [NavalLib with { Modules = [.. Library, Crew, roster] }], run: true, "Debug.Print Drill()");

        Assert.IsEmpty(errors, string.Join("; ", errors));
        Assert.AreEqual(ExecutionOutcome.Completed, run!.Outcome, $"{run.ErrorMessage} {string.Join("; ", run.Diagnostics ?? [])}");
        CollectionAssert.AreEqual(new[] { "r3/3" }, run.Output.Select(line => line.Trim()).ToArray());
    }

    // MS-VBAL §5.3.1.1: a Friend procedure is not accessible from a project that references its project. The referencing project's code that calls one
    // early-bound is refused when it loads, with the error a call of the class's Private member is.
    [TestMethod]
    [DataRow("c.Promote", "Promote", DisplayName = "a Friend Sub")]
    [DataRow("Debug.Print c.Badge()", "Badge", DisplayName = "a Friend Function")]
    [DataRow("Debug.Print c.Rank", "Rank", DisplayName = "a Friend Property Get")]
    [DataRow("c.Rank = 1", "Rank", DisplayName = "a Friend Property Let")]
    public async Task AFriendMemberOfAReferencedProjectsClass_CalledEarlyBoundByTheProject_IsRefused_AsItsPrivateMemberIs(string call, string member)
    {
        static Source Caller(string line) => new("Caller", ModuleType.StdModule, ModuleWorkspace.StandardModule("Caller", "Option Explicit",
            "Public Sub Go()", "    Dim c As Crew", "    Set c = New Crew", $"    {line}", "End Sub"));
        Project[] references = [NavalLib with { Modules = [.. Library, Crew] }];

        var (friend, _) = await LoadAsync([Caller(call)], references, run: false);
        var (hidden, _) = await LoadAsync([Caller("c.Hide")], references, run: false);

        var refused = friend.Where(error => IsOf(error, "Caller", member)).ToArray();
        var refusedPrivate = hidden.Where(error => IsOf(error, "Caller", "Hide")).ToArray();
        Assert.HasCount(1, refused, string.Join("; ", friend));
        Assert.HasCount(1, refusedPrivate, string.Join("; ", hidden));
        Assert.AreEqual(refusedPrivate[0].Replace("Hide", "…", StringComparison.Ordinal), refused[0].Replace(member, "…", StringComparison.Ordinal));
    }

    // MS-VBAL §5.3.1.1, §5.6.3: a late-bound access from code of the class's own project reaches a Friend member by its name, as an early-bound one
    // does: the call runs and returns the member's value.
    [TestMethod]
    [DataRow("Object")]
    [DataRow("Variant")]
    public async Task TheFriendMembersOfAReferencedProjectsClass_AreCalledLateBound_ByAnotherModuleOfItsProject(string declaredType)
    {
        var roster = new Source("Roster", ModuleType.StdModule, ModuleWorkspace.StandardModule("Roster", "Option Explicit",
            "Public Function Drill() As String", $"    Dim c As {declaredType}", "    Set c = New Crew", "    c.Rank = 2", "    c.Promote",
            "    Drill = c.Badge() & \"/\" & c.Rank & \"/\" & CallByName(c, \"Badge\", VbMethod)", "End Function"));

        var (errors, run) = await LoadAsync([], [NavalLib with { Modules = [.. Library, Crew, roster] }], run: true, "Debug.Print Drill()");

        Assert.IsEmpty(errors, string.Join("; ", errors));
        Assert.AreEqual(ExecutionOutcome.Completed, run!.Outcome, $"{run.ErrorMessage} {string.Join("; ", run.Diagnostics ?? [])}");
        CollectionAssert.AreEqual(new[] { "r3/3/r3" }, run.Output.Select(line => line.Trim()).ToArray());
    }

    // MS-VBAL §5.3.1.1, §5.6.3: from a project that references the class's project, a Friend member is not accessible on the object: a late-bound
    // access of its name raises error 438, and one of a Public member runs.
    [TestMethod]
    [DataRow("Object", "c.Promote", "438", DisplayName = "a Friend Sub, through an Object")]
    [DataRow("Object", "Debug.Print c.Badge()", "438", DisplayName = "a Friend Function, through an Object")]
    [DataRow("Object", "Debug.Print c.Rank", "438", DisplayName = "a Friend Property Get, through an Object")]
    [DataRow("Object", "c.Rank = 1", "438", DisplayName = "a Friend Property Let, through an Object")]
    [DataRow("Variant", "c.Promote", "438", DisplayName = "a Friend Sub, through a Variant")]
    [DataRow("Variant", "Debug.Print c.Badge()", "438", DisplayName = "a Friend Function, through a Variant")]
    [DataRow("Variant", "Debug.Print c.Rank", "438", DisplayName = "a Friend Property Get, through a Variant")]
    [DataRow("Variant", "c.Rank = 1", "438", DisplayName = "a Friend Property Let, through a Variant")]
    [DataRow("Object", "Debug.Print CallByName(c, \"Badge\", VbMethod)", "438", DisplayName = "a Friend Function, by CallByName")]
    [DataRow("Object", "Debug.Print CallByName(c, \"Rank\", VbGet)", "438", DisplayName = "a Friend Property Get, by CallByName")]
    [DataRow("Object", "Debug.Print c.Grade", "0;0", DisplayName = "a Public Property Get whose Let is Friend, through an Object")]
    [DataRow("Object", "Debug.Print c.Serial()", "s0;0", DisplayName = "a Public Function, through an Object")]
    [DataRow("Variant", "Debug.Print c.Serial()", "s0;0", DisplayName = "a Public Function, through a Variant")]
    [DataRow("Object", "Debug.Print CallByName(c, \"Serial\", VbMethod)", "s0;0", DisplayName = "a Public Function, by CallByName")]
    public async Task AFriendMemberOfAReferencedProjectsClass_CalledLateBoundByTheProject_IsError438_AndAPublicOneRuns(
        string declaredType, string call, string expected)
    {
        var (errors, run) = await LoadAsync([], [NavalLib with { Modules = [.. Library, Crew, Barracks] }], run: true,
            $"Dim c As {declaredType}", "Set c = Recruit()", "On Error Resume Next", call, "Debug.Print Err.Number");

        Assert.IsEmpty(errors, string.Join("; ", errors));
        Assert.AreEqual(ExecutionOutcome.Completed, run!.Outcome, $"{run.ErrorMessage} {string.Join("; ", run.Diagnostics ?? [])}");
        Assert.AreEqual(expected, string.Join(";", run.Output.Select(line => line.Trim())));
    }

    // MS-VBAL §5.3.1.1: the Public Property Get of a property whose writers are Friend is read early-bound from the referencing project.
    [TestMethod]
    public async Task ThePublicGetOfAReferencedProjectsClassProperty_WhoseWritersAreFriend_IsReadByTheProject()
    {
        var (errors, run) = await LoadAsync([], [NavalLib with { Modules = [.. Library, Crew, Barracks] }], run: true,
            "Dim c As Crew", "Set c = Recruit()", "Debug.Print c.Grade", "Debug.Print c.Post Is Nothing");

        Assert.IsEmpty(errors, string.Join("; ", errors));
        Assert.AreEqual(ExecutionOutcome.Completed, run!.Outcome, $"{run.ErrorMessage} {string.Join("; ", run.Diagnostics ?? [])}");
        Assert.AreEqual("0;True", string.Join(";", run.Output.Select(line => line.Trim())));
    }

    // MS-VBAL §5.3.1.1, §5.4.3.8, §5.4.3.9: from a project that references the class's project, the Friend Let or Set of a property whose Get is Public is
    // inaccessible, as a Private one is: the assignment is refused the same way, when it loads or when it runs.
    [TestMethod]
    [DataRow("Crew", "c.Grade = 1", "c.Mark = 1", DisplayName = "a Friend Property Let, early-bound")]
    [DataRow("Crew", "Set c.Post = Nothing", "Set c.Desk = Nothing", DisplayName = "a Friend Property Set, early-bound")]
    [DataRow("Object", "c.Grade = 1", "c.Mark = 1", DisplayName = "a Friend Property Let, through an Object")]
    [DataRow("Variant", "Set c.Post = Nothing", "Set c.Desk = Nothing", DisplayName = "a Friend Property Set, through a Variant")]
    public async Task AFriendWriterOfAReferencedProjectsClassProperty_AssignedByTheProject_IsRefused_AsAPrivateOneIs(
        string declaredType, string friend, string asPrivate)
    {
        Project[] references = [NavalLib with { Modules = [.. Library, Crew, Barracks] }];
        async Task<string> Outcome(string assignment, string name)
        {
            var (errors, run) = await LoadAsync([], references, run: true,
                $"Dim c As {declaredType}", "Set c = Recruit()", "On Error Resume Next", assignment, "Debug.Print Err.Number");
            return errors.Count > 0
                ? string.Join("; ", errors).Replace(name, "…", StringComparison.Ordinal)
                : string.Join(";", run!.Output.Select(line => line.Trim()));
        }

        var refused = await Outcome(friend, friend.Contains("Grade", StringComparison.Ordinal) ? "Grade" : "Post");
        var refusedPrivate = await Outcome(asPrivate, asPrivate.Contains("Mark", StringComparison.Ordinal) ? "Mark" : "Desk");

        Assert.AreEqual(refusedPrivate, refused);
        Assert.AreNotEqual("0", refused);
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
        => await LoadAsync(ownModules, references, run, inspect: null, main);

    private static async Task<(IReadOnlyList<string> Errors, ExecuteResult? Run)> LoadAsync(
        IReadOnlyList<Source> ownModules, IReadOnlyList<Project> references, bool run, Action<EnvironmentSessionProvider>? inspect, params string[] main)
    {
        var program = ModuleWorkspace.StandardModule("Program", ["Option Explicit", "Public Sub Main()", .. main, "End Sub"]);
        Source[] own = [.. ownModules, new("Program", ModuleType.StdModule, program)];
        var project = new ProjectFile(AppFolder, new RDCoreProject
        {
            Name = "App",
            Modules = [.. own.Select(module => new RDCoreModule { RelativeUri = module.FileName })],
        });

        var referenced = references.Select(reference =>
        {
            var modules = Parsed(reference.Root, reference.Modules);
            var resolver = WorkspaceSymbolResolver.Compose(
                reference.Root, modules.Select(module => (module.Uri, module.Kind, module.Parse)), new IntrinsicSymbolResolver(), reference.Name);
            return new ReferencedProjectModules(reference.Name, reference.Priority, reference.Root, modules, resolver);
        }).ToArray();

        var app = Parsed(AppRoot, own);
        var appResolver = WorkspaceSymbolResolver.Compose(
            AppRoot, app.Select(module => (module.Uri, module.Kind, module.Parse)), new IntrinsicSymbolResolver(), "App",
            libraries: [.. referenced.SelectMany(each => each.NamesFor(AppRoot, ImplicitDeclarationScope.Procedure))]);

        // the session reads the project's modules from its folder, as the host does.
        var files = own.ToDictionary(module => Path.Combine(AppFolder, module.FileName), module => new MockFileData(module.Text));
        var profile = new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false, ExecutionEngine: SdkEnvironmentOptions.DefaultExecutionEngine);
        using var provider = new EnvironmentSessionProvider(profile, new MockFileSystem(files), NullLogger<EnvironmentSessionProvider>.Instance);
        provider.Compose(project.ProjectInfo, AppRoot);

        var errors = new List<string>();
        foreach (var definition in SessionDefinitions.Of(AppRoot, app, appResolver, ImplicitDeclarationScope.Procedure, replace: true, referenced))
        {
            if (definition.ToRequest() is not { } request)
            {
                continue;
            }

            var defined = await DefineAsync(provider, request);
            var name = definition.Referenced is { } each ? $"{each.Name}.{definition.Module.Name}" : definition.Module.Name;
            errors.AddRange(defined.CodeErrors.Select(error => $"{name}: {error}"));
        }

        inspect?.Invoke(provider);
        if (!run)
        {
            return (errors, null);
        }

        var entry = app.Single(module => module.Name == "Program");
        var result = await new HostExecuteHandler(provider, Substitute.For<IErrorDetailsRecorder>(), NullLogger<HostExecuteHandler>.Instance)
            .Handle(new HostExecuteParams
            {
                Json = PlatformJson.Serialize(new HostExecutePayload(entry.Uri, entry.Parse)),
                ModuleName = "Program",
                EntryPoint = "Main",
            }, CancellationToken.None);
        return (errors, result);
    }

    private static ParsedModule[] Parsed(Uri root, IEnumerable<Source> modules) => [.. modules.Select(module =>
    {
        var parse = new ModuleParser().Parse(new Uri(root, module.FileName), module.Text);
        Assert.AreEqual(!module.HasSyntaxErrors, parse.IsSuccess, $"{module.Name}: {string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose))}");
        return new ParsedModule(new UriBuilder(root) { Fragment = module.Name }.Uri, module.Name, module.Type, parse);
    })];

    private static Task<DefineSymbolsResult> DefineAsync(EnvironmentSessionProvider provider, DefineSymbolsParams request)
        => new DefineSymbolsHandler(
                provider, Substitute.For<IProjectAnalysisSessions>(), Substitute.For<IErrorDetailsRecorder>(), NullLogger<DefineSymbolsHandler>.Instance)
            .Handle(Wire.RoundTrip(request), CancellationToken.None);
}
