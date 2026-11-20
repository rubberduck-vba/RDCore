using System.Reflection;
using Microsoft.Extensions.Options;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.ProtocolExtensions;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// What a code action of a diagnostic is held to: made, the module is what the action says it makes of it; analyzed again by every analyzer of the core
/// extension, the finding it fixes is gone and nothing new is found; the module compiles if it did; and a program that runs prints what it printed.
/// </summary>
internal static class CodeActionFixture
{
    /// <summary>
    /// The <c>Program</c> standard module of the given lines.
    /// </summary>
    public static string Module(params string[] lines) => $"Attribute VB_Name = \"Program\"\r\n{string.Join("\r\n", lines)}\r\n";

    // every analyzer of the core extension, with its default settings: what a user who changed none of them is told.
    private static readonly IModuleAnalyzer[] Analyzers = [.. typeof(OptionExplicitAnalyzer).Assembly.GetTypes()
        .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IModuleAnalyzer).IsAssignableFrom(type))
        .Select(type => (IModuleAnalyzer)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            [.. type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single().GetParameters().Select(ArgumentOf)], null)!)];

    private static object ArgumentOf(ParameterInfo parameter)
        => parameter.ParameterType == typeof(IOptions<DiagnosticsOptions>) ? Options.Create(new DiagnosticsOptions())
            : parameter.ParameterType == typeof(IOptions<SdkAppOptions>) ? Options.Create(new SdkAppOptions())
            : throw new NotSupportedException($"an analyzer takes a {parameter.ParameterType}, which the fixture does not make");

    /// <summary>
    /// What every analyzer finds in the <c>Program</c> standard module whose source is given, in a workspace with the given class modules and referenced
    /// libraries (none when omitted).
    /// </summary>
    public static async Task<AnalyzerFinding[]> FindingsOfAsync(
        string program, IReadOnlyList<(string Name, string Source)>? classes = null, ModuleWorkspace.WorkspaceLibraries? libraries = null)
    {
        var payload = await ModuleWorkspace.SemanticsAsync(classes ?? [], program, libraries: libraries);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        var context = new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, program), model);
        return [.. Analyzers.SelectMany(analyzer => analyzer.Analyze(context))];
    }

    /// <summary>
    /// The fix of the first finding of the code in the module of <paramref name="before"/>, made: the module is <paramref name="after"/>, and analyzed
    /// again, that finding is gone and none is found that was not found before; it compiles if it did; and if it <paramref name="runs"/>, <c>Main</c> prints
    /// what it printed. A finding that carries more than one fix is fixed by the one at <paramref name="choice"/>; one that carries one fix, by it.
    /// The workspace has the given class modules and referenced libraries, none when omitted.
    /// </summary>
    public static async Task AssertFixedAsync(
        RDCoreDiagnosticId code, string[] before, string[] after, bool runs = false, int finding = 0, int? choice = null,
        IReadOnlyList<(string Name, string Source)>? classes = null, ModuleWorkspace.WorkspaceLibraries? libraries = null)
    {
        var program = Module(before);
        var found = await FindingsOfAsync(program, classes, libraries);
        var fixedOne = found.Where(each => each.Id == code).ElementAt(finding);
        var fix = choice is { } index ? fixedOne.Fixes?.Edits.ElementAtOrDefault(index) : fixedOne.Fixes?.Edits.SingleOrDefault();
        Assert.IsNotNull(fix, $"the finding carries a fix: {fixedOne.Message}");

        var fixedProgram = fix.Edits.ApplyTo(program);
        Assert.AreEqual(Module(after), fixedProgram);

        // a finding is new when it is not one of the others found before: the fixed one, found again, is new. A fix may make others go with it.
        var others = found.Where(each => !ReferenceEquals(each, fixedOne)).Select(Said).ToList();
        var appeared = (await FindingsOfAsync(fixedProgram, classes, libraries)).Select(Said).Where(each => !others.Remove(each)).ToArray();
        Assert.IsEmpty(appeared, $"the finding is gone, and nothing new is found: {string.Join(" | ", appeared)}");

        if ((await LoadErrorsAsync(program)).Length == 0)
        {
            Assert.IsEmpty(await LoadErrorsAsync(fixedProgram), "it compiles, as it did");
        }

        Task<string[]> LoadErrorsAsync(string module) => libraries is null
            ? ModuleWorkspace.LoadErrorsAsync(classes ?? [], module)
            : ModuleWorkspace.LoadErrorsAsync(classes ?? [], module, libraries);

        if (runs)
        {
            CollectionAssert.AreEqual(
                await ModuleWorkspace.RunAsync(classes ?? [], program), await ModuleWorkspace.RunAsync(classes ?? [], fixedProgram), "it does what it did");
        }
    }

    /// <summary>
    /// That no finding of the code in the module of the lines carries a fix.
    /// </summary>
    public static Task AssertNoFixAsync(RDCoreDiagnosticId code, params string[] lines) => AssertNoFixAsync(code, [], lines);

    /// <summary>
    /// That no finding of the code in the module of the lines, in a workspace with the given class modules, carries a fix.
    /// </summary>
    public static async Task AssertNoFixAsync(RDCoreDiagnosticId code, IReadOnlyList<(string Name, string Source)> classes, params string[] lines)
    {
        var found = (await FindingsOfAsync(Module(lines), classes)).Where(each => each.Id == code).ToArray();
        Assert.IsNotEmpty(found, "the code is found");
        Assert.IsTrue(found.All(each => each.Fixes?.Edits.SingleOrDefault()is null), "and offers no fix");
    }

    private static string Said(AnalyzerFinding finding) => $"{finding.Code}: {finding.Message}";
}
