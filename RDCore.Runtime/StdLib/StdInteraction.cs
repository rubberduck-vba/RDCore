using RDCore.External;
using RDCore.External.Interaction;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics.Conversion;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Runtime.Shared;
using System.Security;

namespace RDCore.Runtime.StdLib;

/// <inheritdoc cref="IStdInteractionModule"/>
/// <remarks>
/// <para>
/// What this module reaches is the outside world's (<see cref="ExternalWorld"/>), and the host says what that is: the dialogs of <c>MsgBox</c> and <c>InputBox</c> are
/// the person running the program's to answer - Windows' own, the interactive shell's console, or nobody's - the desktop of <c>DoEvents</c>, <c>AppActivate</c>,
/// <c>SendKeys</c> and <c>Shell</c> is Windows' or nothing, and the settings are the workspace's <c>appsettings.json</c>, or the registry when the workspace asks for it.
/// An analysis reaches none of it (<see cref="ExternalWorld.None"/>).
/// </para>
/// <para>
/// 🚧 <see cref="Command"/>, <see cref="CommandString"/>, <see cref="CreateObject"/>, <see cref="GetObject"/>, <see cref="GetAttr"/> and <see cref="Partition"/> are
/// declared and not written, and <see cref="MacScript"/> never will be: each returns an error that says so.
/// </para>
/// </remarks>
/// <param name="session">The session whose objects <see cref="CallByName"/> calls, and whose turn <see cref="DoEvents"/> gives up.</param>
/// <param name="outside">What the dialogs, the desktop and the settings are.</param>
/// <param name="cancellation">The run's cancellation: a dialog that is open when the run is stopped stops being waited for.</param>
/// <param name="environment">The environment variables of <see cref="Environ"/>; the process's own unless said otherwise.</param>
public sealed class StdInteraction(
    IRuntimeSession session, ExternalWorld? outside = null, CancellationToken cancellation = default, ProcessEnvironment? environment = null) : IStdInteractionModule
{
    private readonly ExternalWorld _outside = outside ?? ExternalWorld.None;
    private readonly ProcessEnvironment _environment = environment ?? ProcessEnvironment.Current;

    /// <summary>
    /// The error every member that is declared but not written yet returns.
    /// </summary>
    /// <remarks>
    /// Deliberately an error rather than a plausible value: a <c>Command</c> that answered <c>""</c> would be worse than one that says it cannot answer.
    /// </remarks>
    private static VBRuntimeErrorInfo NotImplementedError(string member, string because = "it is declared but not implemented yet")
        => VBRuntimeErrorInfo.For(VBRuntimeErrorId.ApplicationDefinedOrObjectDefinedError, default, $"'{member}' is not implemented: {because}.");

    private static RuntimeSemanticsEvaluationResult<TValue> NotImplemented<TValue>(string member)
        where TValue : VBTypedValue
        => RuntimeSemanticsEvaluationResult<TValue>.Error(NotImplementedError(member));

    private static RuntimeSemanticsEvaluationResult<TValue> Fail<TValue>(VBRuntimeErrorId error, string detail)
        where TValue : VBTypedValue
        => RuntimeSemanticsEvaluationResult<TValue>.Error(VBRuntimeErrorInfo.For(error, default, detail));

    private static RuntimeSemanticsEvaluationResult Fail(VBRuntimeErrorId error, string detail)
        => RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(error, default, detail));

    private static RuntimeSemanticsEvaluationResult<TValue> Succeed<TValue>(TValue value)
        where TValue : VBTypedValue
        => RuntimeSemanticsEvaluationResult<TValue>.Success(value);

    // a Sub yields no value, which the result has no way to say: Empty is what a call of one is the value of.
    private static RuntimeSemanticsEvaluationResult Done() => RuntimeSemanticsEvaluationResult.Success(VBEmptyValue.Empty);

    private static VBVariantValue InVariant(VBTypedValue value) => value as VBVariantValue ?? new VBVariantValue(value);

    private static VBVariantValue NullVariant => new(VBNullValue.Null);

    // what a Variant argument holds, however many Variants deep.
    private static VBTypedValue Unwrapped(VBTypedValue value)
    {
        while (value is VBVariantValue { TypedValue: var wrapped })
        {
            value = wrapped;
        }

        return value;
    }

    // the dispatcher passes null for an argument the caller left out, and the interpreter fills an omitted Optional Variant with Empty: both are omitted.
    private static bool IsOmitted(VBTypedValue? value) => value is null || Unwrapped(value) is VBEmptyValue or VBMissingValue;

    // an omitted Optional String is filled with the type's default, vbNullString, and is no different from one given as "".
    private static string? OptionalText(VBStringValue? value) => value is { Value.Length: > 0 } given ? given.Value : null;

    private static RuntimeSemanticsEvaluationResult<VBTypedValue> Convert(VBTypedValue value, Func<VBTypedValue, ValueConversionResult> conversion, string destination)
    {
        var source = Unwrapped(value);
        var converted = conversion(source);
        return converted.IsSuccess
            ? RuntimeSemanticsEvaluationResult<VBTypedValue>.Success(converted.Value!)
            : Fail<VBTypedValue>(converted.Error ?? VBRuntimeErrorId.TypeMismatch, $"A value of type {source.TypeInfo.Name} cannot be converted to {destination}.");
    }

    // a String is written in the regional settings of the session's environment, as Let-coercion writes it.
    private RuntimeSemanticsEvaluationResult<VBTypedValue> ToText(VBTypedValue value) => Convert(value, source => ValueConversions.ToText(source, session.Environment.Culture), "String");

    private static RuntimeSemanticsEvaluationResult<VBTypedValue> ToLong(VBTypedValue value) => Convert(value, source => ValueConversions.ToNumeric(source, VBLongType.TypeInfo), "Long");

    private static RuntimeSemanticsEvaluationResult<VBTypedValue> ToBoolean(VBTypedValue value) => Convert(value, ValueConversions.ToBoolean, "Boolean");

    private static bool IsTrue(VBTypedValue boolean) => boolean is VBBooleanValue { Value.StoredValue: not 0 };

    #region pure functions

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> IIf(VBVariantValue expression, VBVariantValue truePart, VBVariantValue falsePart)
    {
        // Null is not True: the condition of an If statement that is Null takes its Else, and so does this.
        if (Unwrapped(expression) is VBNullValue)
        {
            return Succeed(falsePart);
        }

        var condition = ToBoolean(expression);
        return condition.IsSuccess
            ? Succeed(IsTrue(condition.Result!) ? truePart : falsePart)
            : RuntimeSemanticsEvaluationResult<VBVariantValue>.Error(condition.ErrorInfo!);
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Choose(VBSingleValue index, params VBVariantValue[] choice)
    {
        // "rounded to the nearest whole number": the way a Single is Let-coerced to a Long, banker's rounding and all.
        var position = ToLong(index);
        if (!position.IsSuccess)
        {
            // an index too large for a Long is out of the bounds of any list of choices there can be.
            return position.ErrorInfo!.ErrorId == (int)VBRuntimeErrorId.Overflow
                ? Succeed(NullVariant)
                : RuntimeSemanticsEvaluationResult<VBVariantValue>.Error(position.ErrorInfo!);
        }

        var ordinal = ((VBLongValue)position.Result!).Value;
        return Succeed(ordinal >= 1 && ordinal <= choice.Length ? choice[ordinal - 1] : NullVariant);
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Switch(params VBVariantValue[] varExpr)
    {
        if (varExpr.Length % 2 != 0)
        {
            return Fail<VBVariantValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument,
                $"Switch was given {varExpr.Length} arguments: each expression is followed by the value it selects.");
        }

        for (var pair = 0; pair < varExpr.Length; pair += 2)
        {
            if (Unwrapped(varExpr[pair]) is VBNullValue)
            {
                continue;
            }

            var condition = ToBoolean(varExpr[pair]);
            if (!condition.IsSuccess)
            {
                return RuntimeSemanticsEvaluationResult<VBVariantValue>.Error(condition.ErrorInfo!);
            }

            if (IsTrue(condition.Result!))
            {
                return Succeed(varExpr[pair + 1]);
            }
        }

        return Succeed(NullVariant);
    }

    #endregion

    #region CallByName

    /// <inheritdoc/>
    /// <remarks>
    /// The member is looked up by name on the default interface of the object's class - late-bound, as <c>IDispatch</c> would look it up - whether the class is the
    /// workspace's or a library's, and invoked the way a call written in source invokes it. <see cref="VBCallType.VBMethod"/> finds a <c>Sub</c> or a
    /// <c>Function</c> (or, failing those, a <c>Property Get</c>); <see cref="VBCallType.VBGet"/> a <c>Property Get</c>, a <c>Function</c> or a public variable;
    /// <see cref="VBCallType.VBLet"/> and <see cref="VBCallType.VBSet"/> a <c>Property Let</c> and a <c>Property Set</c>, the value assigned being the last argument.
    /// <para>
    /// The arguments are passed by value, each Let-coerced to the type of its parameter. An omitted <c>Optional</c> parameter is given its type's default.
    /// 🚧 Assigning a public variable by name is not supported yet (error 438).
    /// </para>
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> CallByName(VBObjectValue objectValue, VBStringValue procName, VBCallType callType, params VBVariantValue[] args)
    {
        if (objectValue.IsNothing())
        {
            return Fail<VBVariantValue>(VBRuntimeErrorId.ObjectVariableOrWithBlockVariableNotSet, $"CallByName(Nothing, \"{procName.Value}\"): there is no object to call.");
        }

        if (!session.Symbols.TryGetInstance(objectValue.Value, out var instance) || session.Callables is not { } callables)
        {
            return Fail<VBVariantValue>(VBRuntimeErrorId.ObjectDoesntSupportThisPropertyOrMethod, $"The object has no member '{procName.Value}' to call by name.");
        }

        var candidates = instance.ClassModule.DefaultInterfaceMembers
            .Where(member => string.Equals(member.Name, procName.Value, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (callType is VBCallType.VBGet && candidates.FirstOrDefault(member => member.Kind is SymbolKindExt.Field or SymbolKindExt.Variable) is { } field)
        {
            return args.Length == 0
                ? Succeed(InVariant(field.ResolvedType.CreateValue(instance.GetValue(field).ForReading())))
                : Fail<VBVariantValue>(VBRuntimeErrorId.WrongNumberOfArgumentsOrInvalidPropertyAssignment, $"'{field.Name}' is a variable, and takes no arguments.");
        }

        if (MemberOf(candidates, callType) is not { } target)
        {
            return Fail<VBVariantValue>(VBRuntimeErrorId.ObjectDoesntSupportThisPropertyOrMethod,
                $"The object has no member '{procName.Value}' that can be called as {callType}.");
        }

        var parameters = RuntimeProcedureInvoker.GetParameters(target).Where(parameter => parameter.Name != "Me").ToArray();
        if (args.Length > parameters.Length)
        {
            return Fail<VBVariantValue>(VBRuntimeErrorId.WrongNumberOfArgumentsOrInvalidPropertyAssignment,
                $"'{target.Name}' takes {parameters.Length} argument(s), and was given {args.Length}.");
        }

        var bound = new IRuntimeValue[parameters.Length];
        for (var index = 0; index < parameters.Length; index++)
        {
            var parameter = parameters[index];
            if (index >= args.Length)
            {
                if (!parameter.IsOptional)
                {
                    return Fail<VBVariantValue>(VBRuntimeErrorId.ArgumentNotOptional, $"'{target.Name}' was not given its argument '{parameter.Name}'.");
                }

                bound[index] = parameter.ResolvedType.DefaultValue.RuntimeValue;
                continue;
            }

            var coerced = Coerce(args[index], parameter.ResolvedType);
            if (!coerced.IsSuccess)
            {
                return RuntimeSemanticsEvaluationResult<VBVariantValue>.Error(coerced.ErrorInfo!);
            }

            bound[index] = coerced.Result!.RuntimeValue;
        }

        var called = callables.ForMember(target, objectValue.RuntimeValue).Call(session.Symbols.Resolver, bound);
        if (!called.IsSuccess)
        {
            return RuntimeSemanticsEvaluationResult<VBVariantValue>.Error(called.ErrorInfo ?? NotImplementedError(nameof(CallByName), "the call had no answer"));
        }

        return Succeed(called.Result is null or VBVoidValue ? new VBVariantValue(VBEmptyValue.Empty) : InVariant(called.Result));
    }

    private static VBTypeMemberSymbol? MemberOf(IReadOnlyList<VBTypeMemberSymbol> candidates, VBCallType callType)
    {
        bool IsMethod(VBTypeMemberSymbol member) => member is VBFunctionMemberSymbol or VBProcedureMemberSymbol
            && member is not (VBPropertyGetMemberSymbol or VBPropertyLetMemberSymbol or VBPropertySetMemberSymbol);

        return callType switch
        {
            VBCallType.VBMethod => candidates.FirstOrDefault(IsMethod) ?? candidates.FirstOrDefault(member => member is VBPropertyGetMemberSymbol),
            VBCallType.VBGet => candidates.FirstOrDefault(member => member is VBPropertyGetMemberSymbol) ?? candidates.FirstOrDefault(member => member is VBFunctionMemberSymbol),
            VBCallType.VBLet => candidates.FirstOrDefault(member => member is VBPropertyLetMemberSymbol),
            VBCallType.VBSet => candidates.FirstOrDefault(member => member is VBPropertySetMemberSymbol),
            _ => null,
        };
    }

    // what the argument of a call written in source is Let-coerced to, from the value alone: a Variant takes it as it is, an object is passed as the reference it is.
    private RuntimeSemanticsEvaluationResult<VBTypedValue> Coerce(VBVariantValue argument, VBType declared)
    {
        var value = Unwrapped(argument);
        if (declared is VBVariantType)
        {
            return RuntimeSemanticsEvaluationResult<VBTypedValue>.Success(argument);
        }

        Func<VBTypedValue, ValueConversionResult>? conversion = value is VBObjectValue ? null : declared switch
        {
            VBNumericType numeric => source => ValueConversions.ToNumeric(source, numeric),
            VBStringType => source => ValueConversions.ToText(source, session.Environment.Culture),
            VBBooleanType => ValueConversions.ToBoolean,
            VBDateType => ValueConversions.ToDate,
            _ => null,
        };

        return conversion is null ? RuntimeSemanticsEvaluationResult<VBTypedValue>.Success(value) : Convert(value, conversion, declared.Name);
    }

    #endregion

    #region dialogs

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBLongValue> MsgBox(
        VBVariantValue prompt, VBMsgBoxStyle buttons = VBMsgBoxStyle.VBDefaultButton1, VBVariantValue? title = default,
        VBVariantValue? helpFile = default, VBVariantValue? helpContext = default)
    {
        var text = ToText(prompt);
        if (!text.IsSuccess)
        {
            return RuntimeSemanticsEvaluationResult<VBLongValue>.Error(text.ErrorInfo!);
        }

        var caption = TitleOf(title);
        if (!caption.IsSuccess)
        {
            return RuntimeSemanticsEvaluationResult<VBLongValue>.Error(caption.ErrorInfo!);
        }

        var request = new MessageBoxRequest(text.Result!.ToString()!, caption.Result!.ToString()!, buttons);
        try
        {
            return Succeed(new VBLongValue((int)_outside.Interaction.ShowMessage(request, cancellation)));
        }
        catch (OperationCanceledException)
        {
            // the run is being stopped, and stops at the next instruction: what the dialog answers in the meantime is the one a window closed without a choice answers.
            return Succeed(new VBLongValue((int)(request.EscapeResult ?? request.Choices[request.DefaultChoice])));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Fail<VBLongValue>(VBRuntimeErrorId.CantPerformRequestedOperation, $"The message could not be shown: {exception.Message}");
        }
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> InputBox(
        VBVariantValue prompt, VBVariantValue? title = default, VBVariantValue? defaultValue = default, VBVariantValue? xpos = default,
        VBVariantValue? ypos = default, VBVariantValue? helpFile = default, VBVariantValue? helpContext = default)
    {
        var text = ToText(prompt);
        var caption = TitleOf(title);
        var initial = IsOmitted(defaultValue) ? RuntimeSemanticsEvaluationResult<VBTypedValue>.Success(VBStringValue.ZeroLengthString) : ToText(defaultValue!);
        RuntimeSemanticsEvaluationResult<VBTypedValue>? x = IsOmitted(xpos) ? null : ToLong(xpos!);
        RuntimeSemanticsEvaluationResult<VBTypedValue>? y = IsOmitted(ypos) ? null : ToLong(ypos!);
        if (new[] { text, caption, initial, x, y }.FirstOrDefault(result => result is { IsSuccess: false }) is { } failed)
        {
            return RuntimeSemanticsEvaluationResult<VBStringValue>.Error(failed.ErrorInfo!);
        }

        var request = new InputBoxRequest(
            text.Result!.ToString()!, caption.Result!.ToString()!, initial.Result!.ToString()!,
            x is { Result: VBLongValue left } ? left.Value : null, y is { Result: VBLongValue top } ? top.Value : null);
        try
        {
            // a dialog that was cancelled answers vbNullString, and one that was confirmed with nothing in it a zero-length string: StrPtr tells them apart.
            return Succeed(_outside.Interaction.Prompt(request, cancellation) is { } typed ? new VBStringValue(typed) : VBStringValue.VBNullString);
        }
        catch (OperationCanceledException)
        {
            return Succeed(VBStringValue.VBNullString);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Fail<VBStringValue>(VBRuntimeErrorId.CantPerformRequestedOperation, $"The input box could not be shown: {exception.Message}");
        }
    }

    private RuntimeSemanticsEvaluationResult<VBTypedValue> TitleOf(VBVariantValue? title)
        => IsOmitted(title) ? RuntimeSemanticsEvaluationResult<VBTypedValue>.Success(new VBStringValue(_outside.DialogTitle)) : ToText(title!);

    #endregion

    #region desktop

    /// <inheritdoc/>
    /// <remarks>
    /// The session's turn is given up for the events that wait for it - those of the objects of a library, which only Windows has - and the messages that wait in
    /// the queue of the thread the program runs on are processed, which only Windows has either. Elsewhere there is nothing to yield to, and it returns at once.
    /// The answer is <c>0</c>: the number of open forms, of which there are none.
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBIntegerValue> DoEvents()
    {
        session.Turn.Pump();
        _outside.Desktop.DoEvents();
        return Succeed(new VBIntegerValue(0));
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult Beep()
    {
        _outside.Desktop.Beep();
        return Done();
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult AppActivate(VBVariantValue title, VBVariantValue? wait = default)
    {
        var waits = WaitOf(wait);
        if (!waits.IsSuccess)
        {
            return RuntimeSemanticsEvaluationResult.Error(waits.ErrorInfo!);
        }

        // a number is the task ID Shell returned, and anything else is the title of a window.
        var target = Unwrapped(title);
        bool found;
        if (target.TypeInfo is VBNumericType)
        {
            var processId = ToLong(target);
            if (!processId.IsSuccess)
            {
                return RuntimeSemanticsEvaluationResult.Error(processId.ErrorInfo!);
            }

            found = _outside.Desktop.AppActivate(((VBLongValue)processId.Result!).Value, IsTrue(waits.Result!));
        }
        else
        {
            var text = ToText(target);
            if (!text.IsSuccess)
            {
                return RuntimeSemanticsEvaluationResult.Error(text.ErrorInfo!);
            }

            found = _outside.Desktop.AppActivate(text.Result!.ToString()!, IsTrue(waits.Result!));
        }

        return found ? Done() : Fail(VBRuntimeErrorId.InvalidProcedureCallOrArgument, $"No application window is '{target}'.");
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult SendKeys(VBStringValue @string, VBVariantValue? wait = default)
    {
        var waits = WaitOf(wait);
        if (!waits.IsSuccess)
        {
            return RuntimeSemanticsEvaluationResult.Error(waits.ErrorInfo!);
        }

        try
        {
            _outside.Desktop.SendKeys(@string.Value, IsTrue(waits.Result!));
            return Done();
        }
        catch (FormatException exception)
        {
            return Fail(VBRuntimeErrorId.InvalidProcedureCallOrArgument, exception.Message);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Fail(VBRuntimeErrorId.CantPerformRequestedOperation, $"The keys could not be sent: {exception.Message}");
        }
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBDoubleValue> Shell(VBVariantValue path, VBAppWinStyle windowStyle = VBAppWinStyle.VBMinimizedFocus)
    {
        var commandLine = ToText(path);
        if (!commandLine.IsSuccess)
        {
            return RuntimeSemanticsEvaluationResult<VBDoubleValue>.Error(commandLine.ErrorInfo!);
        }

        try
        {
            return Succeed(new VBDoubleValue(_outside.Desktop.Shell(commandLine.Result!.ToString()!, windowStyle)));
        }
        catch (FileNotFoundException exception)
        {
            return Fail<VBDoubleValue>(VBRuntimeErrorId.FileNotFound, exception.Message);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Fail<VBDoubleValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, $"'{commandLine.Result}' could not be run: {exception.Message}");
        }
    }

    private static RuntimeSemanticsEvaluationResult<VBTypedValue> WaitOf(VBVariantValue? wait)
        => IsOmitted(wait) ? RuntimeSemanticsEvaluationResult<VBTypedValue>.Success(new VBBooleanValue(false)) : ToBoolean(wait!);

    #endregion

    #region environment

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Environ(VBVariantValue key)
    {
        var read = ReadEnvironment(key);
        return read.IsSuccess
            ? Succeed(new VBVariantValue(read.Result!))
            : RuntimeSemanticsEvaluationResult<VBVariantValue>.Error(read.ErrorInfo!);
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> EnvironString(VBVariantValue key) => ReadEnvironment(key);

    // MS-VBAL 6.1.2.8.1.6: a string is the name of a variable, and anything else the position of one in the table, from 1. A name that is not in the table, and a
    // position past its end, are a zero-length string.
    private RuntimeSemanticsEvaluationResult<VBStringValue> ReadEnvironment(VBVariantValue key)
    {
        var value = Unwrapped(key);
        if (value is VBNullValue)
        {
            return Fail<VBStringValue>(VBRuntimeErrorId.InvalidUseOfNull, "Environ(Null): Null names no environment variable.");
        }

        if (value is VBStringValue name)
        {
            return Succeed(new VBStringValue(_environment.Get(name.Value) ?? string.Empty));
        }

        var ordinal = ToLong(value);
        if (!ordinal.IsSuccess)
        {
            return RuntimeSemanticsEvaluationResult<VBStringValue>.Error(ordinal.ErrorInfo!);
        }

        var position = ((VBLongValue)ordinal.Result!).Value;
        return position < 1
            ? Fail<VBStringValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, $"Environ({position}): the table of environment variables is numbered from 1.")
            : Succeed(new VBStringValue(_environment.GetAt(position) ?? string.Empty));
    }

    #endregion

    #region settings

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> GetSetting(VBStringValue appName, VBStringValue section, VBStringValue key, VBVariantValue? defaultValue = default)
        => Read(() => _outside.ProgramSettings.Get(appName.Value, section.Value, key.Value), defaultValue);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> GetAllSettings(VBStringValue appName, VBStringValue section)
        => ReadAll(() => _outside.ProgramSettings.GetAll(appName.Value, section.Value));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult SaveSetting(VBStringValue appName, VBStringValue section, VBStringValue key, VBStringValue setting)
        => Write(() => _outside.ProgramSettings.Save(appName.Value, section.Value, key.Value, setting.Value));

    /// <inheritdoc/>
    /// <remarks>
    /// Given an application and no section, every section of the application is deleted. A section or key setting that does not exist is error 5.
    /// </remarks>
    public RuntimeSemanticsEvaluationResult DeleteSetting(VBStringValue appName, VBStringValue? section = default, VBStringValue? key = default)
    {
        var (sectionName, keyName) = (OptionalText(section), OptionalText(key));
        if (sectionName is null && keyName is not null)
        {
            return Fail(VBRuntimeErrorId.InvalidProcedureCallOrArgument, "DeleteSetting: a key setting is deleted from a section, and none was named.");
        }

        var deleted = false;
        var written = Write(() => deleted = _outside.ProgramSettings.Delete(appName.Value, sectionName, keyName));
        return !written.IsSuccess || deleted
            ? written
            : Fail(VBRuntimeErrorId.InvalidProcedureCallOrArgument,
                $"DeleteSetting: '{string.Join("\\", new[] { appName.Value, sectionName, keyName }.Where(name => name is not null))}' is not a setting that exists.");
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> GetJsonSetting(VBStringValue key, VBStringValue? config = default, VBVariantValue? defaultValue = default)
        => Read(() => _outside.WorkspaceSettings.Get(key.Value, OptionalText(config)), defaultValue);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> GetAllJsonSettings(VBStringValue key, VBStringValue? config = default)
        => ReadAll(() => _outside.WorkspaceSettings.GetAll(key.Value, OptionalText(config)));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult SaveJsonSetting(VBStringValue key, VBStringValue setting, VBStringValue? config = default)
        => Write(() => _outside.WorkspaceSettings.Save(key.Value, setting.Value, OptionalText(config)));

    // a setting that is not there is the default, which is "" when there is none: a setting is a string, and so is what stands in for one.
    private RuntimeSemanticsEvaluationResult<VBVariantValue> Read(Func<string?> read, VBVariantValue? defaultValue)
    {
        string? value;
        try
        {
            value = read();
        }
        catch (Exception exception) when (SettingsError(exception) is { } error)
        {
            return Fail<VBVariantValue>(error, exception.Message);
        }

        if (value is not null)
        {
            return Succeed(new VBVariantValue(new VBStringValue(value)));
        }

        if (IsOmitted(defaultValue))
        {
            return Succeed(new VBVariantValue(VBStringValue.ZeroLengthString));
        }

        var fallback = ToText(defaultValue!);
        return fallback.IsSuccess
            ? Succeed(new VBVariantValue(fallback.Result!))
            : RuntimeSemanticsEvaluationResult<VBVariantValue>.Error(fallback.ErrorInfo!);
    }

    // MS-VBAL 6.1.2.8.1.7: a two-dimensional array of strings, a row for each key setting with its key in column 0 and its value in column 1; Empty when there is no
    // such section, or nothing in it.
    private static RuntimeSemanticsEvaluationResult<VBVariantValue> ReadAll(Func<IReadOnlyList<KeyValuePair<string, string>>?> read)
    {
        IReadOnlyList<KeyValuePair<string, string>>? settings;
        try
        {
            settings = read();
        }
        catch (Exception exception) when (SettingsError(exception) is { } error)
        {
            return Fail<VBVariantValue>(error, exception.Message);
        }

        if (settings is not { Count: > 0 })
        {
            return Succeed(new VBVariantValue(VBEmptyValue.Empty));
        }

        var array = new VBResizableArrayValue([(0, settings.Count - 1), (0, 1)], VBVariantType.TypeInfo);
        for (var row = 0; row < settings.Count; row++)
        {
            array.TrySetElement(BindingKnowledge.NewCell(new VBVariantValue(new VBStringValue(settings[row].Key))), row, 0);
            array.TrySetElement(BindingKnowledge.NewCell(new VBVariantValue(new VBStringValue(settings[row].Value))), row, 1);
        }

        return Succeed(new VBVariantValue(array));
    }

    private static RuntimeSemanticsEvaluationResult Write(Action write)
    {
        try
        {
            write();
            return Done();
        }
        catch (Exception exception) when (SettingsError(exception) is { } error)
        {
            return Fail(error, exception.Message);
        }
    }

    // a name or a file the store cannot take is the call's mistake (5); a file that is not JSON is 321; one that cannot be read or written is the file system's, as
    // it is for a file statement.
    private static VBRuntimeErrorId? SettingsError(Exception exception) => exception switch
    {
        ArgumentException or InvalidOperationException => VBRuntimeErrorId.InvalidProcedureCallOrArgument,
        FormatException => VBRuntimeErrorId.InvalidFileFormat,
        UnauthorizedAccessException or SecurityException => VBRuntimeErrorId.PermissionDenied,
        IOException => VBRuntimeErrorId.PathOrFileAccessError,
        _ => null,
    };

    #endregion

    #region not implemented

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> MacScript(VBStringValue script)
        => RuntimeSemanticsEvaluationResult<VBVariantValue>.Error(NotImplementedError(nameof(MacScript), "it runs AppleScript, which RD-VBA has none of"));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Command() => NotImplemented<VBVariantValue>(nameof(Command));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> CommandString() => NotImplemented<VBStringValue>("Command$");

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBObjectValue> CreateObject(VBStringValue objectClass, VBStringValue? serverName = default)
        => NotImplemented<VBObjectValue>(nameof(CreateObject));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBLongValue> GetAttr(VBStringValue pathName) => NotImplemented<VBLongValue>(nameof(GetAttr));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBObjectValue> GetObject(VBVariantValue? pathName = default, VBVariantValue? className = default)
        => NotImplemented<VBObjectValue>(nameof(GetObject));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Partition(VBVariantValue number, VBVariantValue start, VBVariantValue stop, VBVariantValue interval)
        => NotImplemented<VBVariantValue>(nameof(Partition));

    #endregion
}
