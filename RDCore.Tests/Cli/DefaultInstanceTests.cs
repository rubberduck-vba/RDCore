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
        "Public Function MakeFromAModule(ByVal v As Long) As IThing", "    Set MakeFromAModule = Thing.Create(v)", "End Function"));

    private static readonly (string Name, string Source) Maker = ("Maker", ClassModule("Maker",
        "Option Explicit",
        "Public Function Make(ByVal v As Long) As IThing", "    Set Make = Thing.Create(v)", "End Function"));

    private static Task<string[]> RunAsync(params string[] body)
        => ModuleWorkspace.RunAsync([Interface, Thing, Maker], [Globals],
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

    // a class that is not predeclared has no default instance: its name is no variable (MS-VBAL §5.6.10 lists none).
    [TestMethod]
    public async Task AClassThatIsNotPredeclared_HasNoDefaultInstance()
    {
        var errors = await LoadErrorsAsync([Interface, Thing, Maker], [Globals],
            "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\nDebug.Print TypeName(Maker)\r\nEnd Sub\r\n");

        Assert.IsTrue(errors.Any(error => error.Contains("Variable not defined")), string.Join(" / ", errors));
    }
}
