using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.2.4.1.2</strong>: a class module with <c>VB_PredeclaredId = True</c> has a default instance, a variable named after the class that is
/// never <c>Nothing</c> when it is referred to - run in the environment host, which composes its classes from a project and is told the attribute by the
/// language server. It is what VBA's object-oriented code is built on: a factory method off a stateless default instance, which hands out objects
/// through a get-only interface while only the factory uses their setters.
/// </summary>
[TestClass]
public sealed class DefaultInstanceTests
{
    private static readonly (string Name, string Source) Interface = ("IThing", ClassModule("IThing",
        "Option Explicit",
        "Public Property Get Value() As Long", "End Property"));

    private static readonly (string Name, string Source) Thing = ("Thing", ClassModule("Thing",
        "Attribute VB_PredeclaredId = True",
        "Option Explicit",
        "Implements IThing",
        "Private Type TThing", "    Value As Long", "End Type",
        "Private This As TThing",
        "Public Function Create(ByVal v As Long) As IThing",
        "    Dim result As Thing",
        "    Set result = New Thing",
        "    result.Value = v",
        "    Set Create = result",
        "End Function",
        "Public Property Get Value() As Long", "    Value = This.Value", "End Property",
        "Friend Property Let Value(ByVal v As Long)", "    This.Value = v", "End Property",
        "Public Property Get IsDefaultInstance() As Boolean", "    IsDefaultInstance = Me Is Thing", "End Property",
        "Private Property Get IThing_Value() As Long", "    IThing_Value = This.Value", "End Property",
        "Private Sub Class_Initialize()", "    Created = Created + 1", "End Sub"));

    private static readonly (string Name, string Source) Globals = ("Globals", StandardModule("Globals",
        "Option Explicit",
        "Public Created As Long",
        "Public Initialized As Long",
        "Public Terminated As Long",
        "Public Function MakeFromAModule(ByVal v As Long) As IThing", "    Set MakeFromAModule = Thing.Create(v)", "End Function"));

    private static readonly (string Name, string Source) Maker = ("Maker", ClassModule("Maker",
        "Option Explicit",
        "Public Function Make(ByVal v As Long) As IThing", "    Set Make = Thing.Create(v)", "End Function"));

    // the class of the Excel grid oracle\s18: a predeclared class whose objects count their Initialize and Terminate.
    private static readonly (string Name, string Source) Widget = ("Widget", ClassModule("Widget",
        "Attribute VB_PredeclaredId = True",
        "Option Explicit",
        "Public Size As Long",
        "Private Sub Class_Initialize()", "    Initialized = Initialized + 1", "End Sub",
        "Private Sub Class_Terminate()", "    Terminated = Terminated + 1", "End Sub"));

    private static Task<string[]> RunAsync(params string[] body)
        => ModuleWorkspace.RunAsync([Interface, Thing, Maker, Widget], [Globals],
            "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n" + string.Join("\r\n", body) + "\r\nEnd Sub\r\n");

    [TestMethod]
    public async Task AFactoryOffTheDefaultInstance_GivesAnObjectThroughItsGetOnlyInterface()
        => CollectionAssert.AreEqual(new[] { "42", "7" }, await RunAsync(
            "Dim a As IThing, b As IThing",
            "Set a = Thing.Create(42)",
            "Set b = Thing.Create(7)",
            "Debug.Print a.Value",
            "Debug.Print b.Value"));

    [TestMethod]
    public async Task TheDefaultInstance_IsStateless_WhileTheObjectsItMakesHaveTheirOwnState()
        => CollectionAssert.AreEqual(new[] { "0", "True", "False" }, await RunAsync(
            "Dim a As IThing",
            "Set a = Thing.Create(42)",
            "Debug.Print Thing.Value",
            "Debug.Print Thing.IsDefaultInstance",
            "Dim t As Thing",
            "Set t = a",
            "Debug.Print t.IsDefaultInstance"));

    // the default instance is one object, made the first time the class's name is referred to - which runs its Class_Initialize then, and once.
    [TestMethod]
    public async Task TheDefaultInstance_IsMadeTheFirstTimeItIsReferredTo_AndOnce()
        => CollectionAssert.AreEqual(new[] { "0", "1", "1", "Thing", "False", "True" }, await RunAsync(
            "Debug.Print Created",
            "Debug.Print Thing.Value + Created",
            "Debug.Print Created",
            "Debug.Print TypeName(Thing)",
            "Debug.Print Thing Is Nothing",
            "Debug.Print Thing Is Thing"));

    [TestMethod]
    public async Task TheDefaultInstance_IsNamedTheSameFromAStandardModule_AndFromAnotherClass()
        => CollectionAssert.AreEqual(new[] { "3", "4" }, await RunAsync(
            "Dim m As New Maker",
            "Debug.Print MakeFromAModule(3).Value",
            "Debug.Print m.Make(4).Value"));

    // the default instance is a variable: what is assigned to its members stays with it.
    [TestMethod]
    public async Task WhatIsAssignedToTheDefaultInstance_StaysWithIt()
        => CollectionAssert.AreEqual(new[] { "5", "0" }, await RunAsync(
            "Thing.Value = 5",
            "Debug.Print Thing.Value",
            "Dim t As Thing",
            "Set t = New Thing",
            "Debug.Print t.Value"));

    // MS-VBAL §5.2.4.1.2 says a default instance cannot be the target of a Set; MS-VBA compiles one and runs it (oracle\s18 D02): the object it held is
    // let go of, and terminates, and the next reference to the class's name makes another - with the state a new object has.
    [TestMethod]
    public async Task ADefaultInstanceSetToNothing_IsLetGoOf_AndTheNextReferenceMakesAnother()
        => CollectionAssert.AreEqual(new[] { "5", "1", "0", "2" }, await RunAsync(
            "Widget.Size = 5",
            "Debug.Print Widget.Size",
            "Set Widget = Nothing",
            "Debug.Print Terminated",
            "Debug.Print Widget.Size",
            "Debug.Print Initialized"));

    // oracle\s18 D03: a default instance can be given another object of its class.
    [TestMethod]
    public async Task ADefaultInstance_CanBeSetToAnotherObject()
        => CollectionAssert.AreEqual(new[] { "0", "False" }, await RunAsync(
            "Widget.Size = 5",
            "Set Widget = New Widget",
            "Debug.Print Widget.Size",
            "Debug.Print Widget Is Nothing"));

    // MS-VBAL §2.5.1: an automatic instantiation variable set to Nothing is never Nothing when it is referred to - the reference makes a new object, with
    // the state a new object has, before Is ever sees it.
    [TestMethod]
    public async Task AVariableDeclaredAsNew_SetToNothing_IsANewObjectTheNextTimeItIsReferredTo()
        => CollectionAssert.AreEqual(new[] { "1", "False", "0" }, await RunAsync(
            "Dim Things As New Collection",
            "Things.Add 42",
            "Debug.Print Things.Count",
            "Set Things = Nothing",
            "Debug.Print Things Is Nothing",
            "Debug.Print Things.Count"));

    // a class that is not predeclared has no default instance: its name is no variable (MS-VBAL §5.6.10 lists none).
    [TestMethod]
    public async Task AClassThatIsNotPredeclared_HasNoDefaultInstance()
    {
        var errors = await LoadErrorsAsync([Interface, Thing, Maker, Widget], [Globals],
            "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\nDebug.Print TypeName(Maker)\r\nEnd Sub\r\n");

        Assert.IsTrue(errors.Any(error => error.Contains("Variable not defined")), string.Join(" / ", errors));
    }

    // the host's Debug object is not a name a Set can be written with: MS-VBA reports a syntax error (oracle\s18 D09).
    [TestMethod]
    public async Task TheDebugObject_IsNoTargetOfASetAssignment()
    {
        var errors = await LoadErrorsAsync([], [], "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\nSet Debug = Nothing\r\nEnd Sub\r\n");

        Assert.IsTrue(errors.Any(error => error.Contains("Syntax error")), string.Join(" / ", errors));
    }
}
