using System.Reflection;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace RDCore.LanguageServer.Services.Diagnostics;

// OmniSharp 0.19.9 makes both report types' constructors non-public and their Items init-only: they are made by reflection, as its serializer makes them.
public static class WorkspaceDiagnosticReports
{
    private static readonly PropertyInfo ReportItems = typeof(WorkspaceDiagnosticReport).GetProperty(nameof(WorkspaceDiagnosticReport.Items))!;
    private static readonly PropertyInfo PartialItems = typeof(WorkspaceDiagnosticReportPartialResult).GetProperty(nameof(WorkspaceDiagnosticReportPartialResult.Items))!;

    public static WorkspaceDiagnosticReport Of(IEnumerable<WorkspaceDocumentDiagnosticReport> reports)
        => With<WorkspaceDiagnosticReport>(ReportItems, reports);

    public static WorkspaceDiagnosticReportPartialResult PartialOf(IEnumerable<WorkspaceDocumentDiagnosticReport> reports)
        => With<WorkspaceDiagnosticReportPartialResult>(PartialItems, reports);

    // the progress manager writes a partial result past the serializer that repairs the report converters, and the stub converter throws: it is sent
    // pre-serialized.
    public static Newtonsoft.Json.Linq.JToken ToJson(WorkspaceDiagnosticReportPartialResult partial, OmniSharp.Extensions.JsonRpc.ISerializer serializer)
        => Newtonsoft.Json.Linq.JToken.Parse(serializer.SerializeObject(partial));

    private static T With<T>(PropertyInfo items, IEnumerable<WorkspaceDocumentDiagnosticReport> reports) where T : class
    {
        var made = (T)Activator.CreateInstance(typeof(T), nonPublic: true)!;
        items.SetValue(made, new Container<WorkspaceDocumentDiagnosticReport>(reports));
        return made;
    }
}
