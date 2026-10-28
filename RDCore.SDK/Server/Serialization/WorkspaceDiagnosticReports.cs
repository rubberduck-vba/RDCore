using System.Reflection;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace RDCore.SDK.Server.Serialization;

/// <summary>
/// Makes the answers of a <c>workspace/diagnostic</c> pull (<strong>LSP 3.17</strong> §Workspace Diagnostics), which OmniSharp 0.19.9 gives no way to make.
/// </summary>
/// <remarks>
/// OmniSharp 0.19.9 generates <see cref="WorkspaceDiagnosticReport"/> and <see cref="WorkspaceDiagnosticReportPartialResult"/> with constructors that are all
/// non-public, and an <c>init</c> accessor for their <c>Items</c>: a handler can neither construct one nor set what it holds. Each is made here with its
/// non-public parameterless constructor, its items set as the serializer would. The report's own records - one document's full or unchanged report - are
/// public, and are made as usual; they are written by <see cref="PullDiagnosticsLspSerializer"/>.
/// </remarks>
public static class WorkspaceDiagnosticReports
{
    private static readonly PropertyInfo ReportItems = typeof(WorkspaceDiagnosticReport).GetProperty(nameof(WorkspaceDiagnosticReport.Items))!;
    private static readonly PropertyInfo PartialItems = typeof(WorkspaceDiagnosticReportPartialResult).GetProperty(nameof(WorkspaceDiagnosticReportPartialResult.Items))!;

    /// <summary>
    /// The answer to a <c>workspace/diagnostic</c> pull: a report for each document - none, for a client that was sent them as partial results.
    /// </summary>
    /// <param name="reports">The documents' reports.</param>
    public static WorkspaceDiagnosticReport Of(IEnumerable<WorkspaceDocumentDiagnosticReport> reports)
        => With<WorkspaceDiagnosticReport>(ReportItems, reports);

    /// <summary>
    /// A partial result of a <c>workspace/diagnostic</c> pull, which a client that gave a <c>partialResultToken</c> is sent (<c>$/progress</c>).
    /// </summary>
    /// <param name="reports">The reports of the documents it is of.</param>
    public static WorkspaceDiagnosticReportPartialResult PartialOf(IEnumerable<WorkspaceDocumentDiagnosticReport> reports)
        => With<WorkspaceDiagnosticReportPartialResult>(PartialItems, reports);

    // the repaired serializer (PullDiagnosticsLspSerializer), through the interface that applies the repair.
    private static readonly OmniSharp.Extensions.JsonRpc.ISerializer Serializer = new PullDiagnosticsLspSerializer();

    /// <summary>
    /// A partial result as it is sent (<c>$/progress</c>): written by the repaired serializer.
    /// </summary>
    /// <remarks>
    /// OmniSharp 0.19.9's progress manager writes a partial result with its serializer's inner <c>JsonSerializer</c>, past the interface
    /// <see cref="PullDiagnosticsLspSerializer"/> repairs the report converters through, and the document reports' stub converter throws. A partial result
    /// sent as this JSON goes out as it is.
    /// </remarks>
    /// <param name="partial">The partial result.</param>
    public static Newtonsoft.Json.Linq.JToken ToJson(WorkspaceDiagnosticReportPartialResult partial)
        => Newtonsoft.Json.Linq.JToken.Parse(Serializer.SerializeObject(partial));

    private static T With<T>(PropertyInfo items, IEnumerable<WorkspaceDocumentDiagnosticReport> reports) where T : class
    {
        var made = (T)Activator.CreateInstance(typeof(T), nonPublic: true)!;
        items.SetValue(made, new Container<WorkspaceDocumentDiagnosticReport>(reports));
        return made;
    }
}
