using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// The environment host counts the references to a declaration that every module of the project can refer to, and nothing outside the project can, over every
/// module of the project: a <c>Friend</c> procedure (MS-VBAL §5.3.1.1), a <c>Public</c> variable or constant of a module that is
/// <c>Option Private Module</c> (MS-VBAL §5.2.1.4), and a <c>Public</c> member of a program, whose run starts at its entry point. A library's members that
/// a referencing project can use count as used by one. A <c>Private</c> procedure is counted within its module (MS-VBAL §5.3.1.1), unless something other
/// than an expression can call it. Each counted reference is stated with where it is.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL 5.0.3 Semantic Analysis")]
public sealed class ProjectReferenceCountTests
{
    private static string Program(params string[] lines)
        => $"Attribute VB_Name = \"Program\"\r\n{string.Join("\r\n", lines)}\r\n";

    private static readonly (string, string) Counter = ("Counter", ModuleWorkspace.ClassModule("Counter",
        "Private mTotal As Long",
        "Friend Sub Add()", "mTotal = mTotal + 1", "End Sub",
        "Friend Function Unused() As Long", "End Function",
        "Friend Property Get Value() As Long", "Value = mTotal", "End Property",
        "Friend Property Let Value(ByVal v As Long)", "mTotal = v", "End Property"));

    private static readonly string[] UsesCounter =
    [
        "Public Sub Main()",
        "Dim c As New Counter",
        "c.Add",
        "c.Add",
        "c.Value = 3",
        "Debug.Print c.Value",
        "End Sub",
    ];

    private static async Task<ModuleSemanticsDto> ModelOfAsync(
        string name, string program, IReadOnlyList<(string Name, string Source)>? classes = null, IReadOnlyList<(string Name, string Source)>? modules = null,
        string moduleName = "", ProjectBuild? build = null)
    {
        var payload = await ModuleWorkspace.SemanticsAsync(classes ?? [Counter], program, moduleName, modules: modules, build: build);
        return payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == name);
    }

    private static DeclarationFactDto Declaration(ModuleSemanticsDto model, string name) => model.Declarations.First(declaration => declaration.Name == name);

    [TestMethod]
    public async Task AFriendProcedure_IsCountedOverEveryModuleOfTheProject()
    {
        var model = await ModelOfAsync("Counter", Program(UsesCounter));

        Assert.AreEqual(new DeclarationReferences(2, 0, 0), Declaration(model, "Add").References);
        Assert.AreEqual(new DeclarationReferences(0, 0, 0), Declaration(model, "Unused").References);
    }

    [TestMethod]
    public async Task AFriendProperty_IsCountedOverEveryAccessor()
        => Assert.AreEqual(new DeclarationReferences(1, 1, 0), Declaration(await ModelOfAsync("Counter", Program(UsesCounter)), "Value").References);

    [TestMethod]
    public async Task ACountedReference_IsStatedWithWhereItIs()
    {
        var sites = Declaration(await ModelOfAsync("Counter", Program(UsesCounter)), "Add").ReferencedFrom;

        Assert.IsNotNull(sites);
        Assert.HasCount(2, sites.Value);
        Assert.IsTrue(sites.Value.All(site => site.Kind == DeclarationReferenceKind.Read && site.Procedure is not null
            && SymbolUri.QualifiedNameOf(site.Procedure) == "Program.Main"));
        Assert.AreEqual(3, sites.Value[0].Location.Range.Start.Line, "on the line that calls it");
        Assert.IsTrue(sites.Value[0].Location.Range.Start < sites.Value[1].Location.Range.Start, "in the order they are written");
    }

    [TestMethod]
    public async Task ACallWithArguments_IsOneReference_AndAFunctionAssigningItsResult_RefersToNothing()
    {
        (string, string) twice = ("Counter", ModuleWorkspace.ClassModule("Counter",
            "Friend Function Twice(ByVal n As Long) As Long", "Twice = n * 2", "End Function",
            "Friend Function Never() As Long", "Never = 1", "End Function"));
        var model = await ModelOfAsync("Counter", Program("Public Sub Main()", "Dim c As New Counter", "Debug.Print c.Twice(2)", "End Sub"), classes: [twice]);

        Assert.AreEqual(new DeclarationReferences(1, 0, 0), Declaration(model, "Twice").References);
        Assert.AreEqual(new DeclarationReferences(0, 0, 0), Declaration(model, "Never").References);
    }

    [TestMethod]
    public async Task AFunctionThatCallsItself_IsReadByItsOwnCall()
    {
        (string, string) recursive = ("Counter", ModuleWorkspace.ClassModule("Counter",
            "Friend Function Again() As Long", "Again = Again()", "End Function"));
        var again = Declaration(await ModelOfAsync("Counter", Program("Public Sub Main()", "End Sub"), classes: [recursive]), "Again");

        Assert.AreEqual(new DeclarationReferences(1, 0, 0), again.References);
        Assert.HasCount(1, again.ReferencedFrom!.Value);
        Assert.AreEqual("Counter.Again", SymbolUri.QualifiedNameOf(again.ReferencedFrom.Value[0].Procedure!), "the site is in the function itself");
    }

    [TestMethod]
    public async Task AFriendProcedureThatAnExcludedBranchOfAnotherModuleCalls_HasNoCount()
    {
        var model = await ModelOfAsync("Counter", Program(
            "#Const FEATURE = 0",
            "Public Sub Main()",
            "Dim c As New Counter",
            "#If FEATURE Then",
            "c.Unused",
            "#Else",
            "c.Add",
            "#End If",
            "End Sub"));

        Assert.IsNull(Declaration(model, "Unused").References, "only the excluded branch calls it");
        Assert.AreEqual(new DeclarationReferences(1, 0, 0), Declaration(model, "Add").References, "the live branch's call is counted");
    }

    [TestMethod]
    public async Task AFunctionCalledAsAStatement_IsAReferenceThatDiscardsTheValueItReturns()
    {
        (string, string) twice = ("Counter", ModuleWorkspace.ClassModule("Counter",
            "Friend Function Twice(ByVal n As Long) As Long", "Twice = n * 2", "End Function"));
        var model = await ModelOfAsync("Counter",
            Program("Public Sub Main()", "Dim c As New Counter", "c.Twice 2", "Debug.Print c.Twice(2)", "End Sub"), classes: [twice]);

        CollectionAssert.AreEqual(new[] { true, false }, Declaration(model, "Twice").ReferencedFrom!.Value.Select(site => site.DiscardsResult).ToArray());
    }

    [TestMethod]
    public async Task AModuleAskedForAlone_IsCountedOverTheWholeProject()
        => Assert.AreEqual(2, Declaration(await ModelOfAsync("Counter", Program(UsesCounter), moduleName: "Counter"), "Add").References?.Reads);

    [TestMethod]
    public async Task APublicVariableOfAPrivateModule_IsCountedAcrossTheProject()
    {
        var model = await ModelOfAsync("Common", Program("Public Sub Main()", "Total = 1", "Debug.Print Total", "End Sub"),
            modules: [("Common", ModuleWorkspace.StandardModule("Common", "Option Private Module", "Public Total As Long", "Public Unread As Long"))]);

        Assert.AreEqual(new DeclarationReferences(1, 1, 0), Declaration(model, "Total").References);
        Assert.AreEqual(new DeclarationReferences(0, 0, 0), Declaration(model, "Unread").References);
    }

    [TestMethod]
    public async Task APublicConstantOfAPrivateModule_IsCountedAcrossTheProject_InTheCodeAndTheDeclarationsOfEachModule()
    {
        var model = await ModelOfAsync("Common",
            Program("Private Const Ceiling As Long = Limit + 1", "Public Sub Main()", "Debug.Print Limit, Ceiling", "End Sub"),
            modules: [("Common", ModuleWorkspace.StandardModule("Common", "Option Private Module",
                "Public Const Limit As Long = 10", "Public Const Twice As Long = Limit * 2", "Public Const Unread As Long = 1"))]);
        var limit = Declaration(model, "Limit");

        Assert.AreEqual(new DeclarationReferences(3, 0, 0), limit.References);
        Assert.AreEqual(2, limit.ReferencedFrom!.Value.Count(site => site.Procedure is null), "the value of a constant is in no procedure");
        Assert.AreEqual(new DeclarationReferences(0, 0, 0), Declaration(model, "Twice").References);
        Assert.AreEqual(new DeclarationReferences(0, 0, 0), Declaration(model, "Unread").References);
    }

    [TestMethod]
    public async Task APublicConstantOfAProgramsModule_IsCountedAcrossTheProject()
    {
        var model = await ModelOfAsync("Common", Program("Public Sub Main()", "Debug.Print Limit", "End Sub"),
            modules: [("Common", ModuleWorkspace.StandardModule("Common", "Public Const Limit As Long = 10", "Public Const Unread As Long = 1"))]);

        Assert.AreEqual(new DeclarationReferences(1, 0, 0), Declaration(model, "Limit").References);
        Assert.AreEqual(new DeclarationReferences(0, 0, 0), Declaration(model, "Unread").References);
    }

    [TestMethod]
    public async Task APublicVariableOfAProgramsModule_IsCountedAcrossTheProject()
    {
        var model = await ModelOfAsync("Common", Program("Public Sub Main()", "Total = 1", "End Sub"),
            modules: [("Common", ModuleWorkspace.StandardModule("Common", "Public Total As Long", "Public Unread As Long"))]);

        Assert.AreEqual(new DeclarationReferences(0, 1, 0), Declaration(model, "Total").References);
        Assert.HasCount(1, Declaration(model, "Total").ReferencedFrom!.Value);
        Assert.AreEqual(new DeclarationReferences(0, 0, 0), Declaration(model, "Unread").References);
    }

    [TestMethod]
    public async Task APublicFunctionOfAProgramsModule_IsCountedAcrossTheProject()
    {
        var model = await ModelOfAsync("Common", Program("Public Sub Main()", "Debug.Print Used(1)", "End Sub"),
            modules: [("Common", ModuleWorkspace.StandardModule("Common",
                "Public Function Used(ByVal n As Long) As Long", "Used = n", "End Function",
                "Function Unused() As Long", "End Function"))]);

        Assert.AreEqual(new DeclarationReferences(1, 0, 0), Declaration(model, "Used").References);
        Assert.AreEqual(new DeclarationReferences(0, 0, 0), Declaration(model, "Unused").References, "a function with no access modifier is Public");
    }

    [TestMethod]
    public async Task APublicVariableOfAProgramsClass_IsCountedAcrossTheProject()
    {
        (string, string) counter = ("Counter", ModuleWorkspace.ClassModule("Counter", "Public Total As Long", "Public Unread As Long"));
        var model = await ModelOfAsync("Counter", Program("Public Sub Main()", "Dim c As New Counter", "c.Total = 1", "End Sub"), classes: [counter]);

        Assert.AreEqual(new DeclarationReferences(0, 1, 0), Declaration(model, "Total").References);
        Assert.AreEqual(new DeclarationReferences(0, 0, 0), Declaration(model, "Unread").References);
    }

    [TestMethod]
    public async Task APublicProcedureOfAProgramsPrivateModule_IsCountedAcrossTheProject()
    {
        var model = await ModelOfAsync("Common", Program("Public Sub Main()", "Helper", "End Sub"),
            modules: [("Common", ModuleWorkspace.StandardModule("Common", "Option Private Module", "Public Sub Helper()", "End Sub"))]);

        Assert.AreEqual(new DeclarationReferences(1, 0, 0), Declaration(model, "Helper").References);
    }

    [TestMethod]
    public async Task TheEntryPoint_HasNoCount_ARunCallsIt()
    {
        var main = Declaration(await ModelOfAsync("Program", Program(UsesCounter)), "Main");

        Assert.AreEqual(DeclarationRole.EntryPoint, main.Role);
        Assert.IsNull(main.References);
    }

    // the interface and the event call them through no expression: what is counted is the code that calls them by their own name, here none.
    [TestMethod]
    public async Task AProgramsEventHandlerAndImplementation_AreCounted_ByTheCallsOfTheirOwnName()
    {
        (string, string) shape = ("IShape", ModuleWorkspace.ClassModule("IShape", "Public Sub Draw()", "End Sub"));
        (string, string) square = ("Square", ModuleWorkspace.ClassModule("Square",
            "Implements IShape",
            "Public Sub IShape_Draw()", "End Sub",
            "Public Sub Class_Initialize()", "End Sub"));
        var program = Program("Public Sub Main()", "Dim s As IShape", "Set s = New Square", "s.Draw", "End Sub");
        var model = await ModelOfAsync("Square", program, classes: [shape, square]);

        Assert.AreEqual(DeclarationRole.InterfaceImplementation, Declaration(model, "IShape_Draw").Role);
        Assert.AreEqual(new DeclarationReferences(0, 0, 0), Declaration(model, "IShape_Draw").References);
        Assert.AreEqual(DeclarationRole.EventHandler, Declaration(model, "Class_Initialize").Role);
        Assert.AreEqual(new DeclarationReferences(0, 0, 0), Declaration(model, "Class_Initialize").References);
        Assert.AreEqual(new DeclarationReferences(1, 0, 0), Declaration(await ModelOfAsync("IShape", program, classes: [shape, square]), "Draw").References,
            "the interface's member is called through it");
    }

    [TestMethod]
    public async Task AMemberOfAProgramsClassThatAnAccessBoundLateNames_HasNoCount()
    {
        (string, string) counter = ("Counter", ModuleWorkspace.ClassModule("Counter", "Public Total As Long", "Public Sub Reset()", "End Sub"));
        var model = await ModelOfAsync("Counter",
            Program("Public Sub Main()", "Dim o As Object", "Set o = New Counter", "o.Total = 1", "End Sub"), classes: [counter]);

        Assert.IsNull(Declaration(model, "Total").References, "the access bound late may be its");
        Assert.AreEqual(new DeclarationReferences(0, 0, 0), Declaration(model, "Reset").References, "no access names it");
    }

    [TestMethod]
    public async Task NoMemberOfAProgramsClass_HasACount_WhenTheProgramCallsByName()
    {
        (string, string) counter = ("Counter", ModuleWorkspace.ClassModule("Counter", "Public Sub Reset()", "End Sub"));
        var model = await ModelOfAsync("Counter",
            Program("Public Sub Main()", "Dim c As New Counter", "CallByName c, \"Re\" & \"set\", vbMethod", "End Sub"), classes: [counter]);

        Assert.IsNull(Declaration(model, "Reset").References);
    }

    [TestMethod]
    public async Task OnlyTheMemberOfAProgramsClassThatCallByNameNamesWithALiteral_HasNoCount()
    {
        (string, string) counter = ("Counter", ModuleWorkspace.ClassModule("Counter", "Public Sub Reset()", "End Sub", "Public Sub Other()", "End Sub"));
        var model = await ModelOfAsync("Counter",
            Program("Public Sub Main()", "Dim c As New Counter", "CallByName c, \"Reset\", vbMethod", "End Sub"), classes: [counter]);

        Assert.IsNull(Declaration(model, "Reset").References);
        Assert.AreEqual(new DeclarationReferences(0, 0, 0), Declaration(model, "Other").References);
    }

    [TestMethod]
    public async Task TheDefaultMemberOfAProgramsClass_HasNoCount_AnObjectUsedAsAValueCallsIt()
    {
        (string, string) holder = ("Holder", ModuleWorkspace.ClassModule("Holder",
            "Public Property Get Value() As Long", "Attribute Value.VB_UserMemId = 0", "Value = 1", "End Property"));
        var model = await ModelOfAsync("Holder", Program("Public Sub Main()", "Dim h As New Holder", "Debug.Print h", "End Sub"), classes: [holder]);

        Assert.IsNull(Declaration(model, "Value").References);
    }

    [TestMethod]
    public async Task APublicMemberOfAProjectWithNoEntryPoint_HasNoCount_ItsHostCanCallIt()
    {
        var model = await ModelOfAsync("Common", Program("Public Sub Run()", "Debug.Print Used()", "End Sub"),
            modules: [("Common", ModuleWorkspace.StandardModule("Common", "Public Total As Long", "Public Function Used() As Long", "End Function"))]);

        Assert.IsNull(Declaration(model, "Used").References);
        Assert.IsNull(Declaration(model, "Total").References);
    }

    [TestMethod]
    public async Task ALibrarysMemberThatAReferencingProjectCanUse_CountsAsUsed_AndHasNoCount()
    {
        (string, string) exposed = ("Shape", ModuleWorkspace.ClassModule("Shape", "Attribute VB_Exposed = True", "Public Sub Draw()", "End Sub"));
        (string, string) inner = ("Inner", ModuleWorkspace.ClassModule("Inner", "Public Sub Hidden()", "End Sub", "Friend Sub Counted()", "End Sub"));
        var build = new ProjectBuild { OutputType = ProjectOutputType.Library };
        var modules = new[]
        {
            ("Common", ModuleWorkspace.StandardModule("Common", "Public Total As Long", "Public Function Used() As Long", "End Function")),
            ("Private", ModuleWorkspace.StandardModule("Private", "Option Private Module", "Public Sub Helper()", "End Sub")),
        };
        async Task<ModuleSemanticsDto> ModelOf(string name)
            => await ModelOfAsync(name, Program("Public Sub Run()", "End Sub"), [exposed, inner], modules, build: build);

        var shared = await ModelOf("Common");
        Assert.IsTrue(Declaration(shared, "Used").IsUsedByReferencingProjects);
        Assert.IsNull(Declaration(shared, "Used").References);
        Assert.IsTrue(Declaration(shared, "Total").IsUsedByReferencingProjects);
        Assert.IsTrue(Declaration(await ModelOf("Shape"), "Draw").IsUsedByReferencingProjects, "the class is exposed");

        Assert.AreEqual(new DeclarationReferences(0, 0, 0), Declaration(await ModelOf("Inner"), "Counted").References, "a Friend member is counted");
        Assert.IsFalse(Declaration(await ModelOf("Private"), "Helper").IsUsedByReferencingProjects, "a private module is no name of a referencing project");
    }

    private static readonly (string, string) Inner = ("Inner", ModuleWorkspace.ClassModule("Inner",
        "Public Sub Hidden()", "End Sub", "Public Sub Called()", "End Sub"));

    // a library of an exposed class Shape and a standard module Common, each with the lines given before the procedure of ClosedShape and ClosedCommon,
    // a class Inner that is not exposed, and a standard module Program whose Sub Run calls Inner's Called.
    private static Task<ModuleSemanticsDto> LibraryModelOfAsync(string name, string[]? common = null, string[]? shape = null)
        => ModelOfAsync(name, Program("Public Sub Run()", "Dim i As New Inner", "i.Called", "End Sub"),
            [Inner, ("Shape", ModuleWorkspace.ClassModule("Shape", ["Attribute VB_Exposed = True", .. shape ?? [], .. ClosedShape]))],
            [("Common", ModuleWorkspace.StandardModule("Common", [.. ClosedCommon[..^2], .. common ?? [], .. ClosedCommon[^2..]]))],
            build: new ProjectBuild { OutputType = ProjectOutputType.Library });

    // what a referencing project can use is a Sub, or is declared as an intrinsic type, an Enum or a user-defined type of intrinsic fields: a library of
    // these alone hands out no object. Common's procedure is its last two lines.
    private static readonly string[] ClosedCommon =
    [
        "Public Enum Shade", "Light", "End Enum",
        "Public Type Point", "X As Long", "Y As Long", "End Type",
        "Public Total As Long",
        "Public Function Where(ByVal s As Shade, ByRef p As Point) As String", "End Function",
    ];

    private static readonly string[] ClosedShape = ["Public Event Drawn(ByVal times As Long)", "Public Sub Draw(ByVal times As Long)", "End Sub"];

    [TestMethod]
    public async Task APublicMemberOfAClassThatIsNotExposed_InALibraryThatHandsOutNoObject_IsCountedOverTheLibrarysCode()
    {
        var model = await LibraryModelOfAsync("Inner");

        Assert.IsTrue(Declaration(await LibraryModelOfAsync("Common"), "Where").IsUsedByReferencingProjects, "the library's own API is used");
        Assert.AreEqual(new DeclarationReferences(1, 0, 0), Declaration(model, "Called").References);
        var hidden = Declaration(model, "Hidden");
        Assert.IsFalse(hidden.IsUsedByReferencingProjects);
        Assert.AreEqual(new DeclarationReferences(0, 0, 0), hidden.References);
    }

    // each row adds one way out to the library of the test above, which hands out no object: the row's line is what makes Hidden reachable from outside.
    [TestMethod]
    [DataRow("Public Function Make() As Object|End Function", "", DisplayName = "a function that returns an Object")]
    [DataRow("Public Function Make()|End Function", "", DisplayName = "a function that returns a Variant")]
    [DataRow("Public Function Make() As Inner|End Function", "", DisplayName = "a function that returns the class")]
    [DataRow("Public Function Make() As Variant()|End Function", "", DisplayName = "a function that returns an array of Variant")]
    [DataRow("Public Held As Collection", "", DisplayName = "a variable of a library class")]
    [DataRow("Public Sub Notify(ByVal listener As Object)|End Sub", "", DisplayName = "a parameter, which the library's code can call back with one")]
    [DataRow("Public Type Pair|Item As Object|End Type|Public Function Make() As Pair|End Function", "",
        DisplayName = "a user-defined type with an Object field")]
    [DataRow("", "Public Event Moved(ByVal by As Object)", DisplayName = "an event of an exposed class, raised with one")]
    public async Task APublicMemberOfAClassThatIsNotExposed_InALibraryThatCanHandOutAnObject_CountsAsUsedByAReferencingProject(string common, string shape)
    {
        string[] Lines(string text) => text.Split('|', StringSplitOptions.RemoveEmptyEntries);
        var hidden = Declaration(await LibraryModelOfAsync("Inner", Lines(common), Lines(shape)), "Hidden");

        Assert.IsTrue(hidden.IsUsedByReferencingProjects, "no referencing project can name the class, and an object of it can be passed to one");
        Assert.IsNull(hidden.References);
    }

    [TestMethod]
    public async Task APrivateProcedureOrProperty_IsCountedWithinItsModule_InAProjectWithNoEntryPoint()
    {
        var model = await ModelOfAsync("Common", Program("Public Sub Run()", "End Sub"),
            modules: [("Common", ModuleWorkspace.StandardModule("Common",
                "Private Sub Helper()", "End Sub",
                "Private Function Unused() As Long", "End Function",
                "Private Property Get Total() As Long", "End Property",
                "Public Sub Work()", "Helper", "Debug.Print Total", "End Sub"))]);

        Assert.AreEqual(new DeclarationReferences(1, 0, 0), Declaration(model, "Helper").References);
        Assert.AreEqual(new DeclarationReferences(0, 0, 0), Declaration(model, "Unused").References);
        Assert.AreEqual(new DeclarationReferences(1, 0, 0), Declaration(model, "Total").References);
    }

    [TestMethod]
    public async Task APrivateProcedureThatAStringOfTheProjectNames_HasNoCount_AHostCanCallItBack()
    {
        var model = await ModelOfAsync("Common", Program("Public Sub Run()", "Debug.Print \"Common.Helper\"", "End Sub"),
            modules: [("Common", ModuleWorkspace.StandardModule("Common", "Private Sub Helper()", "End Sub", "Private Sub Other()", "End Sub"))]);

        Assert.IsNull(Declaration(model, "Helper").References);
        Assert.AreEqual(new DeclarationReferences(0, 0, 0), Declaration(model, "Other").References);
    }

    [TestMethod]
    public async Task APrivateEventHandler_HasNoCount_ItsCallerIsNoExpression()
    {
        (string, string) counter = ("Counter", ModuleWorkspace.ClassModule("Counter", "Private Sub Class_Initialize()", "End Sub"));
        var initialize = Declaration(await ModelOfAsync("Counter", Program("Public Sub Run()", "End Sub"), classes: [counter]), "Class_Initialize");

        Assert.AreEqual(DeclarationRole.EventHandler, initialize.Role);
        Assert.IsNull(initialize.References);
    }

    [TestMethod]
    [DataRow("CallByName Me, \"Re\" & \"set\", vbMethod", null, DisplayName = "a name no string literal gives: nothing of the module is counted")]
    [DataRow("CallByName Me, \"Reset\", vbMethod", 0, DisplayName = "a name a string literal gives: what it names is not counted")]
    [DataRow("CallByName Me, ProcName:=\"Reset\", CallType:=vbMethod", 0, DisplayName = "a name a string literal gives by name")]
    public async Task APrivateProcedureOfAModuleThatCallsByName_IsCountedOnlyWhenEachNameIsALiteral(string call, int? others)
    {
        (string, string) counter = ("Counter", ModuleWorkspace.ClassModule("Counter",
            "Private Sub Reset()", "End Sub", "Private Sub Other()", "End Sub", "Public Sub Run()", call, "End Sub"));
        var model = await ModelOfAsync("Counter", Program("Public Sub Run()", "End Sub"), classes: [counter]);

        Assert.IsNull(Declaration(model, "Reset").References);
        Assert.AreEqual(others, Declaration(model, "Other").References?.Reads);
    }

    // a procedure not fully analyzed refers to a declaration by writing its name (MS-VBAL §5.6.10): one that writes no name of a declaration leaves its
    // count whole.
    [TestMethod]
    public async Task AProcedureNotFullyAnalyzed_ThatWritesNoNameOfADeclaration_LeavesItsCountWhole()
    {
        var model = await ModelOfAsync("Counter", Program(UsesCounter),
            modules: [("Broken", ModuleWorkspace.StandardModule("Broken", "Option Explicit", "Public Sub Bad()", "undeclared = 1", "End Sub"))]);

        Assert.AreEqual(new DeclarationReferences(2, 0, 0), Declaration(model, "Add").References);
        Assert.HasCount(2, Declaration(model, "Add").ReferencedFrom!.Value);
        Assert.AreEqual(new DeclarationReferences(0, 0, 0), Declaration(model, "Unused").References);
    }

    [TestMethod]
    public async Task AProcedureNotFullyAnalyzed_WithholdsTheCountOfEachNameItWrites_AndOfNoOther()
    {
        var model = await ModelOfAsync("Counter", Program(UsesCounter),
            modules: [("Broken", ModuleWorkspace.StandardModule("Broken",
                "Option Explicit", "Public Sub Bad()", "undeclared = 1", "Dim c As New Counter", "c.Add", "End Sub"))]);

        Assert.IsNull(Declaration(model, "Add").References, "the procedure not fully analyzed writes Add");
        Assert.IsNull(Declaration(model, "Add").ReferencedFrom);
        Assert.AreEqual(new DeclarationReferences(1, 1, 0), Declaration(model, "Value").References, "nor Value");
        Assert.AreEqual(new DeclarationReferences(0, 0, 0), Declaration(model, "Unused").References, "nor Unused");
    }

    [TestMethod]
    public async Task APublicVariableOfAPrivateModule_ThatAProcedureNotFullyAnalyzedWrites_HasNoCount()
    {
        var model = await ModelOfAsync("Common", Program("Public Sub Main()", "Total = 1", "Debug.Print Total", "End Sub"),
            modules:
            [
                ("Common", ModuleWorkspace.StandardModule("Common", "Option Private Module", "Public Total As Long", "Public Unread As Long")),
                ("Broken", ModuleWorkspace.StandardModule("Broken", "Option Explicit", "Public Sub Bad()", "undeclared = Total", "End Sub")),
            ]);

        Assert.IsNull(Declaration(model, "Total").References);
        Assert.AreEqual(new DeclarationReferences(0, 0, 0), Declaration(model, "Unread").References);
    }

    [TestMethod]
    public async Task ANameWrittenInAnotherCase_OrWithATypeHint_IsWithheldAsWell()
    {
        var model = await ModelOfAsync("Common", Program("Public Sub Main()", "Total = 1", "End Sub"),
            modules:
            [
                ("Common", ModuleWorkspace.StandardModule("Common", "Option Private Module", "Public Total As Long", "Public Label As String")),
                ("Broken", ModuleWorkspace.StandardModule("Broken", "Option Explicit", "Public Sub Bad()", "undeclared = TOTAL + Len(Label$)", "End Sub")),
            ]);

        Assert.IsNull(Declaration(model, "Total").References);
        Assert.IsNull(Declaration(model, "Label").References);
    }

    [TestMethod]
    public async Task AModuleLocalCount_IsStatedWithWhereEachReferenceIs()
    {
        var model = await ModelOfAsync("Program", Program("Public Sub Main()", "Dim n As Long", "n = 1", "Debug.Print n", "End Sub"));
        var local = Declaration(model, "n");

        Assert.AreEqual(new DeclarationReferences(1, 1, 0), local.References);
        CollectionAssert.AreEqual(
            new[] { DeclarationReferenceKind.Write, DeclarationReferenceKind.Read }, local.ReferencedFrom!.Value.Select(site => site.Kind).ToArray());
    }
}
