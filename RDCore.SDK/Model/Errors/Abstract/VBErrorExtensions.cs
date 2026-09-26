using RDCore.SDK.Model.Diagnostics;

namespace RDCore.SDK.Model.Errors.Abstract;

/// <summary>
/// Regroups extensions around <see cref="VBErrorInfo"/> to formalize RDCore <em>language core</em> error diagnostics and the base RDCore <em>extended semantic analytics diagnostics</em> - all are issued in <c>RDCore.Diagnostics</c>.
/// </summary>
public static class VBErrorExtensions
{
    extension(RDCoreDiagnosticId id)
    {
        /// <summary>
        /// Gets a formalized <strong><c>RDC00000</c></strong> diagnostics code for a RDCore <em>core diagnostic</em>.
        /// </summary>
        /// <remarks>
        /// <em>Core diagnostics</em> are all the diagnostics issued by <c>RDCore.Diagnostics</c> analyzers.<br/>
        /// 🧩 Diagnostics from <strong>other extensions must use a different prefix</strong> to ensure uniqueness and traceability.
        /// </remarks>
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
        public string ToDiagnosticCode() => $"VBC{info.ErrorId:00000}";
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
        public string ToDiagnosticCode() => $"VBC{info.ErrorId:00000}";
    }
    // VBR and VBA are members on the errors themselves (IVBRaisableError.ToDiagnosticCode), not
    // extensions: an extension binds to the static type of what it is called on, and a raisable error
    // travels through the interpreter in carriers declared as the interface - where an extension would
    // silently report the wrong family.
}
