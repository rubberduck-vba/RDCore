using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A type qualified by the module that declares it is that module's type (<strong>MS-VBAL §5.6.12</strong>), whatever else the project or a library calls by
/// its bare name: a procedural module, another module's type, a library's class. The language server binds the name, and a user-defined type or an Enum of
/// another module travels to the host with that module's name, so the host binds the type the language server bound. Run through
/// <see cref="ModuleWorkspace"/>, so the binding that loads the module is the one that runs it.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.12 Member Access Expressions")]
public sealed class ModuleQualifiedTypeTests
{
    private static string Program(string declarations, params string[] body)
        => $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n{declarations}\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";

    private static (string Name, string Source) Module(string name, params string[] body) => (name, StandardModule(name, ["Option Explicit", .. body]));

    private static readonly (string Name, string Source) Helpers = Module("Helpers", "Public Total As Long");

    [TestMethod]
    [DataRow("", "Dim h As Mod2.Helpers\r\nh.X = 1\r\nDebug.Print h.X", DisplayName = "A local")]
    [DataRow("Private h As Mod2.Helpers", "h.X = 1\r\nDebug.Print h.X", DisplayName = "A variable of the module")]
    [DataRow("Private Type TRecord\r\nh As Mod2.Helpers\r\nEnd Type", "Dim t As TRecord\r\nt.h.X = 1\r\nDebug.Print t.h.X", DisplayName = "A field")]
    [DataRow("Private Sub Show(ByRef h As Mod2.Helpers)\r\nDebug.Print h.X\r\nEnd Sub", "Dim v As Mod2.Helpers\r\nv.X = 1\r\nShow v", DisplayName = "A parameter")]
    [DataRow("Private Function Make() As Mod2.Helpers\r\nMake.X = 1\r\nDebug.Print Make.X\r\nEnd Function", "Dim v As Mod2.Helpers\r\nv = Make()", DisplayName = "A function's result")]
    public async Task ATypeQualifiedByItsModule_WhoseBareNameIsAProceduralModule_IsTheType(string declarations, string body)
    {
        // by its bare name the host bound the module Helpers: no type at all, and the statement that used it stopped as "cannot run yet".
        var mod2 = Module("Mod2", "Public Type Helpers", "X As Long", "End Type");

        CollectionAssert.AreEqual(new[] { "1" }, await RunAsync([], [Helpers, mod2], Program(declarations, body)));
    }

    [TestMethod]
    [DataRow("", "Dim h As Mod2.Helpers\r\nh = 1\r\nDebug.Print h", DisplayName = "A local")]
    [DataRow("Private h As Mod2.Helpers", "h = 1\r\nDebug.Print h", DisplayName = "A variable of the module")]
    [DataRow("Private Type TRecord\r\nh As Mod2.Helpers\r\nEnd Type", "Dim t As TRecord\r\nt.h = 1\r\nDebug.Print t.h", DisplayName = "A field")]
    [DataRow("Private Sub Show(ByVal h As Mod2.Helpers)\r\nDebug.Print h\r\nEnd Sub", "Show 1", DisplayName = "A ByVal parameter")]
    [DataRow("Private Sub Fill(ByRef h As Mod2.Helpers)\r\nh = 1\r\nEnd Sub", "Dim v As Mod2.Helpers\r\nFill v\r\nDebug.Print v", DisplayName = "A ByRef parameter")]
    [DataRow("Private Function Make() As Mod2.Helpers\r\nMake = 1\r\nEnd Function", "Debug.Print Make()", DisplayName = "A function's result")]
    public async Task AnEnumQualifiedByItsModule_WhoseBareNameIsAProceduralModule_IsTheEnum(string declarations, string body)
    {
        // by its bare name the host bound the module Helpers, and assigning the variable raised error 13; qualified by its module, it is the Enum, a Long.
        var mod2 = Module("Mod2", "Public Enum Helpers", "hOne = 1", "End Enum");

        CollectionAssert.AreEqual(new[] { "1" }, await RunAsync([], [Helpers, mod2], Program(declarations, body)));
    }

    [TestMethod]
    public async Task ATypeQualifiedByItsModule_NamedLikeTheEnclosingModule_IsTheType()
    {
        var mod2 = Module("Mod2", "Public Type Program", "X As Long", "End Type");

        CollectionAssert.AreEqual(new[] { "1" }, await RunAsync([], [mod2], Program("", "Dim p As Mod2.Program", "p.X = 1", "Debug.Print p.X")));
    }

    [TestMethod]
    public async Task AnEnumQualifiedByItsModule_NamedLikeTheEnclosingModule_IsTheEnum()
    {
        var mod2 = Module("Mod2", "Public Enum Program", "pOne = 1", "End Enum");

        CollectionAssert.AreEqual(new[] { "1" }, await RunAsync([], [mod2], Program("", "Dim p As Mod2.Program", "p = 1", "Debug.Print p")));
    }

    [TestMethod]
    public async Task ATypeTwoModulesDeclare_QualifiedByEither_IsThatModulesType()
    {
        // by its bare name the host found both: the declaration was "The declared type 'TRec' could not be resolved", and a field of it stopped as
        // "cannot run yet".
        var mod2 = Module("Mod2", "Public Type TRec", "X As Long", "End Type");
        var mod3 = Module("Mod3", "Public Type TRec", "Y As Long", "End Type");

        CollectionAssert.AreEqual(new[] { "3", "4" }, await RunAsync([], [mod2, mod3], Program(
            "Private Type TOut\r\nK As Mod3.TRec\r\nEnd Type",
            "Dim a As Mod2.TRec", "Dim o As TOut", "a.X = 3", "o.K.Y = 4", "Debug.Print a.X", "Debug.Print o.K.Y")));
    }

    [TestMethod]
    public async Task AnEnumTwoModulesDeclare_QualifiedByEither_IsThatModulesEnum()
    {
        var mod2 = Module("Mod2", "Public Enum Colours", "cRed = 1", "End Enum");
        var mod3 = Module("Mod3", "Public Enum Colours", "cBlue = 2", "End Enum");

        CollectionAssert.AreEqual(new[] { "1", "2" }, await RunAsync([], [mod2, mod3], Program(
            "Private Sub Paint(ByVal c As Mod3.Colours)\r\nDebug.Print c\r\nEnd Sub", "Dim c As Mod2.Colours", "c = 1", "Debug.Print c", "Paint 2")));
    }

    [TestMethod]
    public async Task ATypeTwoModulesDeclare_ByItsBareName_StillDoesNotResolve()
    {
        var mod2 = Module("Mod2", "Public Type TRec", "X As Long", "End Type");
        var mod3 = Module("Mod3", "Public Type TRec", "Y As Long", "End Type");

        Assert.HasCount(1, await LoadErrorsAsync([], [mod2, mod3], Program("", "Dim x As TRec", "Debug.Print 1")));
    }

    [TestMethod]
    public async Task ATypeQualifiedByItsModule_NamedLikeALibrarysClass_IsTheType()
    {
        // by its bare name the host bound VBA's Collection: the variable was an object, Nothing, and c.X = 1 raised error 91.
        var mod2 = Module("Mod2", "Public Type Collection", "X As Long", "End Type");

        CollectionAssert.AreEqual(new[] { "1" }, await RunAsync([], [mod2], Program("", "Dim c As Mod2.Collection", "c.X = 1", "Debug.Print c.X")));
    }

    [TestMethod]
    public async Task AnotherModulesType_ByItsBareName_OrQualified_IsThatType()
    {
        var mod2 = Module("Mod2", "Public Type TRec", "X As Long", "End Type");

        CollectionAssert.AreEqual(new[] { "2", "5", "7" }, await RunAsync([], [mod2], Program(
            "Private Type TOut\r\nI As Mod2.TRec\r\nEnd Type",
            "Dim a As TRec", "Dim b As Mod2.TRec", "Dim o As TOut", "a.X = 2", "b.X = 5", "o.I.X = 7", "Debug.Print a.X", "Debug.Print b.X", "Debug.Print o.I.X")));
    }

    [TestMethod]
    public async Task AClassModulesType_QualifiedByTheClass_IsTheType()
    {
        var shapes = ("Shapes", ClassModule("Shapes", "Option Explicit", "Public Type TRec", "X As Long", "End Type", "Public Enum Colours", "cRed = 1", "End Enum"));

        CollectionAssert.AreEqual(new[] { "3", "2" }, await RunAsync([shapes], Program(
            "", "Dim t As Shapes.TRec", "Dim e As Shapes.Colours", "t.X = 3", "e = 2", "Debug.Print t.X", "Debug.Print e")));
    }

    [TestMethod]
    public async Task AModulesOwnPrivateType_QualifiedByTheModule_IsTheType()
    {
        // a type of the module being defined travels by its bare name, which the host binds to the module's own type first.
        var mod2 = Module("Mod2", "Private Type TP", "X As Long", "End Type", "Public Function Six() As Long", "Dim t As Mod2.TP", "t.X = 6", "Six = t.X", "End Function");

        CollectionAssert.AreEqual(new[] { "5", "6" }, await RunAsync([], [mod2], Program(
            "Private Type TPriv\r\nX As Long\r\nEnd Type", "Dim x As Program.TPriv", "x.X = 5", "Debug.Print x.X", "Debug.Print Six()")));
    }
}
