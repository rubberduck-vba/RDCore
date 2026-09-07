using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// <strong>MS-VBAL 6.1.3.2 Err Class</strong>
/// </summary>
/// <remarks>
/// Formalizes the public interface of the <c>Err</c> class.<br/>
/// 👉 This object is a singleton that is referenced by the <c>Err</c> global/static symbol.
/// </remarks>
public interface IStdErrClass
{
    #region 6.1.3.2.1 Public Procedures
    /// <summary>
    /// Resets all properties of the <em>error object</em> to their default values.
    /// </summary>
    /// <remarks>
    /// This method is invoked by the following statement semantics:
    /// <list type="bullet">
    /// <item><strong>MS-VBAL 5.4.4.2</strong> <c>Resume</c> statement</item>
    /// <item><strong>MS-VBAL 5.4.2.17</strong> <c>Exit Sub</c> statement</item>
    /// <item><strong>MS-VBAL 5.4.2.18</strong> <c>Exit Function</c> statement</item>
    /// <item><strong>MS-VBAL 5.4.2.19</strong> <c>Exit Property</c> statement</item>
    /// <item><strong>MS-VBAL 5.4.4.1</strong> <c>On Error</c> statement</item>
    /// </list>
    /// </remarks>
    public RuntimeSemanticsEvaluationResult StdErrClass__Clear();

    /// <summary>
    /// Generates a run-time error.
    /// </summary>
    /// <param name="number">A <see cref="VBLongValue"/> that encodes the nature of the error.</param>
    /// <param name="source">A <see cref="VBStringValue"/> expression naming the object or application that generated the error. When setting this property for a class, use the form "project.class".<br/>
    /// <strong>Optional</strong>: uses the current <em>workspace application name</em> unless specified otherwise.</param>
    /// <param name="description"></param>
    /// <param name="helpFile">ℹ️ Unsupported legacy proprietary Microsoft help system. This parameter is <strong>out of scope</strong> of this implementation.</param>
    /// <param name="helpContext">ℹ️ Unsupported legacy proprietary Microsoft help system. This parameter is <strong>out of scope</strong> of this implementation.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    public RuntimeSemanticsEvaluationResult StdErrClass__Raise(VBLongValue number, VBVariantValue source, VBVariantValue description, VBVariantValue? helpFile = default, VBVariantValue? helpContext = default);
    #endregion

    #region 6.1.3.2.2 Public Properties
    /// <summary>
    /// <strong>MS-VBAL 6.1.3.2.1 Description</strong> Gets a <see cref="VBStringValue"/> containing a <em>description</em> of the error.
    /// </summary>
    public RuntimeSemanticsEvaluationResult StdErrClass_getDescription();
    /// <summary>
    /// <strong>MS-VBAL 6.1.3.2.1 Description</strong> Sets the <em>description</em> of the error.
    /// </summary>
    /// <param name="value">A <see cref="VBStringValue"/> containing a description of the error.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    public RuntimeSemanticsEvaluationResult StdErrClass_setDescription(VBStringValue value);

    /// <summary>
    /// <strong>MS-VBAL 6.1.3.2.2 HelpContext</strong> Gets a <see cref="VBStringValue"/> containing the <em>HelpContextID</em> of the error.
    /// </summary>
    /// <remarks>
    /// ℹ️ Unsupported legacy proprietary Microsoft help system. This property is <strong>out of scope</strong> of this implementation.
    /// </remarks>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    public RuntimeSemanticsEvaluationResult StdErrClass_getHelpContext();
    /// <summary>
    /// <strong>MS-VBAL 6.1.3.2.2 HelpContext</strong> Sets the <em>HelpContextID</em> of the error.
    /// </summary>
    /// <param name="value">A <see cref="VBStringValue"/> containing the <em>help context ID</em> value.</param>
    /// <remarks>
    /// ℹ️ Unsupported legacy proprietary Microsoft help system. This property is <strong>out of scope</strong> of this implementation.
    /// </remarks>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    public RuntimeSemanticsEvaluationResult StdErrClass_setHelpContext(VBStringValue value);

    /// <summary>
    /// <strong>MS-VBAL 6.1.3.2.3 HelpFile</strong> Gets a <see cref="VBStringValue"/> containing the <em>HelpFile</em> of the error.
    /// </summary>
    /// <remarks>
    /// ℹ️ Unsupported legacy proprietary Microsoft help system. This property is <strong>out of scope</strong> of this implementation.
    /// </remarks>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    public RuntimeSemanticsEvaluationResult StdErrClass_getHelpFile();
    /// <summary>
    /// <strong>MS-VBAL 6.1.3.2.3 HelpFile</strong> Sets the <em>HelpFile</em> of the error.
    /// </summary>
    /// <param name="value">A <see cref="VBStringValue"/> containing the <em>help file</em> value.</param>
    /// <remarks>
    /// ℹ️ Unsupported legacy proprietary Microsoft help system. This property is <strong>out of scope</strong> of this implementation.
    /// </remarks>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    public RuntimeSemanticsEvaluationResult StdErrClass_setHelpFile(VBStringValue value);

    /// <summary>
    /// <strong>MS-VBAL 6.1.3.2.4 LastDllError</strong> Gets a <see cref="VBLongValue"/> containing a <em>system error code</em> produced by a call to a <em>dynamic-link library</em> (DLL).
    /// </summary>
    /// <remarks>
    /// 👉 Applies only to DLL calls made from VBA code. No error is raised when the <c>LastDllError</c> property is set (internally: this property is read-only).
    /// </remarks>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    public RuntimeSemanticsEvaluationResult StdErrClass_getLastDllError();

    /// <summary>
    /// <strong>MS-VBAL 6.1.3.2.2.5 Number</strong> Gets a <see cref="VBLongValue"/> containing an error code.
    /// </summary>
    /// <remarks>
    /// 👉 <strong>Default member</strong>: this member is invoked by <see cref="VBObjectType"/> <em>implicit let-coercion</em> semantics.
    /// </remarks>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    public RuntimeSemanticsEvaluationResult StdErrClass_getNumber();
    /// <summary>
    /// <strong>MS-VBAL 6.1.3.2.2.5 Number</strong> Sets a <see cref="VBLongValue"/> containing an error code.
    /// </summary>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    public RuntimeSemanticsEvaluationResult StdErrClass_setNumber(VBLongValue value);
    #endregion
}
