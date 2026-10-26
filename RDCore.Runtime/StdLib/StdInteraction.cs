using RDCore.External;
using RDCore.External.Interaction;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics.Conversion;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
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
/// <c>SendKeys</c> and <c>Shell</c> is Windows' or nothing, the settings are the workspace's <c>appsettings.json</c>, or the registry when the workspace asks for it,
/// and the arguments of <see cref="Command"/> are none unless the host gives some. An analysis reaches none of it (<see cref="ExternalWorld.None"/>).
/// </para>
/// <para>
/// <see cref="MacScript"/> is not written, and never will be: it returns an error that says so.
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
    private RuntimeSemanticsEvaluationResult<VBTypedValue> ToText(VBTypedValue value) => Convert(value, source => ValueConversions.ToText(source, session.Environment.Culture, session.Environment.Regional), "String");

    private RuntimeSemanticsEvaluationResult<VBTypedValue> ToLong(VBTypedValue value)
        => Convert(value, source => ValueConversions.ToNumeric(source, VBLongType.TypeInfo, session.Environment.Regional), "Long");

    private RuntimeSemanticsEvaluationResult<VBTypedValue> ToBoolean(VBTypedValue value)
        => Convert(value, source => ValueConversions.ToBoolean(source, session.Environment.Regional), "Boolean");

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
    /// The arguments are passed by value, each Let-coerced to the type of its parameter. An omitted <c>Optional</c> parameter is given what a call written in
    /// source gives it: its declared default, or Missing for a <c>Variant</c> that declares none. <see cref="VBCallType.VBLet"/> and
    /// <see cref="VBCallType.VBSet"/> of a public variable assign it, as an assignment written in source does.
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

        // a late-bound object is called by the name, as the call type says: its arguments are the member's ParamArray, and an assignment's value is the last.
        if (Execution.External.Automation.LateBinding.IsLateBound(instance.ClassModule))
        {
            var assigns = callType is VBCallType.VBLet or VBCallType.VBSet;
            if (assigns && args.Length == 0)
            {
                return Fail<VBVariantValue>(VBRuntimeErrorId.ArgumentNotOptional, $"'{procName.Value}' was not given the value it is assigned.");
            }

            var lateBound = Execution.External.Automation.LateBinding.MemberOf(instance.ClassModule, procName.Value ?? string.Empty, callType switch
            {
                VBCallType.VBLet => RDCore.External.Automation.AutomationInvocation.Let,
                VBCallType.VBSet => RDCore.External.Automation.AutomationInvocation.Set,
                _ => RDCore.External.Automation.AutomationInvocation.Get,
            });

            var indexes = assigns ? args[..^1] : args;
            var collected = new VBResizableArrayValue([(0, indexes.Length - 1)], VBVariantType.TypeInfo);
            for (var index = 0; index < indexes.Length; index++)
            {
                collected.TrySetElement(BindingKnowledge.NewCell(indexes[index]), index);
            }

            IRuntimeValue[] lateArguments = assigns
                ? [new VBRuntimeValue<VBRuntimeArrayValue>(new VBRuntimeArrayValue(collected)), args[^1].RuntimeValue]
                : [new VBRuntimeValue<VBRuntimeArrayValue>(new VBRuntimeArrayValue(collected))];
            var answered = callables.ForMember(lateBound, objectValue.RuntimeValue).Call(session.Symbols.Resolver, lateArguments);
            return answered.IsSuccess
                ? Succeed(answered.Result is null or VBVoidValue ? new VBVariantValue(VBEmptyValue.Empty) : InVariant(answered.Result))
                : RuntimeSemanticsEvaluationResult<VBVariantValue>.Error(answered.ErrorInfo ?? NotImplementedError(nameof(CallByName), "the call had no answer"));
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

        if (callType is VBCallType.VBLet or VBCallType.VBSet
            && candidates.FirstOrDefault(member => member.Kind is SymbolKindExt.Field or SymbolKindExt.Variable) is { } assigned)
        {
            return AssignVariable(objectValue, instance, assigned, callType is VBCallType.VBSet, args);
        }

        if (MemberOf(candidates, callType) is not { } target)
        {
            // a Function asked for as a property is the member, called the wrong way (MS-VBA: 450, Excel-verified, oracle\s40 B22, B28); a name that is no
            // member to be called so at all is 438.
            return callType is VBCallType.VBGet && candidates.Any(member => member is VBFunctionMemberSymbol)
                ? Fail<VBVariantValue>(VBRuntimeErrorId.WrongNumberOfArgumentsOrInvalidPropertyAssignment, $"'{procName.Value}' is a function, and no property to get.")
                : Fail<VBVariantValue>(VBRuntimeErrorId.ObjectDoesntSupportThisPropertyOrMethod, $"The object has no member '{procName.Value}' that can be called as {callType}.");
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

                // what a call written in source gives an omitted parameter: its declared default, or Missing for a Variant that declares none (MS-VBAL
                // §5.3.1.11; MS-VBA, Excel-verified, oracle\s40 B06-B09, B13).
                bound[index] = (session.Symbols.Defaults?.DefaultValueOf(parameter) ?? parameter.ResolvedType.DefaultValue).RuntimeValue;
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

    // VbLet and VbSet of a public variable of the object assign it, as `obj.Name = value` and `Set obj.Name = value` do: the value is the one argument (MS-VBA,
    // Excel-verified, oracle\s40 B01-B05, B15, B18, B20, B21, B23-B27, B30-B32). None is error 446 and more than one 451, as MS-VBA has it; a Let of an object
    // is a Let of its default member's value; a Let into a variable of an object assigns the object's default member, of which Nothing has none (91); a Set
    // into a variable that holds no object is 451, and of what is no object, or an object of another class, 13.
    private RuntimeSemanticsEvaluationResult<VBVariantValue> AssignVariable(
        VBObjectValue owner, IObjectInstance instance, VBTypeMemberSymbol variable, bool isSet, VBVariantValue[] args)
    {
        if (args.Length == 0)
        {
            return Fail<VBVariantValue>(VBRuntimeErrorId.ObjectDoesntSupportNamedArguments, $"'{variable.Name}' was not given the value it is assigned.");
        }

        if (args.Length > 1)
        {
            return Fail<VBVariantValue>(VBRuntimeErrorId.PropertyLetNotDefinedPropertyGetDidNotReturnObject, $"'{variable.Name}' is a variable, and takes no arguments.");
        }

        var declared = variable.ResolvedType;
        var holdsAnObject = declared is VBObjectType or VBClassType;
        var handle = instance.GetValue(variable);
        var value = Unwrapped(args[0]);
        VBTypedValue stored;
        if (isSet)
        {
            if (!holdsAnObject && declared is not VBVariantType)
            {
                return Fail<VBVariantValue>(VBRuntimeErrorId.PropertyLetNotDefinedPropertyGetDidNotReturnObject, $"'{variable.Name}' is a {declared.Name}, which holds no object to Set.");
            }

            if (value is not VBObjectValue assignedObject)
            {
                return Fail<VBVariantValue>(VBRuntimeErrorId.TypeMismatch, $"What is Set into '{variable.Name}' is no object.");
            }

            if (declared is VBClassType { Symbol: var classModule } && !assignedObject.IsNothing()
                && (!session.Symbols.TryGetInstance(assignedObject.Value, out var assignedInstance) || !IsOrImplements(assignedInstance!.ClassModule, classModule, [])))
            {
                return Fail<VBVariantValue>(VBRuntimeErrorId.TypeMismatch, $"'{variable.Name}' is a {declared.Name}, and the object Set into it is not one.");
            }

            stored = declared is VBVariantType ? new VBVariantValue(assignedObject) : assignedObject;
        }
        else if (holdsAnObject)
        {
            // a Let into a variable of an object is a Let of the object's default member.
            var held = declared.CreateValue(handle.ForReading()) as VBObjectValue;
            if (held is null || held.IsNothing())
            {
                return Fail<VBVariantValue>(VBRuntimeErrorId.ObjectVariableOrWithBlockVariableNotSet, $"'{variable.Name}' holds Nothing, which has no default member to assign.");
            }

            return session.Symbols.TryGetInstance(held.Value, out var heldInstance) && ObjectDefaultMember.TryFind(heldInstance!.ClassModule, out var defaultMember)
                ? CallByName(held, new VBStringValue(defaultMember.Name), VBCallType.VBLet, args)
                : Fail<VBVariantValue>(VBRuntimeErrorId.ObjectDoesntSupportThisPropertyOrMethod, $"The object '{variable.Name}' holds has no default member to assign.");
        }
        else
        {
            // a Let of an object is a Let of its default member's value, into a Variant too.
            var assigned = args[0];
            if (value is VBObjectValue)
            {
                var read = ObjectDefaultMember.ValueOf(session, value);
                if (!read.IsSuccess)
                {
                    return RuntimeSemanticsEvaluationResult<VBVariantValue>.Error(read.ErrorInfo!);
                }

                assigned = InVariant(read.Result!);
            }

            var coerced = Coerce(assigned, declared);
            if (!coerced.IsSuccess)
            {
                return RuntimeSemanticsEvaluationResult<VBVariantValue>.Error(coerced.ErrorInfo!);
            }

            stored = coerced.Result!;
        }

        var withEvents = variable.GetProperty(SymbolProperties.WithEvents);
        var previous = holdsAnObject && declared.CreateValue(handle.ForReading()) is VBObjectValue before ? new VBObjectValue(before.Value) : null;
        if (withEvents)
        {
            EventAttachments.Detach(session, owner.Value, variable, previous);
        }

        var heldBefore = ObjectReferences.HeldIn(session, handle);
        handle.Store(session.Symbols.Resolver, SymbolAddressTable.BoxedValue(stored), !stored.IsIndeterminate);
        ObjectReferences.Rebind(session, handle, heldBefore);
        if (withEvents)
        {
            EventAttachments.Attach(session, owner.Value, variable, stored as VBObjectValue);
        }

        return Succeed(new VBVariantValue(VBEmptyValue.Empty));
    }

    // whether a class is, or implements, another.
    private static bool IsOrImplements(VBClassModuleSymbol classModule, VBClassModuleSymbol target, HashSet<string> visited)
        => classModule.Uri.AbsoluteUri == target.Uri.AbsoluteUri
            || visited.Add(classModule.Uri.AbsoluteUri) && classModule.ImplementedInterfaces.Any(implemented => IsOrImplements(implemented, target, visited));

    private static VBTypeMemberSymbol? MemberOf(IReadOnlyList<VBTypeMemberSymbol> candidates, VBCallType callType)
    {
        bool IsMethod(VBTypeMemberSymbol member) => member is VBFunctionMemberSymbol or VBProcedureMemberSymbol
            && member is not (VBPropertyGetMemberSymbol or VBPropertyLetMemberSymbol or VBPropertySetMemberSymbol);

        return callType switch
        {
            VBCallType.VBMethod => candidates.FirstOrDefault(IsMethod) ?? candidates.FirstOrDefault(member => member is VBPropertyGetMemberSymbol),
            // a Function is no property to get: VbGet of one is 450, as MS-VBA has it (Excel-verified, oracle\s40 B22, B28).
            VBCallType.VBGet => candidates.FirstOrDefault(member => member is VBPropertyGetMemberSymbol),
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
            VBNumericType numeric => source => ValueConversions.ToNumeric(source, numeric, session.Environment.Regional),
            VBStringType => source => ValueConversions.ToText(source, session.Environment.Culture, session.Environment.Regional),
            VBBooleanType => source => ValueConversions.ToBoolean(source, session.Environment.Regional),
            VBDateType => source => ValueConversions.ToDate(source, session.Environment.Regional),
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
    /// the queue of the thread the program runs on are processed, which only Windows has either. So are the messages the windows of the program's forms held
    /// while it ran (<see cref="External.Forms.IFormHost.DoEvents"/>): what they raise is handled now, as the answer to the call. Elsewhere there is nothing to yield
    /// to, and it returns at once. The answer is <c>0</c>: the number of open forms, of which there are none.
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBIntegerValue> DoEvents()
    {
        session.Turn.Pump();
        using (session.Turn.Yield())
        {
            _outside.Forms.DoEvents();
        }

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

    private RuntimeSemanticsEvaluationResult<VBTypedValue> WaitOf(VBVariantValue? wait)
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
    public RuntimeSemanticsEvaluationResult<VBObjectValue> CreateObject(VBStringValue objectClass, VBStringValue? serverName = default)
    {
        var progId = objectClass.Value ?? string.Empty;
        if (string.IsNullOrEmpty(serverName?.Value) && NativeClassOf(progId) is { } native)
        {
            return Succeed(NewObjectOf(native));
        }

        return Obtain(new ExternalObjectRequest(ExternalObjectSource.Create, progId, ServerName: serverName?.Value));
    }

    // a class the platform implements itself that answers to a programmatic identifier: the class of the identifier's last part in the standard library, if it
    // declares that identifier (StdLibClassAttribute.ProgId).
    private VBClassModuleSymbol? NativeClassOf(string progId)
    {
        var name = progId[(progId.LastIndexOf('.') + 1)..];
        return name.Length > 0
            && VBProjectSymbol.ResolveQualifiedType(session.Symbols.Resolver, SDK.Runtime.StdLib.StdLibSymbolProvider.LibraryName, name, StaticSymbol.GlobalUri).Symbol
                is VBClassModuleSymbol classModule
            && string.Equals(classModule.GetProperty(SymbolProperties.ProgId), progId, StringComparison.OrdinalIgnoreCase)
            ? classModule
            : null;
    }

    // what New makes of a class of the standard library: an identity, and the record of its instance.
    private VBObjectValue NewObjectOf(VBClassModuleSymbol classModule)
    {
        var objectId = session.Objects.CreateObject();
        session.Symbols.CreateInstance(objectId, classModule);
        return new VBObjectValue(objectId);
    }

    // an object by name, from whatever reaches the machine's servers: through the session's external calls, which the policy over automation sees.
    private RuntimeSemanticsEvaluationResult<VBObjectValue> Obtain(ExternalObjectRequest request)
    {
        if (session.External is not { } external)
        {
            return Fail<VBObjectValue>(VBRuntimeErrorId.ActiveXComponentCantCreateObject, $"{request.Describe()}: nothing here reaches an automation server.");
        }

        var obtained = external.Invoke(ExternalCallRequest.ForObject(request), session.Symbols.Resolver);
        return obtained switch
        {
            { IsSuccess: true, Result: VBObjectValue objectValue } => Succeed(objectValue),
            { IsSuccess: true } => Fail<VBObjectValue>(VBRuntimeErrorId.ActiveXComponentCantCreateObject, $"{request.Describe()} gave no object."),
            _ => RuntimeSemanticsEvaluationResult<VBObjectValue>.Error(obtained.ErrorInfo!),
        };
    }

    // the text of an argument that names a class or a file: Missing is none, Null is error 94.
    private RuntimeSemanticsEvaluationResult<VBObjectValue>? TextOf(VBVariantValue? argument, out string? text)
    {
        text = null;
        if (argument is null || argument.TypedValue is VBMissingValue)
        {
            return null;
        }

        if (argument.TypedValue is VBNullValue)
        {
            return Fail<VBObjectValue>(VBRuntimeErrorId.InvalidUseOfNull, "GetObject: a name is no Null");
        }

        var converted = ValueConversions.ToText(argument.TypedValue, session.Environment.Culture, session.Environment.Regional);
        if (converted is not { IsSuccess: true, Value: VBStringValue value })
        {
            return Fail<VBObjectValue>(converted.Error ?? VBRuntimeErrorId.TypeMismatch, "GetObject: a name is a String");
        }

        text = value.Value ?? string.Empty;
        return null;
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBLongValue> GetAttr(VBStringValue pathName)
    {
        // through the session's file channels, the one seam file I/O goes through (IStdFileSystemModule).
        var path = pathName.Value ?? string.Empty;
        return session.Files.TryGetAttributes(path, out var attributes) is { } failed
            ? RuntimeSemanticsEvaluationResult<VBLongValue>.Error(VBRuntimeErrorInfo.For(failed, default, $"GetAttr({path})"))
            : RuntimeSemanticsEvaluationResult<VBLongValue>.Success(new VBLongValue((int)attributes));
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBObjectValue> GetObject(VBVariantValue? pathName = default, VBVariantValue? className = default)
    {
        if (TextOf(pathName, out var path) is { } badPath)
        {
            return badPath;
        }

        if (TextOf(className, out var progId) is { } badClass)
        {
            return badClass;
        }

        // as MS-VBA answers them (recorded in Excel, oracle\s25 G02, G03): nothing at all is a type mismatch, no file and no class an invalid argument, and
        // no file but a class a new object of it.
        return (path, progId) switch
        {
            (null, null) => Fail<VBObjectValue>(VBRuntimeErrorId.TypeMismatch, "GetObject(): neither a file nor a class"),
            ("", null or "") => Fail<VBObjectValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, "GetObject(\"\"): neither a file nor a class"),
            ("", { } created) => CreateObject(new VBStringValue(created)),
            (null, { } running) => NativeClassOf(running) is not null
                ? Fail<VBObjectValue>(VBRuntimeErrorId.ActiveXComponentCantCreateObject, $"GetObject(, \"{running}\"): no object of it is running")
                : Obtain(new ExternalObjectRequest(ExternalObjectSource.Running, running)),
            ({ } file, var opener) => Obtain(new ExternalObjectRequest(ExternalObjectSource.File, opener is { Length: > 0 } ? opener : null, PathName: file)),
        };
    }

    #endregion

    // what the program was started with, as the host says (ExternalWorld.CommandLine): a String in a Variant, as in MS-VBA.
    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Command() => Succeed(new VBVariantValue(new VBStringValue(_outside.CommandLine)));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> CommandString() => Succeed(new VBStringValue(_outside.CommandLine));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Partition(VBVariantValue number, VBVariantValue start, VBVariantValue stop, VBVariantValue interval)
    {
        // "If any of the parts is Null, Partition returns the data value Null": before what is wrong with the others is (oracle\s27 P03).
        VBVariantValue[] parts = [number, start, stop, interval];
        if (parts.Any(part => Unwrapped(part) is VBNullValue))
        {
            return Succeed(new VBVariantValue(VBNullValue.Null));
        }

        var whole = new long[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            var read = WholeNumberOf(parts[index]);
            if (!read.IsSuccess)
            {
                return RuntimeSemanticsEvaluationResult<VBVariantValue>.Error(read.ErrorInfo!);
            }

            whole[index] = read.Result!.Value;
        }

        var (value, first, last, size) = (whole[0], whole[1], whole[2], whole[3]);
        if (first < 0 || last <= first || size < 1)
        {
            return Fail<VBVariantValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument,
                $"Partition's range starts at {first}, ends at {last}, and is split {size} at a time: it starts at 0 or above, ends after it starts, and is split one or more at a time.");
        }

        // each side of the colon is as wide as the widest number either can be - what is before the range or after it - so that the ranges sort as text
        // (MS-VBA, oracle\s27: the spec's "characters in Stop, plus one" is that for a Start of 0 and a Stop below a power of ten).
        var below = (first - 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var above = (last + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var width = Math.Max(below.Length, above.Length);
        string Side(string text) => text.PadLeft(width);
        string Range(long lower, long upper) => Side(lower.ToString(System.Globalization.CultureInfo.InvariantCulture)) + ":" + Side(upper.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var range = value < first ? Side(string.Empty) + ":" + Side(below)
            : value > last ? Side(above) + ":" + Side(string.Empty)
            : Range(first + (value - first) / size * size, Math.Min(first + (value - first) / size * size + size - 1, last));
        return Succeed(new VBVariantValue(new VBStringValue(range)));
    }

    // a part of Partition, rounded to a whole number as a Long is (half to even): one too large for a Long is a number all the same, past the range.
    private RuntimeSemanticsEvaluationResult<VBLongLongValue> WholeNumberOf(VBVariantValue part)
    {
        var converted = Convert(part, source => ValueConversions.ToNumeric(source, VBDoubleType.TypeInfo, session.Environment.Regional), "Double");
        if (!converted.IsSuccess)
        {
            return RuntimeSemanticsEvaluationResult<VBLongLongValue>.Error(converted.ErrorInfo!);
        }

        var rounded = Math.Round(((VBDoubleValue)converted.Result!).Value, MidpointRounding.ToEven);
        return rounded is >= long.MinValue and <= long.MaxValue
            ? Succeed(new VBLongLongValue((long)rounded))
            : Fail<VBLongLongValue>(VBRuntimeErrorId.Overflow, $"{rounded} is too large a part of a partition.");
    }
}
