using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.StdLib;

/// <inheritdoc cref="IStdInteractionModule"/>
/// <remarks>
/// 🚧 Only <see cref="DoEvents"/> and <see cref="Beep"/> are implemented, being the two that have nothing to reach: the platform has no event queue to
/// yield to and no speaker to sound. Every other member is declared, resolves, and returns the error a member nothing implements returns - the
/// dialogs, the registry, the clipboard of keystrokes and the other processes are the environment's to provide, and the environment's seam for them is
/// the external-dispatch interception, not this class.
/// </remarks>
public sealed class StdInteraction : IStdInteractionModule
{
    /// <summary>
    /// The error every member that is declared but not written yet returns.
    /// </summary>
    /// <remarks>
    /// TODO one per member as they land. Deliberately an error rather than a plausible value: an <c>InputBox</c> that answered <c>""</c> would be worse than
    /// one that says it cannot ask.
    /// </remarks>
    private static VBRuntimeErrorInfo NotImplementedError(string member)
        => VBRuntimeErrorInfo.For(VBRuntimeErrorId.ApplicationDefinedOrObjectDefinedError, default, $"'{member}' is declared but not implemented yet.");

    private static RuntimeSemanticsEvaluationResult<TValue> NotImplemented<TValue>(string member)
        where TValue : VBTypedValue
        => RuntimeSemanticsEvaluationResult<TValue>.Error(NotImplementedError(member));

    private static RuntimeSemanticsEvaluationResult NotImplemented(string member)
        => RuntimeSemanticsEvaluationResult.Error(NotImplementedError(member));

    // MS-VBAL 6.1.2.8.1.5: "an Integer with an implementation-defined meaning". VB6 reports the number of forms that are open, and the platform has none.
    public RuntimeSemanticsEvaluationResult<VBIntegerValue> DoEvents()
        => RuntimeSemanticsEvaluationResult<VBIntegerValue>.Success(new VBIntegerValue(0));

    // MS-VBAL 6.1.2.8.2.2: the tone depends on hardware and system software; with no speaker there is no tone, and nothing to report.
    // TODO route it to the client's bell when a client says it can ring one.
    public RuntimeSemanticsEvaluationResult Beep() => RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);

    public RuntimeSemanticsEvaluationResult<VBVariantValue> CallByName(VBObjectValue objectValue, VBStringValue procName, VBCallType callType, params VBVariantValue[] args)
        => NotImplemented<VBVariantValue>(nameof(CallByName));

    public RuntimeSemanticsEvaluationResult<VBVariantValue> Choose(VBSingleValue index, params VBVariantValue[] choice)
        => NotImplemented<VBVariantValue>(nameof(Choose));

    public RuntimeSemanticsEvaluationResult<VBVariantValue> Command() => NotImplemented<VBVariantValue>(nameof(Command));

    public RuntimeSemanticsEvaluationResult<VBStringValue> CommandString() => NotImplemented<VBStringValue>("Command$");

    public RuntimeSemanticsEvaluationResult<VBObjectValue> CreateObject(VBStringValue objectClass, VBStringValue? serverName = default)
        => NotImplemented<VBObjectValue>(nameof(CreateObject));

    public RuntimeSemanticsEvaluationResult<VBVariantValue> Environ(VBVariantValue key) => NotImplemented<VBVariantValue>(nameof(Environ));

    public RuntimeSemanticsEvaluationResult<VBStringValue> EnvironString(VBVariantValue key) => NotImplemented<VBStringValue>("Environ$");

    public RuntimeSemanticsEvaluationResult<VBVariantValue> GetAllSettings(VBStringValue appName, VBStringValue section)
        => NotImplemented<VBVariantValue>(nameof(GetAllSettings));

    public RuntimeSemanticsEvaluationResult<VBVariantValue> GetJsonSettings(VBStringValue key, VBStringValue? config = default)
        => NotImplemented<VBVariantValue>(nameof(GetJsonSettings));

    public RuntimeSemanticsEvaluationResult<VBLongValue> GetAttr(VBStringValue pathName) => NotImplemented<VBLongValue>(nameof(GetAttr));

    public RuntimeSemanticsEvaluationResult<VBObjectValue> GetObject(VBVariantValue? pathName = default, VBVariantValue? className = default)
        => NotImplemented<VBObjectValue>(nameof(GetObject));

    public RuntimeSemanticsEvaluationResult<VBVariantValue> GetSetting(VBStringValue appName, VBStringValue section, VBStringValue key, VBVariantValue? defaultValue = default)
        => NotImplemented<VBVariantValue>(nameof(GetSetting));

    public RuntimeSemanticsEvaluationResult<VBVariantValue> GetJsonSetting(VBStringValue key, VBStringValue? config = default, VBVariantValue? defaultValue = default)
        => NotImplemented<VBVariantValue>(nameof(GetJsonSetting));

    public RuntimeSemanticsEvaluationResult<VBVariantValue> IIf(VBVariantValue expression, VBVariantValue truePart, VBVariantValue falsePart)
        => NotImplemented<VBVariantValue>(nameof(IIf));

    public RuntimeSemanticsEvaluationResult<VBStringValue> InputBox(
        VBVariantValue prompt, VBVariantValue? title = default, VBVariantValue? defaultValue = default, VBVariantValue? xpos = default,
        VBVariantValue? ypos = default, VBVariantValue? helpFile = default, VBVariantValue? helpContext = default)
        => NotImplemented<VBStringValue>(nameof(InputBox));

    public RuntimeSemanticsEvaluationResult<VBLongValue> MsgBox(
        VBVariantValue prompt, VBMsgBoxStyle buttons = VBMsgBoxStyle.VBDefaultButton1, VBVariantValue? title = default,
        VBVariantValue? helpFile = default, VBVariantValue? helpContext = default)
        => NotImplemented<VBLongValue>(nameof(MsgBox));

    public RuntimeSemanticsEvaluationResult<VBVariantValue> Partition(VBVariantValue number, VBVariantValue start, VBVariantValue stop, VBVariantValue interval)
        => NotImplemented<VBVariantValue>(nameof(Partition));

    public RuntimeSemanticsEvaluationResult<VBDoubleValue> Shell(VBVariantValue path, VBAppWinStyle windowStyle = VBAppWinStyle.VBMinimizedFocus)
        => NotImplemented<VBDoubleValue>(nameof(Shell));

    public RuntimeSemanticsEvaluationResult<VBVariantValue> Switch(params VBVariantValue[] varExpr) => NotImplemented<VBVariantValue>(nameof(Switch));

    public RuntimeSemanticsEvaluationResult AppActivate(VBVariantValue title, VBVariantValue? wait = default) => NotImplemented(nameof(AppActivate));

    public RuntimeSemanticsEvaluationResult DeleteSetting(VBStringValue appName, VBStringValue? section = default, VBStringValue? key = default)
        => NotImplemented(nameof(DeleteSetting));

    public RuntimeSemanticsEvaluationResult SaveSetting(VBStringValue appName, VBStringValue section, VBStringValue key, VBStringValue setting)
        => NotImplemented(nameof(SaveSetting));

    public RuntimeSemanticsEvaluationResult SendKeys(VBStringValue @string, VBVariantValue? wait = default) => NotImplemented(nameof(SendKeys));
}
