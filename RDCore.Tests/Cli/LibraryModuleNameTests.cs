using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A name that a library's procedural module has - <c>Strings</c>, <c>Math</c> - is the module unless another procedural module of the enclosing project has
/// an accessible member of that name, which comes first (<strong>MS-VBAL §5.6.10</strong>: the enclosing project's procedural modules' members, then a
/// referenced project's procedural modules). Run through <see cref="ModuleWorkspace"/>, so the binding that loads the module is the one that runs it. As a
/// declared type, the name is likewise the project's own user-defined type or Enum of that name before it is the library's module.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.10 Simple Name Expressions")]
public sealed class LibraryModuleNameTests
{
    private static string Program(params string[] body)
        => $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";

    private static readonly (string Name, string Source) Widget = ("Widget", ClassModule("Widget", "Option Explicit", "Public MaxRows As Long"));

    [TestMethod]
    [DataRow("Strings", DisplayName = "A variable named like the Strings module")]
    [DataRow("Math", DisplayName = "A variable named like the Math module")]
    public async Task AnotherModulesPublicVariable_NamedLikeALibrarysModule_IsTheVariable(string name)
    {
        // the library's module has no MaxRows: bound to it, the module did not load ("Method or data member not found").
        var mod2 = ("Mod2", StandardModule("Mod2", "Option Explicit", $"Public {name} As New Widget"));

        var output = await RunAsync([Widget], [mod2], Program($"{name}.MaxRows = 3", $"Debug.Print {name}.MaxRows"));

        CollectionAssert.AreEqual(new[] { "3" }, output);
    }

    [TestMethod]
    public async Task AnotherModulesRecord_NamedLikeALibrarysModule_IsReadThroughItsField()
    {
        // bound to the library's module, Strings.Left was its function Left, called with no argument: error 449.
        var mod2 = ("Mod2", StandardModule("Mod2", "Option Explicit", "Public Type Rect", "Left As Long", "End Type", "Public Strings As Rect"));

        CollectionAssert.AreEqual(new[] { "0" }, await RunAsync([], [mod2], Program("Debug.Print Strings.Left")));
    }

    [TestMethod]
    [DataRow("Public Const Strings As Long = 7", "Strings", "7", DisplayName = "A constant named like the Strings module")]
    [DataRow("Public Function Math() As Long\r\nMath = 9\r\nEnd Function", "Math", "9", DisplayName = "A function named like the Math module")]
    public async Task AnotherModulesMember_NamedLikeALibrarysModule_IsWhatTheNameMeans(string declaration, string name, string expected)
    {
        // bound to the library's module, the name was a namespace read as a value, which stopped as "cannot run yet".
        var mod2 = ("Mod2", StandardModule("Mod2", "Option Explicit", declaration));

        CollectionAssert.AreEqual(new[] { expected }, await RunAsync([], [mod2], Program($"Debug.Print {name}")));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.4 Type Binding Context")]
    public async Task AnotherModulesType_NamedLikeALibrarysModule_IsTheType()
    {
        // MS-VBAL §5.6.10's type binding context ranks another module's accessible type ahead of a referenced project's procedural module. Bound to the
        // library's module, the variable was no record, and x.X stopped as "cannot run yet".
        var mod2 = ("Mod2", StandardModule("Mod2", "Option Explicit", "Public Type Strings", "X As Long", "End Type"));

        CollectionAssert.AreEqual(new[] { "3" }, await RunAsync([], [mod2], Program("Dim x As Strings", "x.X = 3", "Debug.Print x.X")));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.4 Type Binding Context")]
    public async Task AnotherModulesEnum_NamedLikeALibrarysModule_IsTheType()
    {
        // a variable of the Enum holds a Long. Bound to the library's module, the declaration is a compile error, as it is when no module declares the Enum.
        var mod2 = ("Mod2", StandardModule("Mod2", "Option Explicit", "Public Enum Strings", "First = 5", "End Enum"));
        var withoutTheEnum = ("Mod2", StandardModule("Mod2", "Option Explicit"));

        var output = await RunAsync([], [mod2], Program("Dim x As Strings", "x = 5", "Debug.Print x"));
        var control = await LoadErrorsAsync([], [withoutTheEnum], Program("Dim x As Strings"));

        CollectionAssert.AreEqual(new[] { "5" }, output);
        Assert.HasCount(1, control, string.Join("; ", control));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.4 Type Binding Context")]
    public async Task AClassOfTheProject_NamedLikeALibrarysModule_IsTheClass()
    {
        // a class module of the enclosing project is a name of the type binding context's second tier, ahead of a referenced project's procedural module.
        var strings = ("Strings", ClassModule("Strings", "Option Explicit", "Public X As Long"));

        CollectionAssert.AreEqual(new[] { "2" }, await RunAsync([strings], Program("Dim s As Strings", "Set s = New Strings", "s.X = 2", "Debug.Print s.X")));
    }

    [TestMethod]
    public async Task ALibrarysModule_StillBinds_WhenTheProjectHasNothingOfItsName()
        => CollectionAssert.AreEqual(new[] { "4", "4" }, await RunAsync([], Program("Debug.Print Strings.LenB(\"ab\")", "Debug.Print VBA.Strings.LenB(\"ab\")")));

    [TestMethod]
    public async Task ALibrarysModule_QualifiedByTheLibrary_StillBinds_BesideAVariableOfItsName()
    {
        var mod2 = ("Mod2", StandardModule("Mod2", "Option Explicit", "Public Strings As New Widget"));

        CollectionAssert.AreEqual(new[] { "4", "3" }, await RunAsync([Widget], [mod2], Program("Debug.Print VBA.Strings.LenB(\"ab\")", "Strings.MaxRows = 3", "Debug.Print Strings.MaxRows")));
    }

    [TestMethod]
    [DataRow("Strings.Left(\"abc\", 1)", DisplayName = "Qualified by its module")]
    [DataRow("VBA.Strings.Left(\"abc\", 1)", DisplayName = "Qualified by its library and its module")]
    public async Task ALibrarysFunction_QualifiedByItsModule_StillLoads(string call)
        // Option Explicit: a qualifier that bound nothing would not load. Only loading is asserted, since nothing implements Left on this branch yet.
        => CollectionAssert.AreEqual(Array.Empty<string>(), await LoadErrorsAsync([], Program($"Debug.Print {call}")));
}
