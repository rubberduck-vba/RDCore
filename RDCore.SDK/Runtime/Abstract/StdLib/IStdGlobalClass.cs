using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// <strong>MS-VBAL 6.1.3.3 Global Class</strong>: the subroutines that load and unload a <em>form</em>.
/// </summary>
/// <remarks>
/// <para>
/// VBA's <c>Global</c> is an application object: its members resolve unqualified, as a standard module's do (<c>Unload Me</c>), which is how it is declared here.
/// </para>
/// <para>
/// What a form is, and what loading and unloading it does, is the form host's: the Microsoft Forms compatibility layer, on Windows, which implements these. A
/// platform with no form host has no form to load, and says so when one is.
/// </para>
/// </remarks>
[StdLibModule("Global")]
public interface IStdGlobalClass
{
    #region 6.1.3.3.1 Public Subroutines
    /// <summary>
    /// <strong>MS-VBAL 6.1.3.3.1.1 Load</strong> Loads a <em>form</em> without showing it.
    /// </summary>
    /// <remarks>
    /// Any reference to a form loads it (a UserForm's <c>Initialize</c> runs then), so the argument has loaded it by the time this is called: what is left is to
    /// say that an object that is no form cannot be loaded (error 361).
    /// </remarks>
    /// <param name="object">The form.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    public RuntimeSemanticsEvaluationResult Load(VBObjectValue @object);

    /// <summary>
    /// <strong>MS-VBAL 6.1.3.3.1.2 Unload</strong> Unloads a <em>form</em>.
    /// </summary>
    /// <remarks>
    /// A UserForm is asked first (<c>QueryClose</c>, with <c>vbFormCode</c>), and stays loaded when it cancels; otherwise its <c>Terminate</c> runs, its window is
    /// destroyed, and a default instance is let go of, so that the next reference to it loads another (Excel-measured, <c>oracle\s41</c>).
    /// </remarks>
    /// <param name="object">The form.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    public RuntimeSemanticsEvaluationResult Unload(VBObjectValue @object);
    #endregion
}
