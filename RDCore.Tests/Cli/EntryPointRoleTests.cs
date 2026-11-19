using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Semantics;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// The environment host says which procedure a run of the project starts with (<see cref="DeclarationRole.EntryPoint"/>, <see cref="ProgramEntryPoint"/>):
/// the one the project's build section names, or else the <c>Sub Main</c> of the one standard module that declares one, which the project's code need not
/// call.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL 5.0.3 Semantic Analysis")]
public sealed class EntryPointRoleTests
{
    private static string Module(string name, params string[] lines) => ModuleWorkspace.StandardModule(name, lines);

    // each declaration the host says is the project's entry point, as its module and its name.
    private static async Task<string> EntryPointsAsync(
        string program, IReadOnlyList<(string Name, string Source)>? modules = null, IReadOnlyList<(string Name, string Source)>? classes = null,
        ProjectBuild? build = null)
    {
        var payload = await ModuleWorkspace.SemanticsAsync(classes ?? [], program, modules: modules, build: build);
        return string.Join(", ", payload.Modules.SelectMany(module => module.Declarations
            .Where(declaration => declaration.Role == DeclarationRole.EntryPoint)
            .Select(declaration => $"{SymbolUri.ModuleNameOf(module.Module)}.{declaration.Name}")));
    }

    [TestMethod]
    [DataRow("Public Sub Main()", "Program.Main", DisplayName = "a Public Sub Main")]
    [DataRow("Sub Main()", "Program.Main", DisplayName = "a Sub Main with no access modifier, which is Public")]
    [DataRow("Public Sub Main(ParamArray Args() As Variant)", "Program.Main", DisplayName = "a Sub Main given its arguments as one ParamArray")]
    [DataRow("Private Sub Main()", "", DisplayName = "a Private Sub Main: the project cannot call it")]
    [DataRow("Public Sub Main(ByVal Count As Long)", "", DisplayName = "a Sub Main that takes an argument")]
    [DataRow("Public Function Main() As Long", "", DisplayName = "a Function Main")]
    public async Task TheSubMainOfTheOneStandardModuleThatDeclaresOne_IsTheEntryPoint(string header, string expected)
    {
        var program = Module("Program", header, header.Contains("Function", StringComparison.Ordinal) ? "End Function" : "End Sub",
            "Public Sub Helper()", "End Sub");

        Assert.AreEqual(expected, await EntryPointsAsync(program));
    }

    [TestMethod]
    public async Task ASubMainOfAnotherStandardModule_IsTheEntryPoint()
    {
        var entryPoints = await EntryPointsAsync(Module("Program", "Public Sub Helper()", "End Sub"),
            [("Startup", Module("Startup", "Sub Main()", "End Sub"))]);

        Assert.AreEqual("Startup.Main", entryPoints);
    }

    [TestMethod]
    public async Task ASubMainOfAClass_IsNoEntryPoint()
    {
        var entryPoints = await EntryPointsAsync(Module("Program", "Public Sub Helper()", "End Sub"),
            classes: [("Runner", ModuleWorkspace.ClassModule("Runner", "Public Sub Main()", "End Sub"))]);

        Assert.AreEqual("", entryPoints);
    }

    [TestMethod]
    public async Task TwoStandardModulesThatDeclareASubMain_HaveNoEntryPoint()
    {
        var entryPoints = await EntryPointsAsync(Module("Program", "Public Sub Main()", "End Sub"), [("Startup", Module("Startup", "Sub Main()", "End Sub"))]);

        Assert.AreEqual("", entryPoints);
    }

    [TestMethod]
    [DataRow("Program.Start", "Program.Start", DisplayName = "a procedure of a module, by the module's name")]
    [DataRow("Begin", "Startup.Begin", DisplayName = "a procedure of whichever standard module declares it")]
    [DataRow("Missing", "", DisplayName = "a procedure no module declares: none, and Sub Main is not it")]
    public async Task TheProcedureTheBuildSectionNames_IsTheEntryPoint(string named, string expected)
    {
        var entryPoints = await EntryPointsAsync(Module("Program", "Public Sub Main()", "End Sub", "Public Sub Start()", "End Sub"),
            [("Startup", Module("Startup", "Public Sub Begin()", "End Sub"))], build: new ProjectBuild { EntryPoint = named });

        Assert.AreEqual(expected, entryPoints);
    }

    [TestMethod]
    public async Task ALibrary_HasNoEntryPoint()
    {
        var entryPoints = await EntryPointsAsync(Module("Program", "Public Sub Main()", "End Sub"),
            build: new ProjectBuild { OutputType = ProjectOutputType.Library });

        Assert.AreEqual("", entryPoints);
    }
}
