using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Workspace;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC01024</c>: a project named <c>VBAProject</c>, the name MS-VBA gives a new project (<see cref="RDCoreProject.DefaultName"/>).
/// </summary>
/// <remarks>
/// The finding is of the project's <c>.rdproj</c>, at its name, or at the start of the file when the file names no project. Names are compared without regard
/// to case.
/// </remarks>
internal sealed class DefaultProjectNameAnalyzer : IProjectAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.DefaultProjectName.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ProjectAnalysisContext context)
    {
        if (!string.Equals(context.Project.Name, RDCoreProject.DefaultName, StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        yield return new AnalyzerFinding(
            RDCoreDiagnosticId.DefaultProjectName.ToCode(),
            context.NameRange ?? SourceRange.Empty,
            DiagnosticSeverity.Information,
            AnalyzerMessages.Format(RDCoreDiagnosticsResources.DefaultProjectName_Message, context.Project.Name));
    }
}
