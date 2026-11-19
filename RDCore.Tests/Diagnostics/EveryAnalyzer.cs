using RDCore.Diagnostics;
using RDCore.Diagnostics.Analyzers;
using RDCore.Parsing;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// Every analyzer of the core extension, as the extension runs them over a module: what a fix is checked against, so that applying it brings no finding that
/// was not there before.
/// </summary>
internal static class EveryAnalyzer
{
    private static readonly IModuleAnalyzer[] Analyzers = [.. typeof(AnalyzerMessages).Assembly.GetTypes()
        .Where(type => type is { IsAbstract: false, IsClass: true } && typeof(IModuleAnalyzer).IsAssignableFrom(type))
        .Select(Create)
        .OfType<IModuleAnalyzer>()];

    // an analyzer takes nothing, or the settings of the extension, which are their defaults here.
    private static IModuleAnalyzer? Create(Type type)
        => type.GetConstructor(Type.EmptyTypes) is { } none
            ? (IModuleAnalyzer)none.Invoke(null)
            : type.GetConstructor([typeof(Microsoft.Extensions.Options.IOptions<DiagnosticsOptions>)]) is { } withOptions
                ? (IModuleAnalyzer)withOptions.Invoke([Microsoft.Extensions.Options.Options.Create(new DiagnosticsOptions())])
                : null;

    /// <summary>
    /// What every analyzer reports of the module <c>Program</c> of a workspace, each finding as its code and its message: a line the fix adds moves the
    /// others, so where a finding is is not compared.
    /// </summary>
    /// <param name="program">The source of the module.</param>
    /// <param name="others">The other modules of the workspace, by name.</param>
    public static Task<string[]> FindingsAsync(string program, params (string Name, string Source)[] others)
        => FindingsOfAsync("Program", program, others);

    /// <summary>
    /// What every analyzer reports of one module of a workspace: the standard module <c>Program</c>, or one of its class modules.
    /// </summary>
    /// <param name="name">The name of the module analyzed.</param>
    /// <param name="program">The source of the <c>Program</c> standard module.</param>
    /// <param name="classes">The class modules of the workspace, by name.</param>
    public static async Task<string[]> FindingsOfAsync(string name, string program, params (string Name, string Source)[] classes)
    {
        var payload = await ModuleWorkspace.SemanticsAsync(classes, program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == name);
        var uri = new Uri(Path.Combine(ModuleWorkspace.Root, name == "Program" ? "Program.bas" : $"{name}.cls"));
        var parsed = new ModuleParser().Parse(uri, name == "Program" ? program : classes.Single(module => module.Name == name).Source);
        return [.. Analyzers.SelectMany(analyzer => analyzer.Analyze(new ModuleAnalysisContext(uri, parsed, model))).Select(finding => $"{finding.Code}: {finding.Message}").Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// The source once the edits of a fix are made, the last first so that the positions of the others hold.
    /// </summary>
    /// <param name="source">The source the fix is offered for, its lines ending with <c>\r\n</c>.</param>
    /// <param name="fix">The fix.</param>
    public static string Applied(string source, EditFix fix)
    {
        var starts = new List<int> { 0 };
        for (var index = source.IndexOf("\r\n", StringComparison.Ordinal); index >= 0; index = source.IndexOf("\r\n", index + 2, StringComparison.Ordinal))
        {
            starts.Add(index + 2);
        }

        int Offset(OmniSharp.Extensions.LanguageServer.Protocol.Models.Position position) => starts[position.Line] + position.Character;

        foreach (var edit in fix.Edits.OrderByDescending(edit => Offset(edit.Range.Start)))
        {
            source = source[..Offset(edit.Range.Start)] + edit.NewText + source[Offset(edit.Range.End)..];
        }

        return source;
    }

    /// <summary>
    /// That a fix brings nothing the language server reports: the program it is applied to compiles, the fixed program compiles too, and no analyzer reports
    /// of it what it did not report before.
    /// </summary>
    /// <param name="before">The source of the module <c>Program</c> the fix is applied to.</param>
    /// <param name="after">The source of the module once the fix is applied.</param>
    /// <param name="others">The other modules of the workspace, by name.</param>
    public static async Task AssertBringsNothingNewAsync(string before, string after, params (string Name, string Source)[] others)
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync(others, before);
        Assert.IsEmpty(errors, $"the program compiles before the fix: {string.Join("; ", errors)}");
        errors = await ModuleWorkspace.LoadErrorsAsync(others, after);
        Assert.IsEmpty(errors, $"the program compiles after the fix: {string.Join("; ", errors)}");
        CollectionAssert.AreEqual(
            Array.Empty<string>(), Added(await FindingsAsync(before, others), await FindingsAsync(after, others)), "no analyzer reports what it did not before");
    }

    /// <summary>
    /// That a fix of a class module brings nothing the language server reports, as <see cref="AssertBringsNothingNewAsync"/> says of the <c>Program</c> module.
    /// </summary>
    /// <param name="name">The name of the class module the fix is applied to.</param>
    /// <param name="before">The source of the class module the fix is applied to.</param>
    /// <param name="after">The source of the class module once the fix is applied.</param>
    /// <param name="program">The source of the <c>Program</c> standard module.</param>
    public static async Task AssertClassBringsNothingNewAsync(string name, string before, string after, string program)
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync([(name, before)], program);
        Assert.IsEmpty(errors, $"the program compiles before the fix: {string.Join("; ", errors)}");
        errors = await ModuleWorkspace.LoadErrorsAsync([(name, after)], program);
        Assert.IsEmpty(errors, $"the program compiles after the fix: {string.Join("; ", errors)}");
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            Added(await FindingsOfAsync(name, program, (name, before)), await FindingsOfAsync(name, program, (name, after))),
            "no analyzer reports what it did not before");
    }

    /// <summary>
    /// The findings of <paramref name="after"/> that <paramref name="before"/> does not have, each as many times as it has more of them.
    /// </summary>
    /// <param name="before">The findings before the fix.</param>
    /// <param name="after">The findings after it.</param>
    public static string[] Added(string[] before, string[] after)
    {
        var left = before.GroupBy(finding => finding).ToDictionary(group => group.Key, group => group.Count());
        var added = new List<string>();
        foreach (var finding in after)
        {
            if (left.TryGetValue(finding, out var count) && count > 0)
            {
                left[finding] = count - 1;
            }
            else
            {
                added.Add(finding);
            }
        }

        return [.. added];
    }
}
