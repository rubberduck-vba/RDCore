using RDCore.Diagnostics.Model;

namespace RDCore.SDK.Model.Errors
{
    public static class VBErrorExtensions
    {
        extension(RDCoreDiagnosticId id)
        {
            public string ToDiagnosticCode() => $"RDC{(int)id:00000}";
        }

        extension(VBSyntaxErrorInfo info)
        {
            /// <summary>
            /// Gets a formalized <strong><c>VBC00000</c></strong> diagnostics code for a <em>compile-time exception</em>.
            /// </summary>
            /// <param name="id">The <c>VBCompileErrorId</c> value to codify.</param>
            /// <remarks>
            /// A <c>VBCompileErrorException</c> would be thrown in the <em>static semantics</em> layer by the language core.
            /// </remarks>
            public string ToDiagnosticCode() => $"VBC{(int)info.VBCompileErrorId:00000}";
        }

        extension(VBCompileErrorInfo info)
        {
            /// <summary>
            /// Gets a formalized <strong><c>VBC00000</c></strong> diagnostics code for a <em>compile-time exception</em>.
            /// </summary>
            /// <param name="id">The <c>VBCompileErrorId</c> value to codify.</param>
            /// <remarks>
            /// A <c>VBCompileErrorException</c> would be thrown in the <em>static semantics</em> layer by the language core.
            /// </remarks>
            public string ToDiagnosticCode() => $"VBC{(int)info.VBCompileErrorId:00000}";
        }

        extension(VBRuntimeErrorInfo info)
        {
            /// <summary>
            /// Gets a formalized <strong><c>VBR00000</c></strong> diagnostics code for a <em>run-time exception</em>.
            /// </summary>
            /// <param name="id">The <c>VBRuntimeErrorException</c> to codify.</param>
            /// <remarks>
            /// A <c>VBRuntimeErrorException</c> would be thrown in the <em>runtime semantics</em> layer by the language core,
            /// and unhandled in user code.
            /// </remarks>
            public string ToDiagnosticCode() => $"VBR{(int)info.VBRuntimeErrorId:00000}";
        }

        extension(VBApplicationErrorInfo info)
        {
            /// <summary>
            /// Gets a formalized <strong><c>VBA00000</c></strong> pseudo-diagnostics code for an <em>application run-time exception</em>.
            /// </summary>
            /// <param name="id">The <c>VBApplicationErrorException</c> to pseudo-codify.</param>
            /// <remarks>
            /// A <c>VBApplicationErrorException</c> is explicitly raised in workspace source code via <c>Error</c> or <c>Err.Raise</c> statements.
            /// </remarks>
            public string ToDiagnosticCode() => $"VBA{info.CustomErrorCode:00000}";
        }
    }
}
