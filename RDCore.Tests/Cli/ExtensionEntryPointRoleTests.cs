using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.CLI.Host;
using RDCore.SDK.Extensibility;
using RDCore.SDK.Extensibility.Annotations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;

namespace RDCore.Tests.Cli;

/// <summary>
/// The environment host says which members an extension calls from outside the project (<see cref="DeclarationRole.ExtensionEntryPoint"/>): those that
/// carry an annotation an extension of the platform declares as an entry point (<see cref="AnnotationDeclaration.IsEntryPoint"/>), as its manifest says.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL 5.0.3 Semantic Analysis")]
public sealed class ExtensionEntryPointRoleTests
{
    /// <summary>
    /// The host's annotations of a platform whose one extension declares <c>'@TestMethod</c> (alias <c>'@Test</c>) as an entry point, and
    /// <c>'@Description</c> as an annotation that is not one.
    /// </summary>
    internal static IHostExtensionAnnotations UnitTesting()
    {
        var extensions = Substitute.For<IExtensionsProvider>();
        extensions.Discover().Returns(_ =>
        [
            new ExtensionInfo
            {
                Name = "RDCore.UnitTesting.exe",
                Title = "RDCore.UnitTesting",
                Version = new Version(1, 0),
                Publisher = "",
                PublisherWebUrl = "",
                Description = "",
                FileHash = "",
                Capabilities = [],
                Annotations =
                [
                    new AnnotationDeclaration { Name = "TestMethod", Aliases = ["Test"], Targets = AnnotationTargets.Member, IsEntryPoint = true },
                    new AnnotationDeclaration { Name = "Description", Targets = AnnotationTargets.Member },
                ],
            },
        ]);
        return new HostExtensionAnnotations(extensions, NullLogger<HostExtensionAnnotations>.Instance);
    }

    private static string Module(string name, params string[] lines) => ModuleWorkspace.StandardModule(name, lines);

    private static readonly string Program = Module("Program", "Public Sub Main()", "End Sub");

    private static async Task<ModuleSemanticsDto> SemanticsOfAsync(
        string module, IReadOnlyList<(string Name, string Source)>? modules = null, IReadOnlyList<(string Name, string Source)>? classes = null)
    {
        var payload = await ModuleWorkspace.SemanticsAsync(classes ?? [], Program, modules: modules, extensionAnnotations: UnitTesting());
        return payload.Modules.Single(semantics => SymbolUri.ModuleNameOf(semantics.Module) == module);
    }

    // each declaration of the module, with its role, in the order the module declares them.
    private static string RolesOf(ModuleSemanticsDto semantics, params string[] names)
        => string.Join(", ", names.Select(name => $"{name}: {semantics.Declarations.Single(declaration => declaration.Name == name).Role}"));

    [TestMethod]
    [DataRow("'@TestMethod", DisplayName = "its name")]
    [DataRow("'@testmethod", DisplayName = "its name, in another case")]
    [DataRow("'@Test", DisplayName = "an alias")]
    [DataRow("'@TestMethod|'@Description \"Adds two numbers\"", DisplayName = "above another annotation")]
    public async Task AMemberAnnotatedAsAnExtensionsEntryPoint_IsOne(string annotations)
    {
        var semantics = await SemanticsOfAsync("Tests",
            [("Tests", Module("Tests", [.. annotations.Split('|'), "Public Sub AddsTwoNumbers()", "End Sub", "Public Sub Helper()", "End Sub"]))]);

        Assert.AreEqual("AddsTwoNumbers: ExtensionEntryPoint, Helper: None", RolesOf(semantics, "AddsTwoNumbers", "Helper"));
    }

    [TestMethod]
    [DataRow("'@Description \"Adds two numbers\"", DisplayName = "an annotation the extension declares, which is not an entry point")]
    [DataRow("'@Unknown", DisplayName = "an annotation no extension declares")]
    public async Task AMemberWithNoEntryPointAnnotation_HasNoRole(string annotation)
    {
        var semantics = await SemanticsOfAsync("Tests", [("Tests", Module("Tests", annotation, "Public Sub AddsTwoNumbers()", "End Sub"))]);

        Assert.AreEqual("AddsTwoNumbers: None", RolesOf(semantics, "AddsTwoNumbers"));
    }

    [TestMethod]
    public async Task AnEntryPointAnnotation_IsNoEntryPoint_WhenNoExtensionOfThePlatformIsKnown()
    {
        var payload = await ModuleWorkspace.SemanticsAsync([], Program,
            modules: [("Tests", Module("Tests", "'@TestMethod", "Public Sub AddsTwoNumbers()", "End Sub"))]);

        Assert.AreEqual("AddsTwoNumbers: None",
            RolesOf(payload.Modules.Single(semantics => SymbolUri.ModuleNameOf(semantics.Module) == "Tests"), "AddsTwoNumbers"));
    }

    // a class's Initialize handler is called by the language, whatever it is annotated with.
    [TestMethod]
    public async Task AnEventHandlerAnnotatedAsAnEntryPoint_KeepsItsRole()
    {
        var semantics = await SemanticsOfAsync("Fixture",
            classes: [("Fixture", ModuleWorkspace.ClassModule("Fixture", "'@TestMethod", "Private Sub Class_Initialize()", "End Sub"))]);

        Assert.AreEqual("Class_Initialize: EventHandler", RolesOf(semantics, "Class_Initialize"));
    }
}
