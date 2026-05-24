namespace RDCore.SDK.Model.Errors;

public static class VBErrorExtensions
{
    extension(VBCompileErrorException exception)
    {
        /// <summary>
        /// Gets a formalized <strong><c>VBC00000</c></strong> diagnostics code for a <em>compile-time exception</em>.
        /// </summary>
        /// <param name="id">The <c>VBCompileErrorId</c> value to codify.</param>
        /// <remarks>
        /// A <c>VBCompileErrorException</c> would be thrown in the <em>static semantics</em> layer by the language core.
        /// </remarks>
        public string ToDiagnosticCode() => $"VBC{(int)exception.VBCompileErrorId:00000}";
    }

    extension(VBRuntimeErrorException exception)
    {
        /// <summary>
        /// Gets a formalized <strong><c>VBR00000</c></strong> diagnostics code for a <em>run-time exception</em>.
        /// </summary>
        /// <param name="id">The <c>VBRuntimeErrorException</c> to codify.</param>
        /// <remarks>
        /// A <c>VBRuntimeErrorException</c> would be thrown in the <em>runtime semantics</em> layer by the language core,
        /// and unhandled in user code.
        /// </remarks>
        public string ToDiagnosticCode() => $"VBR{exception.VBErrorNumber:00000}";
    }
}
