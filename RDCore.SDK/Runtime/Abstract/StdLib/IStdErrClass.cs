using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// <strong>MS-VBAL 6.1.3.2 Err Class</strong>
/// </summary>
/// <remarks>
/// Formalizes the public interface of the <c>ErrObject</c> class, whose sole instance is the
/// <em>error object</em>: its properties and methods reflect and control the error state of the active
/// VBA environment, and so are backed by the session's own error state rather than by per-instance
/// storage.
/// <para>
/// 👉 The class is named <c>ErrObject</c>, and its instance is reached through the <c>Err</c> function of
/// the <c>Information</c> module (<see cref="IStdInformationModule.Err"/>). MS-VBAL describes the same
/// arrangement as "a global class module with a default instance variable" named <c>Err</c>, which is
/// what a bare <c>Err</c> resolving to the error object looks like from source; a zero-argument function
/// of a promoted standard module produces exactly that, and leaves <c>ErrObject</c> nameable in an
/// <c>As</c> clause the way MS-VBA has it. Not creatable: <c>New ErrObject</c> names nothing.
/// </para>
/// </remarks>
[StdLibClass("ErrObject", IsCreatable = false)]
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
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult Clear();

    /// <summary>
    /// Generates a run-time error.
    /// </summary>
    /// <remarks>
    /// An argument that is not specified takes the error object's current property setting, unless that
    /// has been cleared — so <c>Err.Raise</c> given only a <c>Number</c> re-raises with whatever
    /// <c>Source</c> and <c>Description</c> are already set.
    /// </remarks>
    /// <param name="number">A <see cref="VBLongValue"/> that encodes the nature of the error.</param>
    /// <param name="source">A <see cref="VBStringValue"/> expression naming the object or application that generated the error. When setting this property for a class, use the form "project.class".<br/>
    /// <strong>Optional</strong>: uses the current <em>workspace application name</em> unless specified otherwise.</param>
    /// <param name="description">A <see cref="VBStringValue"/> expression describing the error.<br/>
    /// <strong>Optional</strong>: unspecified, the description the <c>Error</c> function would return for <paramref name="number"/> is used, or "Application-defined or object-defined error" when it names no VBA run-time error.</param>
    /// <param name="helpFile">ℹ️ Unsupported legacy proprietary Microsoft help system. This parameter is <strong>out of scope</strong> of this implementation.</param>
    /// <param name="helpContext">ℹ️ Unsupported legacy proprietary Microsoft help system. This parameter is <strong>out of scope</strong> of this implementation.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult Raise(VBLongValue number, VBVariantValue? source = default, VBVariantValue? description = default, VBVariantValue? helpFile = default, VBVariantValue? helpContext = default);
    #endregion

    #region 6.1.3.2.2 Public Properties
    /// <summary>
    /// <strong>MS-VBAL 6.1.3.2.2.1 Description</strong> Gets a <see cref="VBStringValue"/> containing a <em>description</em> of the error.
    /// </summary>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(Kind = StdLibMemberKind.PropertyGet)]
    RuntimeSemanticsEvaluationResult<VBStringValue> Description();

    /// <summary>
    /// <strong>MS-VBAL 6.1.3.2.2.1 Description</strong> Sets the <em>description</em> of the error.
    /// </summary>
    /// <param name="value">A <see cref="VBStringValue"/> containing a description of the error.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(Kind = StdLibMemberKind.PropertyLet)]
    RuntimeSemanticsEvaluationResult Description(VBStringValue value);

    /// <summary>
    /// <strong>MS-VBAL 6.1.3.2.2.2 HelpContext</strong> Gets a <see cref="VBLongValue"/> containing the <em>HelpContextID</em> of the error.
    /// </summary>
    /// <remarks>
    /// ℹ️ Unsupported legacy proprietary Microsoft help system. This property is <strong>out of scope</strong> of this implementation.
    /// </remarks>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(Kind = StdLibMemberKind.PropertyGet)]
    RuntimeSemanticsEvaluationResult<VBLongValue> HelpContext();

    /// <summary>
    /// <strong>MS-VBAL 6.1.3.2.2.2 HelpContext</strong> Sets the <em>HelpContextID</em> of the error.
    /// </summary>
    /// <param name="value">A <see cref="VBLongValue"/> containing the <em>help context ID</em> value.</param>
    /// <remarks>
    /// ℹ️ Unsupported legacy proprietary Microsoft help system. This property is <strong>out of scope</strong> of this implementation.
    /// </remarks>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(Kind = StdLibMemberKind.PropertyLet)]
    RuntimeSemanticsEvaluationResult HelpContext(VBLongValue value);

    /// <summary>
    /// <strong>MS-VBAL 6.1.3.2.2.3 HelpFile</strong> Gets a <see cref="VBStringValue"/> containing the <em>HelpFile</em> of the error.
    /// </summary>
    /// <remarks>
    /// ℹ️ Unsupported legacy proprietary Microsoft help system. This property is <strong>out of scope</strong> of this implementation.
    /// </remarks>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(Kind = StdLibMemberKind.PropertyGet)]
    RuntimeSemanticsEvaluationResult<VBStringValue> HelpFile();

    /// <summary>
    /// <strong>MS-VBAL 6.1.3.2.2.3 HelpFile</strong> Sets the <em>HelpFile</em> of the error.
    /// </summary>
    /// <param name="value">A <see cref="VBStringValue"/> containing the <em>help file</em> value.</param>
    /// <remarks>
    /// ℹ️ Unsupported legacy proprietary Microsoft help system. This property is <strong>out of scope</strong> of this implementation.
    /// </remarks>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(Kind = StdLibMemberKind.PropertyLet)]
    RuntimeSemanticsEvaluationResult HelpFile(VBStringValue value);

    /// <summary>
    /// <strong>MS-VBAL 6.1.3.2.2.4 LastDllError</strong> Gets a <see cref="VBLongValue"/> containing a <em>system error code</em> produced by a call to a <em>dynamic-link library</em> (DLL).
    /// </summary>
    /// <remarks>
    /// 👉 Applies only to DLL calls made from VBA code. No error is raised when the <c>LastDllError</c> property is set (internally: this property is read-only).
    /// </remarks>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(Kind = StdLibMemberKind.PropertyGet)]
    RuntimeSemanticsEvaluationResult<VBLongValue> LastDllError();

    /// <summary>
    /// <strong>MS-VBAL 6.1.3.2.2.5 Number</strong> Gets a <see cref="VBLongValue"/> containing an error code.
    /// </summary>
    /// <remarks>
    /// 👉 <strong>Default member</strong>: this member is invoked by <see cref="VBObjectType"/> <em>implicit let-coercion</em> semantics.
    /// </remarks>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(Kind = StdLibMemberKind.PropertyGet)]
    RuntimeSemanticsEvaluationResult<VBLongValue> Number();

    /// <summary>
    /// <strong>MS-VBAL 6.1.3.2.2.5 Number</strong> Sets a <see cref="VBLongValue"/> containing an error code.
    /// </summary>
    /// <param name="value">A <see cref="VBLongValue"/> containing the error code.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(Kind = StdLibMemberKind.PropertyLet)]
    RuntimeSemanticsEvaluationResult Number(VBLongValue value);

    /// <summary>
    /// <strong>MS-VBAL 6.1.3.2.2.6 Source</strong> Gets a <see cref="VBStringValue"/> naming the object or application that originally generated the error.
    /// </summary>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(Kind = StdLibMemberKind.PropertyGet)]
    RuntimeSemanticsEvaluationResult<VBStringValue> Source();

    /// <summary>
    /// <strong>MS-VBAL 6.1.3.2.2.6 Source</strong> Sets the name of the object or application that originally generated the error.
    /// </summary>
    /// <param name="value">A <see cref="VBStringValue"/> naming the object or application.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(Kind = StdLibMemberKind.PropertyLet)]
    RuntimeSemanticsEvaluationResult Source(VBStringValue value);
    #endregion
}
