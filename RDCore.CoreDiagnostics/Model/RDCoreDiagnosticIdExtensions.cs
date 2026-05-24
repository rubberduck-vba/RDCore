namespace RDCore.Diagnostics.Model;

internal static class RDCoreDiagnosticIdExtensions
{
    extension(RDCoreDiagnosticId id)
    {
        public string ToDiagnosticCode() => $"RDC{(int)id:00000}";
    }
}
