using RDCore.External.Automation;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
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
using RDCore.SDK.Runtime.StdLib;
using System.Runtime.CompilerServices;

namespace RDCore.Runtime.Execution.External.Automation;

/// <summary>
/// Reaches the objects of a referenced library - the object model of a host application, <c>Scripting</c>, <c>ADODB</c> - through the automation servers of
/// the machine.
/// </summary>
/// <remarks>
/// <para>
/// The symbols of a library are known from its description, wherever the program is analyzed. Running one is another matter: a call binds late, to a member
/// of an object by its name (<see cref="SymbolProperties.ExternalTarget"/> says which), and an object is a handle the server owns. The session knows it by
/// an identity of its own (<see cref="ISessionExternalObjects"/>), so that counting references, <c>Is</c> and <c>Set x = Nothing</c> are the machinery that
/// already does them; when the last reference goes, the server is let go of.
/// </para>
/// <para>
/// Everything that is platform-specific is behind <see cref="IAutomationServer"/>. A machine that has none leaves this provider unable to run anything, and a
/// program that needs one is told so when it asks, not when it is loaded.
/// </para>
/// <para>
/// 🚧 TODO An object that was returned and never assigned is not let go of until the session is: nothing holds a reference to count down, the same gap a
/// temporary of the workspace's own classes has. Events (<c>WithEvents</c> on a server's object) are not connected.
/// </para>
/// </remarks>
/// <param name="session">The session whose objects these are.</param>
/// <param name="server">What reaches the automation servers of this machine.</param>
public sealed class AutomationCallProvider(IRuntimeSession session, IAutomationServer server) : IExternalCallProvider
{
    private readonly ServerObjects _objects = new(session, server);

    // The enumerator that a server's enumeration member returns is an object of the standard library's IEnumVARIANT, whose members the loop calls by name: those are
    // the library's, and this provider answers them for the enumerators it holds - which is why it comes before the library's own provider.
    private static readonly string MoveNextKey = EnumeratorKey(nameof(IStdEnumVariantClass.MoveNext));
    private static readonly string CurrentKey = EnumeratorKey(nameof(IStdEnumVariantClass.Current));
    private static readonly string ResetKey = EnumeratorKey(nameof(IStdEnumVariantClass.Reset));

    private static string EnumeratorKey(string member)
        => StdLibSymbolReader.ExternalTargetOf(typeof(IStdEnumVariantClass), typeof(IStdEnumVariantClass).GetMethod(member)!);

    // what the last member the enumerator moved to was: IEnumVARIANT hands a member over once, and the loop reads it after.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, StrongBox<object?>> Moved = [];

    // which call it is comes first: whether the servers are there can mean starting what reaches them, which no call that is not theirs should do.
    /// <inheritdoc/>
    public bool CanDispatch(ExternalCallRequest request)
        => (request.IsAutomation || IsEnumeratorMember(request)) && server.IsAvailable;

    private bool IsEnumeratorMember(ExternalCallRequest request)
        => request.Member.GetProperty(SymbolProperties.ExternalTarget) is { } key && (key == MoveNextKey || key == CurrentKey || key == ResetKey)
        && request.Arguments is [var receiver, ..] && IdentityOf(receiver) is { } identity && HandleOf(identity) is not null;

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult Dispatch(ExternalCallRequest request, ISymbolResolver resolver)
    {
        try
        {
            return request.IsCreation ? Create(request) : IsEnumeratorMember(request) ? Enumerate(request) : Call(request, resolver);
        }
        catch (AutomationException failure)
        {
            return RuntimeSemanticsEvaluationResult.Error(AutomationErrors.ToError(failure, request.CallSite, request.Describe()));
        }
    }

    private RuntimeSemanticsEvaluationResult Create(ExternalCallRequest request)
    {
        var classModule = request.Creates!;
        if (classModule.GetProperty(SymbolProperties.ProgId) is not { Length: > 0 } progId)
        {
            return RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(
                VBRuntimeErrorId.ActiveXComponentCantCreateObject, request.CallSite,
                $"{request.Describe()} cannot be created: the library names no programmatic identifier for '{classModule.Name}'."));
        }

        if (IdentityOf(request.Arguments[0]) is not { } identity)
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        // the program waits for the server, and gives its turn up while it does: what the server raises meanwhile is answered inside the call.
        object created;
        using (session.Turn.Yield())
        {
            created = server.CreateObject(progId);
        }

        session.ExternalObjects.Bind(identity, server, created);
        return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
    }

    // MoveNext, Current and Reset of an enumerator a server made.
    private RuntimeSemanticsEvaluationResult Enumerate(ExternalCallRequest request)
    {
        var enumerator = HandleOf(IdentityOf(request.Arguments[0])!.Value)!;
        var key = request.Member.GetProperty(SymbolProperties.ExternalTarget);

        if (key == MoveNextKey)
        {
            bool moved;
            object? current;
            using (session.Turn.Yield())
            {
                moved = server.MoveNext(enumerator, out current);
            }

            Moved.AddOrUpdate(enumerator, new StrongBox<object?>(current));
            return RuntimeSemanticsEvaluationResult.Success(new VBBooleanValue(moved));
        }

        if (key == ResetKey)
        {
            using (session.Turn.Yield())
            {
                server.Reset(enumerator);
            }

            return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
        }

        // the member is whatever the server handed over: a value, or an object of a class of the libraries - Excel's sheets are enumerated as sheets.
        var last = Moved.TryGetValue(enumerator, out var box) ? box.Value : null;
        return RuntimeSemanticsEvaluationResult.Success(
            AutomationMarshaller.FromAutomation(last, VBVariantType.TypeInfo, (value, declared) => Wrap(value, declared, session.Symbols.Resolver)));
    }

    private RuntimeSemanticsEvaluationResult Call(ExternalCallRequest request, ISymbolResolver resolver)
    {
        var member = request.Member;
        var (name, invocation) = ParseTarget(member.GetProperty(SymbolProperties.ExternalTarget)!);
        var parameters = RuntimeProcedureInvoker.GetParameters(member);

        if (parameters is not [{ Name: "Me" }, ..] || IdentityOf(request.Arguments[0]) is not { } identity)
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        if (!session.ExternalObjects.TryGet(identity, out var owner, out var target) || !ReferenceEquals(owner, server))
        {
            return RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(
                VBRuntimeErrorId.ObjectVariableOrWithBlockVariableNotSet, request.CallSite, $"{request.Describe()} was called on no object."));
        }

        var passed = Arguments(request, parameters, resolver);

        // the array the server is given is the one it writes the arguments passed by reference back into.
        var values = passed.Values.ToArray();
        object? result;
        using (session.Turn.Yield())
        {
            result = server.Invoke(target, name, invocation, values, [.. passed.ByReference], session.Environment.Culture);
        }

        WriteBack(passed, values, resolver);

        // a value is assigned and a member that returns nothing has returned it: neither has anything to read.
        if (invocation is AutomationInvocation.Let or AutomationInvocation.Set)
        {
            return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
        }

        // MS-VBAL §5.4.2.4: what the enumeration member returns is the enumerator the loop drives, and its class is the standard library's.
        var enumeration = member.TryGetProperty(SymbolProperties.UserMemId, out var userMemId) && userMemId == WellKnownDispIds.NewEnum;
        return RuntimeSemanticsEvaluationResult.Success(AutomationMarshaller.FromAutomation(
            result, member.ResolvedType, (value, declared) => enumeration ? WrapEnumerator(value, resolver) : Wrap(value, declared, resolver)));
    }

    private VBTypedValue WrapEnumerator(object enumerator, ISymbolResolver resolver) => _objects.WrapEnumerator(enumerator, resolver);

    private sealed class PassedArguments
    {
        public List<object?> Values { get; } = [];

        public List<bool> ByReference { get; } = [];

        // where the server wrote an argument that was passed by reference, so the variable it names can be told: the type the variable is declared, the
        // argument's position among the values, the address of the variable, and what it held.
        public List<(VBType Declared, int Position, MemoryAddress Address, object? Sent)> Variables { get; } = [];
    }

    // the arguments in the order the server takes them. The caller has Let-coerced each to the parameter's declared type, which is what recovers the
    // typed value from the storage the argument arrives as; an argument passed by reference arrives as the address of the variable it names.
    private PassedArguments Arguments(ExternalCallRequest request, System.Collections.Immutable.ImmutableArray<VBParameterSymbol> parameters, ISymbolResolver resolver)
    {
        var passed = new PassedArguments();
        for (var index = 1; index < parameters.Length; index++)
        {
            var parameter = parameters[index];
            var argument = request.Arguments[index];

            if (parameter is ParamArrayParameterSymbol)
            {
                // the arguments the caller collected for a ParamArray are the rest of the call.
                if (parameter.ResolvedType.CreateValue(new ValueBindingHandle(argument)) is VBArrayValue collected)
                {
                    for (var element = 0; element < collected.Length; element++)
                    {
                        passed.Values.Add(AutomationMarshaller.ToAutomation(collected.ElementAt(element)!, HandleOf));
                        passed.ByReference.Add(false);
                    }
                }

                continue;
            }

            MemoryAddress? address = null;
            VBType? referenced = null;
            while (argument is VBRuntimeReference reference && resolver.TryRead(reference.Value, out var cell))
            {
                address ??= reference.Value;
                referenced ??= reference.DeclaredType;
                argument = cell.Value;
            }

            // MS-VBAL §5.3.1.11: a variable passed to a Variant parameter is the variable, of the type it is declared - which is what the server is given, by reference.
            var declared = referenced ?? parameter.ResolvedType;

            // MS-VBAL §5.2.3.4: a member of an enumeration is a Long, and so is a value of its type to a server, which knows it as one.
            var typed = declared is VBEnumType
                ? new VBLongValue(Convert.ToInt32(argument.BoxedValue, System.Globalization.CultureInfo.InvariantCulture))
                : declared.CreateValue(new ValueBindingHandle(argument));
            var converted = AutomationMarshaller.ToAutomation(typed, HandleOf, omitEmpty: parameter.IsOptional && parameter.ResolvedType is VBVariantType);

            // only a variable can be written to; one of a type the server cannot write a result of back to (an object, an array) is not told.
            var writable = address is { } && parameter.ParameterKind is ParameterKind.ImplicitByRef or ParameterKind.ExplicitByRef
                && converted is not (Array or null) and not Type and not System.Runtime.InteropServices.DispatchWrapper;
            if (writable)
            {
                passed.Variables.Add((declared, passed.Values.Count, address!.Value, converted));
            }

            passed.Values.Add(converted);
            passed.ByReference.Add(writable);
        }

        return passed;
    }

    // MS-VBAL §5.3.1.11: a reference parameter is the variable the argument names, so a server that wrote to it has written to the variable.
    private void WriteBack(PassedArguments passed, object?[] values, ISymbolResolver resolver)
    {
        foreach (var (variableType, position, address, sent) in passed.Variables)
        {
            var now = values[position];
            if (Equals(sent, now) || !resolver.TryRead(address, out var cell))
            {
                continue;
            }

            try
            {
                var written = AutomationMarshaller.FromAutomation(now, variableType, (value, declared) => Wrap(value, declared, resolver));
                cell.SetValue(resolver, written.RuntimeValue);
            }
            catch (AutomationException)
            {
                // a value the server left in an argument that is not one the variable can hold is not one the program asked for.
            }
        }
    }

    private VBTypedValue Wrap(object value, VBType declared, ISymbolResolver resolver) => _objects.Wrap(value, declared, resolver);

    private object? HandleOf(VBRuntimeObjectId identity) => _objects.HandleOf(identity);

    private static VBRuntimeObjectId? IdentityOf(IRuntimeValue value)
        => value is VBRuntimeValue<VBRuntimeObjectId> identity ? identity.StoredValue : null;

    // `Library.Class.Member/kind`, as the reader of the description stamped it.
    private static (string Name, AutomationInvocation Invocation) ParseTarget(string target)
    {
        var slash = target.LastIndexOf('/');
        var path = slash < 0 ? target : target[..slash];
        var invocation = slash < 0 ? string.Empty : target[(slash + 1)..];

        return (path[(path.LastIndexOf('.') + 1)..], invocation switch
        {
            "get" => AutomationInvocation.Get,
            "let" => AutomationInvocation.Let,
            "set" => AutomationInvocation.Set,
            _ => AutomationInvocation.Method,
        });
    }
}
