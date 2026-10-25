using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// <strong>MS-VBAL 6.1.2.8 Interaction Module</strong>
/// </summary>
/// <remarks>
/// Formalizes the public interface of the standard library <c>VBA.Interaction</c> module.
/// </remarks>
[StdLibModule]
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
    RuntimeSemanticsEvaluationResult<VBVariantValue> CallByName(VBObjectValue objectValue, VBStringValue procName, VBCallType callType, params VBVariantValue[] args);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.1.2 Choose</strong>
    /// </summary>
    /// <remarks>
    /// Returns a value from a list of choices, selected by a one-based index.<br/>
    /// 👉 <strong>Indexing is one-based</strong> regardless of any <c>Option Base</c> directives. An index that is not a whole number is rounded to the nearest one,
    /// the way a <c>Single</c> is Let-coerced to a <c>Long</c> (banker's rounding).<br/>
    /// ⚠️ This function yields a <see cref="VBNullValue"/> if the supplied <c>index</c> is out of bounds.
    /// </remarks>
    /// <param name="index">An <see cref="VBIntegerValue"/> between 1 and the number of supplied choices.</param>
    /// <param name="choice">An array containing the values to choose from.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> Choose(VBSingleValue index, params VBVariantValue[] choice);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.1.3 Command</strong>
    /// </summary>
    /// <remarks>
    /// Gets a <see cref="VBVariantValue"/> containing the <em>command-line arguments</em> (if any) that were used to initiate the execution of the currently running <em>workspace application</em>.
    /// <para>
    /// What those are is the host's to say ("implementation dependent"): none - an empty <c>String</c> - unless it says otherwise, as in a program an Office
    /// application runs (recorded in Excel). The <see cref="VBVariantValue"/> holds a <c>String</c>.
    /// </para>
    /// </remarks>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> Command();
    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.1.3 Command$</strong>
    /// </summary>
    /// <remarks>
    /// Gets a <see cref="VBStringValue"/> containing the <em>command-line arguments</em> (if any) that were used to initiate the execution of the currently running <em>workspace application</em>.
    /// </remarks>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember("Command$")]
    RuntimeSemanticsEvaluationResult<VBStringValue> CommandString();

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.1.4 CreateObject</strong> Creates an object of the class a programmatic identifier names (<c>Scripting.Dictionary</c>), on this
    /// machine or on the server machine named.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The name is looked for, whatever its case, among the classes the platform implements itself first (<see cref="StdLibClassAttribute.ProgId"/>), which
    /// a program creates by name on any machine; then among the machine's automation servers - COM, on Windows - through the session's external calls, which
    /// an administrator's policy sees. Error <c>429</c> <em>ActiveX component can't create object</em> when neither has the class.
    /// </para>
    /// <para>
    /// A server's object is bound to late, as <strong>MS-VBA</strong> binds it: its members are reached by name, whatever a type library says of them - error
    /// <c>438</c> for a name it does not answer to. It is an instance of the class of a referenced library when one declares its class.
    /// </para>
    /// <para>
    /// As in <strong>MS-VBA</strong> (recorded in Excel): a server machine that is not there is the <c>HRESULT</c> of the failure (<c>-2147023174</c>), not
    /// error <c>462</c>.
    /// </para>
    /// </remarks>
    /// <param name="objectClass">A <see cref="VBStringValue"/> containing the <em>application name and class</em> of the object to create.</param>
    /// <param name="serverName">A <see cref="VBStringValue"/> containing the name of the network server where the object will be created.<br/><strong>Optional</strong>: the <em>local machine</em> is used unless specified otherwise.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBObjectValue> CreateObject(VBStringValue objectClass, VBStringValue? serverName = default);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.1.5 DoEvents</strong>
    /// </summary>
    /// <remarks>
    /// Yields execution so the operating system can process externally generated events.<br/>
    /// 👉 This function returns an <see cref="VBIntegerValue"/> with an <em>implementation-defined meaning</em>.
    /// </remarks>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBIntegerValue> DoEvents();

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.1.6 Environ</strong>
    /// </summary>
    /// <remarks>
    /// Gets a <see cref="VBVariantValue"/> (<see cref="VBStringValue"/>) associated with an implementation-defined <em>environment variable</em>.
    /// </remarks>
    /// <param name="key">A <see cref="VBStringValue"/>, or a data value that is let-coercible to <see cref="VBLongValue"/>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> Environ(VBVariantValue key);
    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.1.6 Environ$</strong>
    /// </summary>
    /// <remarks>
    /// Gets a <see cref="VBStringValue"/> associated with an implementation-defined <em>environment variable</em>.
    /// </remarks>
    /// <param name="key">A <see cref="VBStringValue"/>, or a data value that is let-coercible to <see cref="VBLongValue"/>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember("Environ$")]
    RuntimeSemanticsEvaluationResult<VBStringValue> EnvironString(VBVariantValue key);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.1.7 GetAllSettings</strong>
    /// </summary>
    /// <remarks>
    /// Gets a two-dimensional <see cref="VBArrayValue"/> containing <see cref="VBStringValue"/> elements representing 
    /// the configuration setting <em>keys</em> and <em>values</em> of the specified <em>settings store</em> section if it exists under the specified <em>application name</em>; returns <see cref="VBEmptyValue"/> otherwise.<br/>
    /// 👉 The settings store is the <em>workspace application settings</em> (<c>ProgramSettings:appName:section</c> in the workspace's <c>appsettings.json</c>), unless the
    /// environment asks for the legacy <em>Windows Registry</em> store (<c>LegacyRegistrySettings</c>), which only Windows has.
    /// </remarks>
    /// <param name="appName">A <see cref="VBStringValue"/> expression containing the name of the application or project whose key settings are requested.</param>
    /// <param name="section">A <see cref="VBStringValue"/> expression containing the name of the configuration section whose key settings are requested.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> GetAllSettings(VBStringValue appName, VBStringValue section);
    /// <summary>
    /// 🧩 <strong>RD-VBAL 6.1.2.8.1.7.1 GetAllJsonSettings</strong><br/>
    /// </summary>
    /// <remarks>
    /// Gets a two-dimensional <see cref="VBArrayValue"/> containing <see cref="VBStringValue"/> elements representing 
    /// the configuration setting <em>keys</em> and <em>values</em> of the specified <em>managed configuration</em> section if it exists; returns <see cref="VBEmptyValue"/> otherwise.<br/>
    /// 👉 Always the <em>workspace application settings</em>, whatever store <see cref="GetAllSettings"/> is configured to use. A child of the section that is a section of its
    /// own (it has children, and no value) is not a key setting of it, and is left out.
    /// </remarks>
    /// <param name="key">A <see cref="VBStringValue"/> expression containing the <em>managed configuration path</em> (e.g. <c>"Configuration:ConnectionStrings"</c>) of the configuration section whose key settings are requested.</param>
    /// <param name="config">A <see cref="VBStringValue"/> expression containing the <strong>name of a .json configuration file</strong> associated with the <em>workspace application</em>.<br/><strong>Optional</strong>: The default value of this parameter depends on the current host configuration (normally <c>"appsettings.json"</c>).</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> GetAllJsonSettings(VBStringValue key, VBStringValue? config = default);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.1.8 GetAttr</strong>
    /// </summary>
    /// <remarks>
    /// Gets the <see cref="VBFileAttribute"/> <strong>bitwise flags</strong> representing the <em>attributes</em> of a specified <em>file</em> or <em>directory/folder</em>.
    /// </remarks>
    /// <param name="pathName">A <see cref="VBStringValue"/> expression containing a file name. May specify a <em>mapped drive</em> and/or a <em>directory/folder path</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(ReturnType = typeof(VBFileAttribute))]
    RuntimeSemanticsEvaluationResult<VBLongValue> GetAttr(VBStringValue pathName);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.1.9 GetObject</strong> Gets the object of a class that is running, the one a file is, or a new one.
    /// </summary>
    /// <remarks>
    /// As in <strong>MS-VBA</strong> (recorded in Excel), through the session's external calls as <see cref="CreateObject"/>, and bound to late as its objects are:
    /// <list type="bullet">
    /// <item><c>GetObject(, class)</c>: the object of the class that is running; error <c>429</c> when none is, or the class is not registered;</item>
    /// <item><c>GetObject(path)</c>, <c>GetObject(path, class)</c>: the object the file is, opened by its own class or the one named - or the one a moniker
    /// names (<c>winmgmts:</c>); error <c>432</c> <em>File name or class name not found</em> when the file is not there;</item>
    /// <item><c>GetObject("", class)</c>: a new object of the class, as <see cref="CreateObject"/>;</item>
    /// <item><c>GetObject()</c> is error <c>13</c>, and <c>GetObject("")</c> error <c>5</c>.</item>
    /// </list>
    /// A class the platform implements itself is a new object of it, the one <see cref="CreateObject"/> gives, for <c>GetObject("", class)</c>; it has none
    /// running and opens no file.
    /// </remarks>
    /// <param name="pathName">The file, or the moniker, of the object; a zero-length <c>String</c> for a new one.</param>
    /// <param name="className">The programmatic identifier of the class (<c>Excel.Application</c>).</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBObjectValue> GetObject(VBVariantValue? pathName = default, VBVariantValue? className = default);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.1.10 GetSetting</strong>
    /// </summary>
    /// <remarks>
    /// Gets a <see cref="VBStringValue"/> representing the <em>key setting value</em> from the specified application's entry in an <em>implementation-defined application registry</em> if it exists; gets the specified <c>defaultValue</c> otherwise.<br/>
    /// 👉 The registry is the <em>workspace application settings</em> (<c>ProgramSettings:appName:section:key</c> in the workspace's <c>appsettings.json</c>), unless the
    /// environment asks for the legacy <em>Windows Registry</em> store (<c>LegacyRegistrySettings</c>), which only Windows has.
    /// </remarks>
    /// <param name="appName">A <see cref="VBStringValue"/> expression containing the name of the application or project whose key settings are requested.</param>
    /// <param name="section">A <see cref="VBStringValue"/> expression containing the name of the configuration section whose key settings are requested.</param>
    /// <param name="key">A <see cref="VBStringValue"/> expression containing the <em>key</em> of the requested <em>key setting</em> value.</param>
    /// <param name="defaultValue">A <see cref="VBVariantValue"/> expression containing the value to return if no value is set in the key setting.<br/><strong>Optional</strong>: defaults to <see cref="VBStringValue.ZeroLengthString"/>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> GetSetting(VBStringValue appName, VBStringValue section, VBStringValue key, VBVariantValue? defaultValue = default);
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
    RuntimeSemanticsEvaluationResult<VBVariantValue> GetJsonSetting(VBStringValue key, VBStringValue? config = default, VBVariantValue? defaultValue = default);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.1.11 IIf</strong>
    /// </summary>
    /// <remarks>
    /// Returns one of two parts, <strong>depending on the <em>evaluation of an expression</em></strong>.<br/>
    /// 👉 Both parts are evaluated, whichever is returned: they are arguments. A <c>Null</c> expression is not <c>True</c>, and returns <paramref name="falsePart"/>.<br/><br/>
    /// 💡 The wording of how this function and its parameters are specified does not exclude 🧩<c>VBDeferredValue</c> <em>language core extension</em> values that could...<br/>
    /// <em>fix the confusion around this function</em> while opening the door to something like <em>first-class delegates</em> in (extended) RD-VBA.
    /// </remarks>
    /// <param name="expression">The expression to evaluate, Let-coerced to <c>Boolean</c>.</param>
    /// <param name="truePart">The value returned when <paramref name="expression"/> is <c>True</c>.</param>
    /// <param name="falsePart">The value returned when <paramref name="expression"/> is not <c>True</c>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> IIf(VBVariantValue expression, VBVariantValue truePart, VBVariantValue falsePart);
    
    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.1.12 InputBox</strong>
    /// </summary>
    /// <remarks>
    /// Displays a <em>prompt</em> in a dialog box, waits for the user to input text or click a button, and returns a <see cref="VBStringValue"/> containing the user-provided input.<br/>
    /// 👉 A dialog that is cancelled - its <em>Cancel</em> button, or <kbd>Esc</kbd> - returns <c>vbNullString</c>, a string bound to a null pointer; a dialog confirmed with
    /// nothing typed returns a zero-length string. The two compare equal, and <c>StrPtr</c> is how a caller tells them apart.
    /// </remarks>
    /// <param name="prompt">A <see cref="VBStringValue"/> expression containing the <em>message</em> to be displayed in the dialog box.</param>
    /// <param name="title">A <see cref="VBStringValue"/> expression containing the <em>title</em> of the dialog box.<br/><strong>Optional</strong>: uses the name of the <em>workspace project</em> if omitted.</param>
    /// <param name="defaultValue">A <see cref="VBStringValue"/> expression containing the <em>default (pre-filled) value</em>.<br/><strong>Optional</strong>: defaults to <see cref="VBStringValue.ZeroLengthString"/>.</param>
    /// <param name="xpos">A <see cref="VBLongValue"/> expression containing the <em>horizontal distance</em> (measured in <em>twips</em>) of the left edge of the dialog box from the left edge of the screen.<br/><strong>Optional</strong>: dialog is horizontally centered if omitted.</param>
    /// <param name="ypos">A <see cref="VBLongValue"/> expression containing the <em>vertical distance</em> (measured in <em>twips</em>) of the top edge of the dialog box from the top edge of the screen.<br/><strong>Optional</strong>: dialog is vertically centered if omitted.</param>
    /// <param name="helpFile">ℹ️ Unsupported legacy proprietary Microsoft help system. This parameter is <strong>out of scope</strong> of this implementation.</param>
    /// <param name="helpContext">ℹ️ Unsupported legacy proprietary Microsoft help system. This parameter is <strong>out of scope</strong> of this implementation.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBStringValue> InputBox(VBVariantValue prompt, VBVariantValue? title = default, VBVariantValue? defaultValue = default, VBVariantValue? xpos = default, VBVariantValue? ypos = default, VBVariantValue? helpFile = default, VBVariantValue? helpContext = default);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.1.13 MsgBox</strong>
    /// </summary>
    /// <remarks>
    /// Displays a message in a dialog box, waits for the user to click a button, and returns a <see cref="VBMsgBoxResult"/> value indicating which button the user clicked.<br/>
    /// 👉 <kbd>Esc</kbd> closes the dialog at window level, as its close button does: it answers <see cref="VBMsgBoxResult.VBCancel"/> when the dialog has a
    /// <em>Cancel</em> button, <see cref="VBMsgBoxResult.VBOk"/> when <em>OK</em> is its only button, and does nothing for <see cref="VBMsgBoxStyle.VBYesNo"/> and
    /// <see cref="VBMsgBoxStyle.VBAbortRetryIgnore"/>, which cannot be dismissed without choosing.
    /// </remarks>
    /// <param name="prompt">A <see cref="VBStringValue"/> expression containing the <em>message</em> to be displayed in the dialog box.</param>
    /// <param name="buttons">A <see cref="VBMsgBoxStyle"/> expression containing the <strong>sum of values</strong> specifying the number and type of buttons to display, the icon style to use, the identity of the default button, and the modality of the message box.<br/><strong>Optional</strong>: defaults to <see cref="VBMsgBoxStyle.VBDefaultButton1"/> (<see cref="VBIntegerValue"/> <c>0</c>).</param>
    /// <param name="title">A <see cref="VBStringValue"/> expression containing the <em>title</em> of the dialog box.<br/><strong>Optional</strong>: uses the name of the <em>workspace project</em> if omitted.</param>
    /// <param name="helpFile">ℹ️ Unsupported legacy proprietary Microsoft help system. This parameter is <strong>out of scope</strong> of this implementation.</param>
    /// <param name="helpContext">ℹ️ Unsupported legacy proprietary Microsoft help system. This parameter is <strong>out of scope</strong> of this implementation.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(ReturnType = typeof(VBMsgBoxResult))]
    RuntimeSemanticsEvaluationResult<VBLongValue> MsgBox(VBVariantValue prompt, VBMsgBoxStyle buttons = VBMsgBoxStyle.VBDefaultButton1, VBVariantValue? title = default, VBVariantValue? helpFile = default, VBVariantValue? helpContext = default);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.1.14 Partition</strong>
    /// </summary>
    /// <remarks>
    /// Returns a <see cref="VBStringValue"/> indicating where a number occurs within a calculated series of ranges.
    /// <para>
    /// As in <strong>MS-VBA</strong> (recorded in Excel):
    /// <list type="bullet">
    /// <item>a range is <c>lower:upper</c>, the last one ending at <c>stop</c>; before the first it is <c>:start-1</c>, and after the last <c>stop+1:</c>;</item>
    /// <item>each side of the colon is padded with leading spaces to the width of the wider of <c>start-1</c> and <c>stop+1</c>, so that the ranges sort as
    /// text (<c>Partition(195, 20, 199, 10)</c> is <c>"190:199"</c>, not the spec's four characters a side);</item>
    /// <item>each part is a number rounded to a whole one as a <c>Long</c> is, half to even - one too large for a <c>Long</c> is past the range, not an
    /// overflow - and one that is no number is error <c>13</c>;</item>
    /// <item>a <c>Null</c> part makes the result <c>Null</c>, whatever the others are; the result is otherwise a <c>String</c> in a <see cref="VBVariantValue"/>.</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <param name="number">A <see cref="VBLongValue"/> expression containing the <em>value</em> to be evaluated against the <em>range</em>.</param>
    /// <param name="start">A <see cref="VBLongValue"/> expression containing the <strong>start</strong> of the overall range of numbers.<br/>👉 The numeric value of this parameter <strong>cannot be less than</strong> <c>0</c> 💥<see cref="VBRuntimeErrorId.InvalidProcedureCallOrArgument"/>.</param>
    /// <param name="stop">A <see cref="VBLongValue"/> expression containing the <strong>end</strong> of the overall range of numbers.<br/>👉 The numeric value of this parameter <strong>cannot be less than or equal to</strong> the <c>start</c> value 💥<see cref="VBRuntimeErrorId.InvalidProcedureCallOrArgument"/>.</param>
    /// <param name="interval">A <see cref="VBLongValue"/> expression containing the <strong>interval</strong> of each range of numbers.<br/>👉 The numeric value of this parameter <strong>cannot be less than</strong> <c>1</c> 💥<see cref="VBRuntimeErrorId.InvalidProcedureCallOrArgument"/>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> Partition(VBVariantValue number, VBVariantValue start, VBVariantValue stop, VBVariantValue interval);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.1.15 Shell</strong>
    /// </summary>
    /// <remarks>
    /// <strong>Asynchronously</strong> runs an executable program and returns a <see cref="VBDoubleValue"/> representing the implementation-defined program's <em>task ID</em> (or <em>process ID</em>) if successful, otherwise returns the data value <c>0</c>.<br/>
    /// 👉 Only Windows runs the program; elsewhere nothing is run, and the answer is <c>0</c>.
    /// </remarks>
    /// <param name="path">A <see cref="VBStringValue"/> expression containing the name of the program to run, and any arguments or command-line switches it takes.</param>
    /// <param name="windowStyle">A <see cref="VBIntegerValue"/> (<see cref="VBAppWinStyle"/>) expression containing a value that sets the <em>style</em> of the window in which the program is to be executed.<br/><strong>Optional</strong>: <see cref="VBAppWinStyle.VBMinimizedFocus"/> unless specified otherwise.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBDoubleValue> Shell(VBVariantValue path, VBAppWinStyle windowStyle = VBAppWinStyle.VBMinimizedFocus);

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
    /// 👉 There seems to be a 🧩<c>VBDeferredValue</c> <em>language core extension</em> opportunity here, too. See: <see cref="IIf"/>
    /// </remarks>
    /// <param name="varExpr">A <see cref="VBArrayValue"/> containing <see cref="VBVariantValue"/> elements representing expressions to be evaluated.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> Switch(params VBVariantValue[] varExpr);

    /// <summary>
    /// <strong>MacScript</strong> Runs an AppleScript script and returns what it returns.
    /// </summary>
    /// <remarks>
    /// A hidden member of MS-VBA's <c>Interaction</c> module, kept for the Macintosh versions of Office. It resolves, so that code written for both platforms
    /// compiles, and raises a run-time error when it is called: RD-VBA has no AppleScript to run.
    /// </remarks>
    /// <param name="script">The AppleScript source, or the path of a compiled script.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(IsHidden = true)]
    RuntimeSemanticsEvaluationResult<VBVariantValue> MacScript(VBStringValue script);
    #endregion

    #region 6.1.2.8.2. Public Subroutines

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.2.1 AppActivate</strong>
    /// </summary>
    /// <remarks>
    /// Changes the focus to the named application window, without affecting whether it is maximized or minimized.<br/>
    /// 👉 The title is compared to the title of each running application; with no exact match, any application whose title <em>begins with</em> it is activated.<br/>
    /// 👉 Only Windows has application windows to activate; elsewhere this does nothing.
    /// </remarks>
    /// <param name="title">A <see cref="VBStringValue"/> expression containing the title in the title bar of the application window to activate, or the task ID that <see cref="Shell"/> returned.</param>
    /// <param name="wait">A <see cref="VBBooleanValue"/> expression specifying whether the calling application has the focus before activating another.<br/><strong>Optional</strong>: <c>False</c> unless specified otherwise, which activates the application immediately.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult AppActivate(VBVariantValue title, VBVariantValue? wait = default);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.2.2 Beep</strong>
    /// </summary>
    /// <remarks>
    /// Sounds a tone through the computer's speaker.<br/>
    /// 👉 The frequency and duration of the beep depend on hardware and system software, and vary among computers. An environment with no speaker to sound it through
    /// has nothing to do, and that is not an error.
    /// </remarks>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult Beep();

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.2.3 DeleteSetting</strong>
    /// </summary>
    /// <remarks>
    /// Deletes a section or key setting from an application's entry in an <em>implementation-defined application registry</em>.<br/>
    /// 💥 A run-time error occurs if the section or key setting does not exist.
    /// </remarks>
    /// <param name="appName">A <see cref="VBStringValue"/> expression containing the name of the application or project to which the section or key setting applies.</param>
    /// <param name="section">A <see cref="VBStringValue"/> expression containing the name of the section where the key setting is being deleted.<br/><strong>Optional</strong>: given only the application name, every section of the application is deleted; given the section as well, the section is deleted with all of its key settings.</param>
    /// <param name="key">A <see cref="VBStringValue"/> expression containing the name of the key setting being deleted.<br/><strong>Optional</strong>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult DeleteSetting(VBStringValue appName, VBStringValue? section = default, VBStringValue? key = default);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.2.4 SaveSetting</strong>
    /// </summary>
    /// <remarks>
    /// Saves or creates an application entry in the application's entry in the <em>implementation-defined application registry</em>.<br/>
    /// 💥 An error occurs if the key setting can't be saved for any reason.
    /// </remarks>
    /// <param name="appName">A <see cref="VBStringValue"/> expression containing the name of the application or project to which the setting applies.</param>
    /// <param name="section">A <see cref="VBStringValue"/> expression containing the name of the section where the key setting is being saved.</param>
    /// <param name="key">A <see cref="VBStringValue"/> expression containing the name of the key setting being saved.</param>
    /// <param name="setting">A <see cref="VBStringValue"/> expression containing the value the key is being set to.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult SaveSetting(VBStringValue appName, VBStringValue section, VBStringValue key, VBStringValue setting);
    /// <summary>
    /// 🧩 <strong>RD-VBAL 6.1.2.8.2.4.1 SaveJsonSetting</strong><br/>
    /// </summary>
    /// <remarks>
    /// Saves a <em>managed configuration</em> setting in the <em>workspace application settings</em>, creating the sections of its path - and the file - as needed:
    /// what <see cref="GetJsonSetting"/> reads back.<br/>
    /// 💥 An error occurs if the setting can't be saved for any reason. The value is written as a JSON string, and a configuration file that is written to is written
    /// as JSON without its comments.
    /// </remarks>
    /// <param name="key">A <see cref="VBStringValue"/> expression containing the <em>managed configuration path</em> of the setting (e.g. <c>"Configuration:ConnectionStrings:AppDb"</c>).</param>
    /// <param name="setting">A <see cref="VBStringValue"/> expression containing the value the setting is being set to.</param>
    /// <param name="config">A <see cref="VBStringValue"/> expression containing the <strong>name of a .json configuration file</strong> associated with the <em>workspace application</em>.<br/><strong>Optional</strong>: The default value of this parameter depends on the current host configuration (normally <c>"appsettings.json"</c>).</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult SaveJsonSetting(VBStringValue key, VBStringValue setting, VBStringValue? config = default);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.8.2.5 SendKeys</strong>
    /// </summary>
    /// <remarks>
    /// Sends one or more keystrokes to the active window as if typed at the keyboard.<br/>
    /// 👉 Only Windows has an active window to send them to; elsewhere this does nothing.
    /// </remarks>
    /// <param name="string">A <see cref="VBStringValue"/> expression specifying the keystrokes to send.</param>
    /// <param name="wait">A <see cref="VBBooleanValue"/> expression specifying the wait mode.<br/><strong>Optional</strong>: <c>False</c> unless specified otherwise, which returns control to the procedure immediately after the keys are sent. With <c>True</c>, the keystrokes <strong>must</strong> be processed before control is returned.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult SendKeys(VBStringValue @string, VBVariantValue? wait = default);

    #endregion
}
