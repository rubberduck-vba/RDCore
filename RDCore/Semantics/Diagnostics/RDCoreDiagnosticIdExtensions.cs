using RDCore.Parsing;

namespace RDCore.Semantics.Diagnostics;

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
