using System.Reflection;
using Microsoft.Extensions.Options;
using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
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
        => parameter.ParameterType == typeof(IOptions<DiagnosticsOptions>)
            ? Options.Create(new DiagnosticsOptions())
            : throw new NotSupportedException($"an analyzer takes a {parameter.ParameterType}, which the fixture does not make");

    /// <summary>
    /// What every analyzer finds in the <c>Program</c> standard module whose source is given.
    /// </summary>
    public static async Task<AnalyzerFinding[]> FindingsOfAsync(string program)
    {
        var payload = await ModuleWorkspace.SemanticsAsync([], program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas"));
        var context = new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, program), model);
        return [.. Analyzers.SelectMany(analyzer => analyzer.Analyze(context))];
    }

    /// <summary>
    /// The source with the edits of a fix made, the last first so that the positions of the others hold.
    /// </summary>
    public static string Fixed(string source, EditFix fix)
    {
        var starts = new List<int> { 0 };
        for (var at = source.IndexOf('\n'); at >= 0; at = source.IndexOf('\n', at + 1))
        {
            starts.Add(at + 1);
        }

        int OffsetOf(OmniSharp.Extensions.LanguageServer.Protocol.Models.Position position) => starts[position.Line] + position.Character;

        var text = source;
        foreach (var edit in fix.Edits.OrderByDescending(edit => edit.Range.Start.Line).ThenByDescending(edit => edit.Range.Start.Character))
        {
            text = text[..OffsetOf(edit.Range.Start)] + edit.NewText + text[OffsetOf(edit.Range.End)..];
        }

        return text;
    }

    /// <summary>
    /// The fix of the first finding of the code in the module of <paramref name="before"/>, made: the module is <paramref name="after"/>, and analyzed
    /// again, that finding is gone and none is found that was not found before; it compiles if it did; and if it <paramref name="runs"/>, <c>Main</c> prints
    /// what it printed.
    /// </summary>
    public static async Task AssertFixedAsync(RDCoreDiagnosticId code, string[] before, string[] after, bool runs = false, int finding = 0)
    {
        var program = Module(before);
        var found = await FindingsOfAsync(program);
        var fixedOne = found.Where(each => each.Id == code).ElementAt(finding);
        var fix = fixedOne.Fixes?.Edit;
        Assert.IsNotNull(fix, $"the finding carries a fix: {fixedOne.Message}");

        var fixedProgram = Fixed(program, fix);
        Assert.AreEqual(Module(after), fixedProgram);

        // a finding is new when it is not one of the others found before: the fixed one, found again, is new. A fix may make others go with it.
        var others = found.Where(each => !ReferenceEquals(each, fixedOne)).Select(Said).ToList();
        var appeared = (await FindingsOfAsync(fixedProgram)).Select(Said).Where(each => !others.Remove(each)).ToArray();
        Assert.IsEmpty(appeared, $"the finding is gone, and nothing new is found: {string.Join(" | ", appeared)}");

        if ((await ModuleWorkspace.LoadErrorsAsync([], program)).Length == 0)
        {
            Assert.IsEmpty(await ModuleWorkspace.LoadErrorsAsync([], fixedProgram), "it compiles, as it did");
        }

        if (runs)
        {
            CollectionAssert.AreEqual(await ModuleWorkspace.RunAsync([], program), await ModuleWorkspace.RunAsync([], fixedProgram), "it does what it did");
        }
    }

    /// <summary>
    /// That no finding of the code in the module of the lines carries a fix.
    /// </summary>
    public static async Task AssertNoFixAsync(RDCoreDiagnosticId code, params string[] lines)
    {
        var found = (await FindingsOfAsync(Module(lines))).Where(each => each.Id == code).ToArray();
        Assert.IsNotEmpty(found, "the code is found");
        Assert.IsTrue(found.All(each => each.Fixes?.Edit is null), "and offers no fix");
    }

    private static string Said(AnalyzerFinding finding) => $"{finding.Code}: {finding.Message}";
}
