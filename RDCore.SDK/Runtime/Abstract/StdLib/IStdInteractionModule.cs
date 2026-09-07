using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// <strong>MS-VBAL 6.1.2.8 Interaction Module</strong>
/// </summary>
/// <remarks>
/// Formalizes the public interface of the standard library <c>VBA.Interaction</c> module.<br/>
/// ℹ️ <strong>This interface is currently incomplete.</strong>
/// </remarks>
public interface IStdInteractionModule
{
    #region 6.1.2.8.1 StdInteraction: Public Functions

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.1.1 CallByName</strong>
    /// </summary>
    /// <remarks>
    /// Invokes a <em>method</em> or <em>property</em> by name against the specified <see cref="VBObjectValue"/>.
    /// </remarks>
    /// <param name="objectValue">The object exposing the member to be invoked.</param>
    /// <param name="procName">The name of the member to be invoked.</param>
    /// <param name="callType">The type of member invocation.</param>
    /// <param name="args">Any arguments to be supplied to the member upon invocation.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdInteraction__CallByName(VBObjectValue objectValue, string procName, VBCallType callType, params VBVariantValue[] args);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.1.2 Choose</strong>
    /// </summary>
    /// <remarks>
    /// Invokes a <em>method</em> or <em>property</em> by name against the specified <see cref="VBObjectValue"/>.<br/>
    /// 👉 <strong>Indexing is one-based</strong> regardless of any <c>Option Base</c> directives.<br/>
    /// ⚠️ This function yields a <see cref="VBNullValue"/> if the supplied <c>index</c> is out of bounds.
    /// </remarks>
    /// <param name="index">An <see cref="VBIntegerValue"/> between 1 and the number of supplied choices.</param>
    /// <param name="choice">An array containing the values to choose from.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdInteraction__Choose(VBSingleValue index, params VBVariantValue[] choice);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.1.3 Command</strong>
    /// </summary>
    /// <remarks>
    /// Gets a <see cref="VBVariantValue"/> containing the <em>command-line arguments</em> (if any) that were used to initiate the execution of the currently running <em>workspace application</em>.
    /// </remarks>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdInteraction__VCommand();
    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.1.3 Command$</strong>
    /// </summary>
    /// <remarks>
    /// Gets a <see cref="VBStringValue"/> containing the <em>command-line arguments</em> (if any) that were used to initiate the execution of the currently running <em>workspace application</em>.
    /// </remarks>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdInteraction__SCommand();

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.1.4 CreateObject</strong>
    /// </summary>
    /// <remarks>
    /// Creates and returns an object reference to an <em>externally provided</em> and possibly <em>remote</em> object.
    /// </remarks>
    /// <param name="objectClass">A <see cref="VBStringValue"/> containing the <em>application name and class</em> of the object to create.</param>
    /// <param name="serverName">A <see cref="VBStringValue"/> containing the name of the network server where the object will be created.<br/><strong>Optional</strong>: the <em>local machine</em> is used unless specified otherwise.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdInteraction__CreateObject(VBStringValue objectClass, VBStringValue? serverName = default);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.1.5 DoEvents</strong>
    /// </summary>
    /// <remarks>
    /// Yields execution so the operating system can process externally generated events.<br/>
    /// 👉 This function returns an <see cref="VBIntegerValue"/> with an <em>implementation-defined meaning</em>.
    /// </remarks>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdInteraction__DoEvents();

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.1.6 Environ</strong>
    /// </summary>
    /// <remarks>
    /// Gets a <see cref="VBVariantValue"/> (<see cref="VBStringValue"/>) associated with an implementation-defined <em>environment variable</em>.
    /// </remarks>
    /// <param name="key">A <see cref="VBStringValue"/>, or a data value that is let-coercible to <see cref="VBLongValue"/>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdInteraction__VEnviron(VBVariantValue key);
    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.1.6 Environ$</strong>
    /// </summary>
    /// <remarks>
    /// Gets a <see cref="VBStringValue"/> associated with an implementation-defined <em>environment variable</em>.
    /// </remarks>
    /// <param name="key">A <see cref="VBStringValue"/>, or a data value that is let-coercible to <see cref="VBLongValue"/>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdInteraction__SEnviron(VBVariantValue key);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.1.7 GetAllSettings</strong>
    /// </summary>
    /// <remarks>
    /// Gets a two-dimensional <see cref="VBArrayValue"/> containing <see cref="VBStringValue"/> elements representing 
    /// the configuration setting <em>keys</em> and <em>values</em> of the specified <em>settings store</em> section if it exists under the specified <em>application name</em>; returns <see cref="VBEmptyValue"/> otherwise.
    /// </remarks>
    /// <param name="appName">A <see cref="VBStringValue"/> expression containing the name of the application or project whose key settings are requested.</param>
    /// <param name="section">A <see cref="VBStringValue"/> expression containing the name of the configuration section whose key settings are requested.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdInteraction__GetAllSettings(VBStringValue appName, VBStringValue section);
    /// <summary>
    /// 🧩 <strong>RD-VBAL 6.1.2.8.1.7.1 GetJsonSettings</strong><br/>
    /// </summary>
    /// <remarks>
    /// Gets a two-dimensional <see cref="VBArrayValue"/> containing <see cref="VBStringValue"/> elements representing 
    /// the configuration setting <em>keys</em> and <em>values</em> of the specified <em>managed configuration</em> section if it exists; returns <see cref="VBEmptyValue"/> otherwise.
    /// </remarks>
    /// <param name="key">A <see cref="VBStringValue"/> expression containing the <em>managed configuration path</em> (e.g. <c>"Configuration:ConnectionStrings"</c>) of the configuration section whose key settings are requested.</param>
    /// <param name="config">A <see cref="VBStringValue"/> expression containing the <strong>name of a .json configuration file</strong> associated with the <em>workspace application</em>.<br/><strong>Optional</strong>: The default value of this parameter depends on the current host configuration (normally <c>"appsettings.json"</c>).</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdInteraction__GetJsonSettings(VBStringValue key, VBStringValue? config = default);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.1.8 GetAttr</strong>
    /// </summary>
    /// <remarks>
    /// Gets the <see cref="VBFileAttribute"/> <strong>bitwise flags</strong> representing the <em>attributes</em> of a specified <em>file</em> or <em>directory/folder</em>.
    /// </remarks>
    /// <param name="pathName">A <see cref="VBStringValue"/> expression containing a file name. May specify a <em>mapped drive</em> and/or a <em>directory/folder path</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdInteraction__GetAttr(VBStringValue pathName);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.1.9 GetObject</strong>
    /// </summary>
    /// <remarks>
    /// Gets (<strong>or creates</strong> if none is found) a <see cref="VBObjectValue"/> reference to an <em>externally provided</em> and possibly <em>remote</em> object.<br/>
    /// 👉 This function <strong>cannot</strong> be used to get a reference to an object of a <em>workspace-defined</em> object class.<br/><br/>
    /// 🎯 <strong>TODO</strong>: Clarify the specification of the behavior of an omitted <c>className</c> parameter.
    /// </remarks>
    /// <param name="pathName">A <see cref="VBStringValue"/> expression containing the name of the <em>network server</em> where the object will be created.<br/><strong>Optional</strong>: the <em>local machine</em> is used unless specified otherwise.</param>
    /// <param name="className">A <em>qualified</em> <see cref="VBStringValue"/> expression containing the <em>application name</em> and <em>class</em> of the object to create.<br/><strong>Optional</strong> (⚠️ but raises an error given <see cref="VBMissingValue"/>): returns the singleton instance of (the last created) <em>single-instance object</em> given an <em>empty string</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdInteraction__GetObject(VBVariantValue? pathName = default, VBVariantValue? className = default);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.1.10 GetSetting</strong>
    /// </summary>
    /// <remarks>
    /// Gets a <see cref="VBStringValue"/> representing the <em>key setting value</em> from the specified application's entry in an <em>implementation-defined application registry</em> if it exists; gets the specified <c>defaultValue</c> otherwise.
    /// </remarks>
    /// <param name="appName">A <see cref="VBStringValue"/> expression containing the name of the application or project whose key settings are requested.</param>
    /// <param name="section">A <see cref="VBStringValue"/> expression containing the name of the configuration section whose key settings are requested.</param>
    /// <param name="key">A <see cref="VBStringValue"/> expression containing the <em>key</em> of the requested <em>key setting</em> value.</param>
    /// <param name="defaultValue">A <see cref="VBVariantValue"/> expression containing the value to return if no value is set in the key setting.<br/><strong>Optional</strong>: defaults to <see cref="VBStringValue.ZeroLengthString"/>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdInteraction__GetSetting(VBStringValue appName, VBStringValue section, VBStringValue key, VBVariantValue? defaultValue = default);
    /// <summary>
    /// 🧩 <strong>RD-VBAL 6.1.2.8.1.10.1 GetJsonSetting</strong><br/>
    /// </summary>
    /// <remarks>
    /// Gets a <see cref="VBStringValue"/> representing the value of the specified <em>managed configuration</em> setting if it exists; gets the specified <c>defaultValue</c> otherwise.
    /// </remarks>
    /// <param name="key">A <see cref="VBStringValue"/> expression containing the <em>managed configuration path</em> (e.g. <c>"Configuration:ConnectionStrings:AppDb"</c>) of the configuration section whose key settings are requested.</param>
    /// <param name="config">A <see cref="VBStringValue"/> expression containing the <strong>name of a .json configuration file</strong> associated with the <em>workspace application</em>.<br/><strong>Optional</strong>: The default value of this parameter depends on the current host configuration (normally <c>"appsettings.json"</c>).</param>
    /// <param name="defaultValue">A <see cref="VBVariantValue"/> expression containing the value to return if no value is set in the key setting.<br/><strong>Optional</strong>: defaults to <see cref="VBStringValue.ZeroLengthString"/>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdInteraction__GetJsonSetting(VBStringValue key, VBStringValue? config = default, VBVariantValue? defaultValue = default);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.1.11 IIf</strong>
    /// </summary>
    /// <remarks>
    /// Returns one of two parts, <strong>depending on the <em>evaluation of an expression</em></strong>.<br/><br/>
    /// 💡 The wording of how this function and its parameters are specified does not exclude 🧩<c>VBDeferredValue</c> <em>language core extension</em> values that could...<br/>
    /// <em>fix the confusion around this function</em> while opening the door to something like <em>first-class delegates</em> in (extended) RD-VBA.
    /// </remarks>
    /// <param name="pathName">A <see cref="VBStringValue"/> expression containing a file name. May specify a <em>mapped drive</em> and/or a <em>directory/folder path</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdInteraction__IIf(VBVariantValue expression, VBVariantValue truePart, VBVariantValue falsePart);
    
    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.1.12 InputBox</strong>
    /// </summary>
    /// <remarks>
    /// Displays a <em>prompt</em> in a dialog box, waits for the user to input text or click a button, and returns a <see cref="VBStringValue"/> containing the user-provided input.
    /// </remarks>
    /// <param name="prompt">A <see cref="VBStringValue"/> expression containing the <em>message</em> to be displayed in the dialog box.</param>
    /// <param name="title">A <see cref="VBStringValue"/> expression containing the <em>title</em> of the dialog box.<br/><strong>Optional</strong>: uses the name of the <em>workspace project</em> if omitted.</param>
    /// <param name="defaultValue">A <see cref="VBStringValue"/> expression containing the <em>default (pre-filled) value</em>.<br/><strong>Optional</strong>: defaults to <see cref="VBStringValue.ZeroLengthString"/>.</param>
    /// <param name="xpos">A <see cref="VBLongValue"/> expression containing the <em>horizontal distance</em> (measured in <em>twips</em>) of the left edge of the dialog box from the left edge of the screen.<br/><strong>Optional</strong>: dialog is horizontally centered if omitted.</param>
    /// <param name="ypos">A <see cref="VBLongValue"/> expression containing the <em>vertical distance</em> (measured in <em>twips</em>) of the top edge of the dialog box from the top edge of the screen.<br/><strong>Optional</strong>: dialog is vertically centered if omitted.</param>
    /// <param name="helpFile">ℹ️ Unsupported legacy proprietary Microsoft help system. This parameter is <strong>out of scope</strong> of this implementation.</param>
    /// <param name="helpContext">ℹ️ Unsupported legacy proprietary Microsoft help system. This parameter is <strong>out of scope</strong> of this implementation.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdInteraction__InputBox(VBVariantValue prompt, VBVariantValue? title = default, VBVariantValue? defaultValue = default, VBVariantValue? xpos = default, VBVariantValue? ypos = default, VBVariantValue? helpFile = default, VBVariantValue? helpContext = default);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.1.13 MsgBox</strong>
    /// </summary>
    /// <remarks>
    /// Displays a message in a dialog box, waits for the user to click a button, and returns a <see cref="VBMsgBoxResult"/> value indicating which button the user clicked.
    /// </remarks>
    /// <param name="prompt">A <see cref="VBStringValue"/> expression containing the <em>message</em> to be displayed in the dialog box.</param>
    /// <param name="buttons">A <see cref="VBMsgBoxStyle"/> expression containing the <strong>sum of values</strong> specifying the number and type of buttons to display, the icon style to use, the identity of the default button, and the modality of the message box.<br/><strong>Optional</strong>: defaults to <see cref="VBMsgBoxStyle.VBDefaultButton1"/> (<see cref="VBIntegerValue"/> <c>0</c>).</param>
    /// <param name="title">A <see cref="VBStringValue"/> expression containing the <em>title</em> of the dialog box.<br/><strong>Optional</strong>: uses the name of the <em>workspace project</em> if omitted.</param>
    /// <param name="helpFile">ℹ️ Unsupported legacy proprietary Microsoft help system. This parameter is <strong>out of scope</strong> of this implementation.</param>
    /// <param name="helpContext">ℹ️ Unsupported legacy proprietary Microsoft help system. This parameter is <strong>out of scope</strong> of this implementation.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdInteraction__MsgBox(VBVariantValue prompt, VBMsgBoxStyle buttons = VBMsgBoxStyle.VBDefaultButton1, VBVariantValue? title = default, VBVariantValue? helpFile = default, VBVariantValue? helpContext = default);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.1.14 Partition</strong>
    /// </summary>
    /// <remarks>
    /// Returns a <see cref="VBStringValue"/> indicating where a number occurs within a calculated series of ranges.
    /// </remarks>
    /// <param name="number">A <see cref="VBLongValue"/> expression containing the <em>value</em> to be evaluated against the <em>range</em>.</param>
    /// <param name="start">A <see cref="VBLongValue"/> expression containing the <strong>start</strong> of the overall range of numbers.<br/>👉 The numeric value of this parameter <strong>cannot be less than</strong> <c>0</c> 💥<see cref="VBRuntimeErrorId.InvalidProcedureCallOrArgument"/>.</param>
    /// <param name="stop">A <see cref="VBLongValue"/> expression containing the <strong>end</strong> of the overall range of numbers.<br/>👉 The numeric value of this parameter <strong>cannot be less than or equal to</strong> the <c>start</c> value 💥<see cref="VBRuntimeErrorId.InvalidProcedureCallOrArgument"/>.</param>
    /// <param name="interval">A <see cref="VBLongValue"/> expression containing the <strong>interval</strong> of each range of numbers.<br/>👉 The numeric value of this parameter <strong>cannot be less than</strong> <c>1</c> 💥<see cref="VBRuntimeErrorId.InvalidProcedureCallOrArgument"/>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdInteraction__Partition(VBVariantValue number, VBVariantValue start, VBVariantValue stop, VBVariantValue interval);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.1.15 Shell</strong>
    /// </summary>
    /// <remarks>
    /// <strong>Asynchronously</strong> runs an executable program and returns a <see cref="VBDoubleValue"/> representing the implementation-defined program's <em>task ID</em> (or <em>process ID</em>) if successful, otherwise returns the data value <c>0</c>.<br/>
    /// </remarks>
    /// <param name="path">A <see cref="VBStringValue"/> expression containing the <em>value</em> to be evaluated against the <em>range</em>.</param>
    /// <param name="windowStyle">A <see cref="VBIntegerValue"/> (<see cref="VBAppWinStyle"/>) expression containing a value that sets the <em>style</em> of the window in which the program is to be executed.<br/><strong>Optional</strong>: <see cref="VBAppWinStyle.VBMinimizedFocus"/> unless specified otherwise.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdInteraction__Shell(VBVariantValue path, VBAppWinStyle windowStyle = VBAppWinStyle.VBMinimizedFocus);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.1.16 Switch</strong>
    /// </summary>
    /// <remarks>
    /// Evaluates a list of expressions and returns a <see cref="VBVariantValue"/> <em>or an expression</em> associated with the first expression in the list that evaluates to the <em>data value</em> <c>True</c>.
    /// <list type="bullet">
    /// <item>The expressions are <strong>evaluated left to right</strong>;</item>
    /// <item>The <em>first element</em> (at index <c>n+0</c>) in the <c>n</c><sup>th</sup> pair of values is the <strong>expression to be evaluated</strong>;</item>
    /// <item>The <em>second element</em> (at index <c>n+1</c>) in the <c>n</c><sup>th</sup> pair of values is the associated <strong>value to return</strong> if the first element evaluates to <c>True</c>;</item>
    /// <item>💥<see cref="VBRuntimeErrorId.InvalidProcedureCallOrArgument"/> if the expressions aren't properly paired.</item>
    /// </list>
    /// <br/><br/>
    /// 👉 There seems to be a 🧩<c>VBDeferredValue</c> <em>language core extension</em> opportunity here, too. See: <see cref="StdInteraction__IIf"/>
    /// </remarks>
    /// <param name="varExpr">A <see cref="VBArrayValue"/> containing <see cref="VBVariantValue"/> elements representing expressions to be evaluated.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdInteraction__Switch(params VBVariantValue[] varExpr);
    #endregion

    #region 6.1.2.8.2. Public Subroutines

    /****************************************************************************************************
     * 🎯 The target interface defines this section too.
     *     👉 THANK YOU for taking the time to write XML documentation for anything you add here.
    /****************************************************************************************************/

    #endregion
}
