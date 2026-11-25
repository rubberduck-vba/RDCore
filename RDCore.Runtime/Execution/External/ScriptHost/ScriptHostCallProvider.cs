using RDCore.Runtime.Execution.External.Automation;
using RDCore.Runtime.LanguageCore.Errors;
using RDCore.Runtime.Semantics.Conversion;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.Execution.External.ScriptHost;

/// <summary>
/// Runs Windows Script Host's <c>WScript</c> (of <c>IHost.Global</c>) and the members of the objects it gives, which the platform holds itself: what they
/// say of the script comes from the session's <see cref="SessionScript"/>.
/// </summary>
/// <remarks>
/// <para>
/// The script runs as <c>cscript.exe</c> runs it. <c>Echo</c> writes its arguments to the program's output, separated by a space, each as
/// <c>VariantChangeType</c> writes it as text: a <c>Boolean</c> is <c>-1</c> or <c>0</c>, <c>Null</c> is <c>null</c>, and an array or an object raises
/// error 13. <c>StdOut</c> writes to the program's output and <c>StdErr</c> to its error output; reading either raises error 54.
/// </para>
/// <para>
/// <c>Quit</c> ends the program, whose process ends with the code it is given (<see cref="ISessionHalt.RequestEnd"/>), and with <c>0</c> without one.
/// <c>Sleep</c> waits the milliseconds it is given, or until the program is stopped.
/// </para>
/// <para>
/// <c>Arguments</c> are the arguments the script was started with. An argument that starts with <c>/</c> is also named: its name is up to the first
/// <c>:</c>, and its value is the rest, or <c>Empty</c> when it has no <c>:</c>. <c>Named</c> finds the first argument of a name, without regard to case.
/// </para>
/// <para>
/// <c>Name</c> is <c>Windows Script Host</c> and <c>Version</c> is <c>10.0</c>. <c>FullName</c> and <c>Path</c> are the executable and the folder of the
/// process the program runs in. <c>Interactive</c> is whether the program runs attended (<see cref="IRuntimeSession.RunsUnattended"/>) until the program
/// assigns it.
/// </para>
/// </remarks>
/// <param name="session">The session whose script asks.</param>
/// <param name="cancellation">Stops a <c>Sleep</c> when the program is stopped.</param>
public sealed class ScriptHostCallProvider(IRuntimeSession session, CancellationToken cancellation = default) : IExternalCallProvider, IExternalObjectOwner
{
    private const string Library = "IHost";
    private const string GlobalKey = "IHost.Global.WScript/get";
    private const string MemberPrefix = "IHost.";

    private const string HostName = "Windows Script Host";
    private const string HostVersion = "10.0";

    /// <inheritdoc/>
    public bool CanDispatch(ExternalCallRequest request)
        => !request.IsCreation
            && request.Member.GetProperty(SymbolProperties.ExternalTarget) is { } key
            && (key == GlobalKey || key.StartsWith(MemberPrefix, StringComparison.Ordinal) && ObjectOf(request) is not null);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult Dispatch(ExternalCallRequest request, IRuntimeSymbolResolver resolver)
    {
        var key = request.Member.GetProperty(SymbolProperties.ExternalTarget)!;
        if (key == GlobalKey)
        {
            return Given(ScriptObjectKind.Host, resolver);
        }

        var slash = key.LastIndexOf('/');
        var name = key[(key.LastIndexOf('.', slash) + 1)..slash].ToUpperInvariant();
        var kind = key[(slash + 1)..];
        var target = ObjectOf(request)!;
        return target.Kind switch
        {
            ScriptObjectKind.Host => Host(request, resolver, target.Script, name, kind),
            ScriptObjectKind.Arguments => Arguments(request, resolver, target.Script, name, kind),
            ScriptObjectKind.Named => Named(request, resolver, target.Script, name, kind),
            ScriptObjectKind.Unnamed => Unnamed(request, resolver, target.Script, name, kind),
            _ => Stream(request, resolver, target.Kind, name),
        };
    }

    /// <inheritdoc/>
    public void Release(object handle)
    {
        // the script is the session's, which outlives its objects.
        if (handle is ScriptObject released)
        {
            released.Script.Released(released.Kind);
        }
    }

    private RuntimeSemanticsEvaluationResult Host(ExternalCallRequest request, IRuntimeSymbolResolver resolver, SessionScript script, string name, string kind)
        => (name, kind) switch
        {
            ("ECHO", "method") => Echo(request, resolver),
            ("QUIT", "method") => Quit(request, resolver),
            ("SLEEP", "method") => Sleep(request, resolver),
            ("ARGUMENTS", "get") => Given(ScriptObjectKind.Arguments, resolver),
            ("STDOUT", "get") => Given(ScriptObjectKind.StdOut, resolver),
            ("STDERR", "get") => Given(ScriptObjectKind.StdErr, resolver),
            ("SCRIPTNAME", "get") => Success(new VBStringValue(System.IO.Path.GetFileName(script.FullName))),
            ("SCRIPTFULLNAME", "get") => Success(new VBStringValue(script.FullName)),
            ("NAME", "get") => Success(new VBStringValue(HostName)),
            ("VERSION", "get") => Success(new VBStringValue(HostVersion)),
            ("FULLNAME", "get") => Success(new VBStringValue(Environment.ProcessPath ?? string.Empty)),
            ("PATH", "get") => Success(new VBStringValue(System.IO.Path.GetDirectoryName(Environment.ProcessPath) ?? string.Empty)),
            ("INTERACTIVE", "get") => Success(script.Interactive ?? !session.RunsUnattended ? VBBooleanValue.True : VBBooleanValue.False),
            ("INTERACTIVE", "let") => Assigned(request, resolver, value => script.Interactive = value is VBBooleanValue { Value.StoredValue: not 0 }),
            _ => NotImplemented(request, name),
        };

    // Echo: each argument as text, separated by a space, and a line break after them; no argument writes an empty line.
    private RuntimeSemanticsEvaluationResult Echo(ExternalCallRequest request, IRuntimeSymbolResolver resolver)
    {
        var texts = new List<string>();
        foreach (var item in Items(request, resolver))
        {
            if (EchoText(item) is not { } text)
            {
                return Error(VBRuntimeErrorId.TypeMismatch, request, "Echo writes text, numbers, dates, Boolean values, Empty and Null");
            }

            texts.Add(text);
        }

        session.Output.Write(string.Join(' ', texts));
        session.Output.WriteLine();
        return Done;
    }

    // VariantChangeType to a BSTR, which writes a Boolean as a number.
    private string? EchoText(VBTypedValue item) => Unwrapped(item) switch
    {
        VBBooleanValue boolean => (bool)boolean.Value ? "-1" : "0",
        VBNullValue => "null",
        null or VBArrayValue or VBObjectValue => null,
        { } value => ValueConversions.ToText(value, session.Environment.Culture, session.Environment.Regional)
            is { IsSuccess: true, Value: VBStringValue text }
            ? text.Value ?? string.Empty
            : null,
    };

    // Quit: the program ends, and its process with the code it is given, which the caller made a Long.
    private RuntimeSemanticsEvaluationResult Quit(ExternalCallRequest request, IRuntimeSymbolResolver resolver)
    {
        var code = Parameters(request, resolver) is [_, VBLongValue { Value: var exitCode }, ..] ? (int)exitCode : 0;
        session.Halt.RequestEnd(code, request.CallSite);
        return RuntimeSemanticsEvaluationResult.InternalError();
    }

    private RuntimeSemanticsEvaluationResult Sleep(ExternalCallRequest request, IRuntimeSymbolResolver resolver)
    {
        if (Parameters(request, resolver) is not [_, VBLongValue { Value: var milliseconds }, ..] || milliseconds < 0)
        {
            return Error(VBRuntimeErrorId.InvalidProcedureCallOrArgument, request, "Sleep waits a number of milliseconds that is not negative");
        }

        cancellation.WaitHandle.WaitOne((int)milliseconds);
        return Done;
    }

    // Arguments: Item, its default member, and Count, of every argument; Named and Unnamed views of them.
    private RuntimeSemanticsEvaluationResult Arguments(ExternalCallRequest request, IRuntimeSymbolResolver resolver,
        SessionScript script, string name, string kind)
        => (name, kind) switch
        {
            ("ITEM", "method") => Indexed(request, resolver, script.Arguments, value => new VBStringValue(value)),
            ("COUNT", "method") or ("LENGTH", "get") => Success(new VBLongValue(script.Arguments.Count)),
            ("NAMED", "get") => Given(ScriptObjectKind.Named, resolver),
            ("UNNAMED", "get") => Given(ScriptObjectKind.Unnamed, resolver),
            _ => NotImplemented(request, name),
        };

    private RuntimeSemanticsEvaluationResult Named(ExternalCallRequest request, IRuntimeSymbolResolver resolver,
        SessionScript script, string name, string kind)
    {
        var named = script.Arguments.Where(argument => argument.StartsWith('/')).Select(argument => argument[1..]).ToList();
        switch (name, kind)
        {
            case ("COUNT", "get") or ("LENGTH", "get"):
                return Success(new VBLongValue(named.Count));
            case ("ITEM", "get") or ("EXISTS", "method"):
                var wanted = Parameters(request, resolver) is [_, VBStringValue { Value: var text }, ..] ? text ?? string.Empty : string.Empty;
                var found = named.FirstOrDefault(argument => NameOf(argument).Equals(wanted, StringComparison.OrdinalIgnoreCase));
                if (name is "EXISTS")
                {
                    return Success(found is null ? VBBooleanValue.False : VBBooleanValue.True);
                }

                var colon = found?.IndexOf(':') ?? -1;
                return Success(colon < 0 ? VBEmptyValue.Empty : new VBStringValue(found![(colon + 1)..]));
            default:
                return NotImplemented(request, name);
        }
    }

    private static string NameOf(string named) => named.IndexOf(':') is var colon and >= 0 ? named[..colon] : named;

    private RuntimeSemanticsEvaluationResult Unnamed(ExternalCallRequest request, IRuntimeSymbolResolver resolver,
        SessionScript script, string name, string kind)
    {
        var unnamed = script.Arguments.Where(argument => !argument.StartsWith('/')).ToList();
        return (name, kind) switch
        {
            ("ITEM", "get") => Indexed(request, resolver, unnamed, value => new VBStringValue(value)),
            ("COUNT", "get") or ("LENGTH", "get") => Success(new VBLongValue(unnamed.Count)),
            _ => NotImplemented(request, name),
        };
    }

    // the item at a zero-based index; error 9 outside the list.
    private RuntimeSemanticsEvaluationResult Indexed(ExternalCallRequest request, IRuntimeSymbolResolver resolver, IReadOnlyList<string> items,
        Func<string, VBTypedValue> value)
        => Parameters(request, resolver) is [_, VBLongValue { Value: var index }, ..] && index >= 0 && index < items.Count
            ? Success(value(items[(int)index]))
            : Error(VBRuntimeErrorId.SubscriptOutOfRange, request, $"there are {items.Count} arguments");

    // StdOut and StdErr: Write, WriteLine and WriteBlankLines; a member that reads raises error 54, as it does of a stream that is only written.
    private RuntimeSemanticsEvaluationResult Stream(ExternalCallRequest request, IRuntimeSymbolResolver resolver, ScriptObjectKind stream, string name)
    {
        var parameters = Parameters(request, resolver);
        switch (name)
        {
            case "WRITE" when parameters is [_, VBStringValue { Value: var text }, ..]:
                Write(stream, text ?? string.Empty, lines: 0);
                return Done;
            case "WRITELINE":
                Write(stream, parameters is [_, VBStringValue { Value: var line }, ..] ? line ?? string.Empty : string.Empty, lines: 1);
                return Done;
            case "WRITEBLANKLINES" when parameters is [_, VBLongValue { Value: var lines }, ..]:
                if (lines < 0)
                {
                    return Error(VBRuntimeErrorId.InvalidProcedureCallOrArgument, request, "WriteBlankLines writes a number of lines that is not negative");
                }

                Write(stream, string.Empty, (int)lines);
                return Done;
            case "READ" or "READLINE" or "READALL" or "SKIP" or "SKIPLINE" or "ATENDOFSTREAM" or "ATENDOFLINE":
                return Error(VBRuntimeErrorId.BadFileMode, request, "the stream is only written");
            default:
                return NotImplemented(request, name);
        }
    }

    private void Write(ScriptObjectKind stream, string text, int lines)
    {
        if (stream is ScriptObjectKind.StdErr)
        {
            var error = SessionScript.Of(session).Error;
            error.Write(text);
            for (var line = 0; line < lines; line++)
            {
                error.WriteLine();
            }

            return;
        }

        session.Output.Write(text);
        for (var line = 0; line < lines; line++)
        {
            session.Output.WriteLine();
        }
    }

    // the session's object of a kind: the one the program holds, or a new one when nothing holds it.
    private RuntimeSemanticsEvaluationResult Given(ScriptObjectKind kind, IRuntimeSymbolResolver resolver)
    {
        var script = SessionScript.Of(session);
        if (script.TryGetObject(kind, out var held) && session.ExternalObjects.TryGet(held, out var owner, out var handle) && ReferenceEquals(owner, this)
            && handle is ScriptObject { Kind: var heldKind } && heldKind == kind)
        {
            return Success(new VBObjectValue(held));
        }

        if (ServerObjects.Current(resolver, Library, ClassOf(kind)) is not { } classModule)
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        var identity = session.Objects.CreateObject();
        session.Symbols.CreateInstance(identity, classModule);
        session.ExternalObjects.Bind(identity, this, new ScriptObject(script, kind));
        script.Gave(kind, identity);
        return Success(new VBObjectValue(identity));
    }

    private static string ClassOf(ScriptObjectKind kind) => kind switch
    {
        ScriptObjectKind.Host => "IHost_Class",
        ScriptObjectKind.Arguments => "IArguments_Class",
        ScriptObjectKind.Named => "IWSHNamedArguments_Class",
        ScriptObjectKind.Unnamed => "IWSHUnnamedArguments_Class",
        _ => "TextStream",
    };

    // the object the receiver of a member's call is, when it is one of these.
    private ScriptObject? ObjectOf(ExternalCallRequest request)
        => request.Arguments is [VBRuntimeValue<VBRuntimeObjectId> { StoredValue: var receiver }, ..]
            && session.ExternalObjects.TryGet(receiver, out var owner, out var handle) && ReferenceEquals(owner, this) && handle is ScriptObject target
            ? target
            : null;

    // the items of Echo's ParamArray, which the caller collected into one array.
    private static IEnumerable<VBTypedValue> Items(ExternalCallRequest request, IRuntimeSymbolResolver resolver)
    {
        var parameters = RuntimeProcedureInvoker.GetParameters(request.Member);
        if (parameters.Length < 2 || request.Arguments.Length < 2
            || parameters[1].ResolvedType.CreateValue(new ValueBindingHandle(request.Arguments[1])) is not VBArrayValue items)
        {
            yield break;
        }

        for (var index = 0; index < items.Length; index++)
        {
            if (items.ElementAt(index) is { } item)
            {
                yield return item;
            }
        }
    }

    // the arguments as the types their parameters declare, which the caller Let-coerced them to: the receiver first.
    private static VBTypedValue?[] Parameters(ExternalCallRequest request, IRuntimeSymbolResolver resolver)
    {
        var parameters = RuntimeProcedureInvoker.GetParameters(request.Member);
        return [.. request.Arguments.Select((argument, index)
            => index < parameters.Length ? Unwrapped(Read(argument, parameters[index].ResolvedType, resolver)) : null)];
    }

    private static VBTypedValue? Read(IRuntimeValue argument, VBType declared, IRuntimeSymbolResolver resolver)
    {
        while (argument is VBRuntimeReference reference && resolver.TryRead(reference, declared, out var cell))
        {
            argument = cell.Value;
        }

        return declared.CreateValue(new ValueBindingHandle(argument));
    }

    private static VBTypedValue? Unwrapped(VBTypedValue? value)
    {
        while (value is VBVariantValue variant)
        {
            value = variant.TypedValue;
        }

        return value;
    }

    private static RuntimeSemanticsEvaluationResult Assigned(ExternalCallRequest request, IRuntimeSymbolResolver resolver, Action<VBTypedValue> assign)
    {
        if (Parameters(request, resolver) is not [_, { } value, ..])
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        assign(value);
        return Done;
    }

    private static RuntimeSemanticsEvaluationResult Done => RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);

    private static RuntimeSemanticsEvaluationResult Success(VBTypedValue value) => RuntimeSemanticsEvaluationResult.Success(value);

    private static RuntimeSemanticsEvaluationResult NotImplemented(ExternalCallRequest request, string member)
        => Error(VBRuntimeErrorId.ObjectDoesntSupportThisAction, request, $"{member} is not implemented for Windows Script Host");

    private static RuntimeSemanticsEvaluationResult Error(VBRuntimeErrorId error, ExternalCallRequest request, string detail)
        => RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(error, request.CallSite, $"{request.Describe()}: {detail}"));
}
