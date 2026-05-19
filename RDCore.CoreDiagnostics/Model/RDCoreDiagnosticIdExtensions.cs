using RDCore.SDK.Model.Errors;

namespace RDCore.Diagnostics.Model;

internal static class RDCoreDiagnosticIdExtensions
{
    extension(RDCoreDiagnosticId id)
    {
        public string ToDiagnosticCode() => $"RDC{(int)id:00000}";
    }

    extension(VBCompileErrorId id)
    {
        public string ToDiagnosticCode() => $"VBC{(int)id:00000}";
    }

    extension(int vbErrorNumber)
    {
        public string ToDiagnosticCode() => $"VBR{vbErrorNumber:00000}";
    }
}
