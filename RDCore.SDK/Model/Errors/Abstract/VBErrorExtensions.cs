using RDCore.SDK.Model.Diagnostics;

namespace RDCore.SDK.Model.Errors.Abstract;

/// <summary>
/// Regroups extensions around <see cref="VBErrorInfo"/> to formalize RDCore <em>language core</em> error diagnostics and the base RDCore <em>extended semantic analytics diagnostics</em> - all are issued in <c>RDCore.Diagnostics</c>.
/// </summary>
public static class VBErrorExtensions
{
    // VBCompileErrorId reserves [9300..] for the formalized MS-VBA compilation errors, which is what
    // separates a semantic compile error from a parser syntax error - they share the VBC family, so the id
    // is the only thing that tells them apart.
    private const int SemanticCompileErrorFloor = 9300;

    extension(VBErrorInfo info)
    {
        /// <summary>
        /// Gets the <em>category</em> of this error, as a reader sees it in the title of a message about it:
        /// which of <strong>RD-VBAL §2.6</strong>'s four families raised it.
        /// </summary>
        /// <remarks>
        /// The title says what kind of thing went wrong - "Run-time error" - and the error's own
        /// <see cref="VBErrorInfo.Description"/> says what it was: "Division by zero". Localized.
        /// <para>
        /// 👉 Switches on the error's runtime type rather than being one member per type, so that it cannot
        /// be reached through a carrier declared as something more general and answer for the wrong family.
        /// </para>
        /// </remarks>
        public string ToDiagnosticTitle() => info switch
        {
            VBRuntimeErrorInfo => Exceptions.ErrorTitle_Runtime,
            VBApplicationErrorInfo => Exceptions.ErrorTitle_Application,
            _ => info.ErrorId >= SemanticCompileErrorFloor ? Exceptions.ErrorTitle_Compile : Exceptions.ErrorTitle_Syntax,
        };
    }

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
