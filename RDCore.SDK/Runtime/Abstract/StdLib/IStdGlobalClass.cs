using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// <strong>MS-VBAL 6.1.3.3 Global Class</strong><br/>
/// ℹ️ These runtime semantics implicate <strong>MSForms</strong>, which is <strong>out of scope</strong> for the <em>language core</em> 
/// and isn't specified directly as an intrinsic part of MS-VBAL.
/// </summary>
/// <remarks>
/// Formalizes the public interface of the <c>Global</c> class.<br/>
/// 👉 This object is a singleton that is referenced by the <c>Global</c> global/static symbol.
/// </remarks>
public interface IStdGlobalClass
{
    #region 6.1.3.3.1 Public Procedures
    /// <summary>
    /// <strong>MS-VBAL 6.1.3.3.1.1 Load</strong> Loads a <em>form</em> or <em>control</em> into memory.<br/>
    /// </summary>
    /// <remarks>
    /// 🎯 A UserForm <em>designer</em> would absolutely be in-scope for a client IDE, <strong>however</strong>:
    /// <list type="bullet">
    /// <item>Using <c>MSForms</c> directly would tie RDCore to MS-VBA, which must be avoided to keep RDCore portable;</item>
    /// <item>The implication is that we will eventually need a managed library that can reinterpret MSForms and render it <strong>without</strong> the MSForms library or MSVBA runtime;</item>
    /// <item>👉 It's probably best for everyone to <em>move on</em> from legacy MSForms.</item>
    /// </list>
    /// </remarks>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    public RuntimeSemanticsEvaluationResult StdGlobalClass_Load(VBObjectValue value);

    /// <summary>
    /// <strong>MS-VBAL 6.1.3.3.1.2 Unload</strong> Unloads a <em>form</em> or <em>control</em> from memory.<br/>
    /// </summary>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    public RuntimeSemanticsEvaluationResult StdGlobalClass_Unload(VBObjectValue value);
    #endregion
}