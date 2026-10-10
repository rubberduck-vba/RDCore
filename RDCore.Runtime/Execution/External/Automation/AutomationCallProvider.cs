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
using RDCore.SDK.Runtime.Shared;

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
    /// <inheritdoc/>
    public bool CanDispatch(ExternalCallRequest request) => request.IsAutomation && server.IsAvailable;

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult Dispatch(ExternalCallRequest request, ISymbolResolver resolver)
    {
        try
        {
            return request.IsCreation ? Create(request) : Call(request, resolver);
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

        session.ExternalObjects.Bind(identity, server, server.CreateObject(progId));
        return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
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
        var result = server.Invoke(target, name, invocation, values, [.. passed.ByReference]);

        WriteBack(passed, values, parameters, resolver);

        // a value is assigned and a member that returns nothing has returned it: neither has anything to read.
        return RuntimeSemanticsEvaluationResult.Success(invocation is AutomationInvocation.Let or AutomationInvocation.Set
            ? VBVoidValue.Void
            : AutomationMarshaller.FromAutomation(result, member.ResolvedType, (value, declared) => Wrap(value, declared, resolver)));
    }

    private sealed class PassedArguments
    {
        public List<object?> Values { get; } = [];

        public List<bool> ByReference { get; } = [];

        // where the server wrote an argument that was passed by reference, so the variable it names can be told: the parameter, the argument's
        // position among the values, the address of the variable, and what it held.
        public List<(int Parameter, int Position, MemoryAddress Address, object? Sent)> Variables { get; } = [];
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
            while (argument is VBRuntimeReference reference && resolver.TryRead(reference.Value, out var cell))
            {
                address ??= reference.Value;
                argument = cell.Value;
            }

            var typed = parameter.ResolvedType.CreateValue(new ValueBindingHandle(argument));
            var converted = AutomationMarshaller.ToAutomation(typed, HandleOf, omitEmpty: parameter.IsOptional && parameter.ResolvedType is VBVariantType);

            // only a variable can be written to; one of a type the server cannot write a result of back to (an object, an array) is not told.
            var writable = address is { } && parameter.ParameterKind is ParameterKind.ImplicitByRef or ParameterKind.ExplicitByRef
                && converted is not (Array or null) and not Type and not System.Runtime.InteropServices.DispatchWrapper;
            if (writable)
            {
                passed.Variables.Add((index, passed.Values.Count, address!.Value, converted));
            }

            passed.Values.Add(converted);
            passed.ByReference.Add(writable);
        }

        return passed;
    }

    // MS-VBAL §5.3.1.11: a reference parameter is the variable the argument names, so a server that wrote to it has written to the variable.
    private void WriteBack(
        PassedArguments passed, object?[] values, System.Collections.Immutable.ImmutableArray<VBParameterSymbol> parameters, ISymbolResolver resolver)
    {
        foreach (var (parameter, position, address, sent) in passed.Variables)
        {
            var now = values[position];
            if (Equals(sent, now) || !resolver.TryRead(address, out var cell))
            {
                continue;
            }

            try
            {
                var written = AutomationMarshaller.FromAutomation(now, parameters[parameter].ResolvedType, (value, declared) => Wrap(value, declared, resolver));
                cell.SetValue(resolver, written.RuntimeValue);
            }
            catch (AutomationException)
            {
                // a value the server left in an argument that is not one the variable can hold is not one the program asked for.
            }
        }
    }

    // the object the language holds for an object a server returned. The same server object is the same object to the program, which is what `Is` asks.
    private VBTypedValue Wrap(object value, VBType declared, ISymbolResolver resolver)
    {
        if (session.ExternalObjects.TryFind(server, value, out var existing))
        {
            return new VBObjectValue(existing);
        }

        var classModule = ClassOf(value, declared, resolver)
            ?? throw new AutomationException(unchecked((int)0x80020005), "The class of an object the server returned is not one of the referenced libraries'.");

        var identity = session.Objects.CreateObject();
        session.Symbols.CreateInstance(identity, classModule);
        session.ExternalObjects.Bind(identity, server, value);
        return new VBObjectValue(identity);
    }

    // The class an object is an instance of: the one its member declares to return, as the project has it now, or - for a member that is declared to return an
    // Object, as `ActiveSheet` is - the one the server says it is, which is how late binding finds the members.
    private VBClassModuleSymbol? ClassOf(object value, VBType declared, ISymbolResolver resolver)
    {
        if (declared is VBClassType { Symbol: { } named })
        {
            return Current(resolver, named.GetProperty(SymbolProperties.Library), named.Name) ?? named;
        }

        if (server.ClassNameOf(value) is not { Length: > 0 } qualified)
        {
            return null;
        }

        // `Excel._Worksheet`: the interface of a class is named for it, with a leading underscore the library's description leaves out.
        var dot = qualified.IndexOf('.');
        var library = dot < 0 ? null : qualified[..dot];
        var className = (dot < 0 ? qualified : qualified[(dot + 1)..]).TrimStart('_');
        return Current(resolver, library, className);
    }

    private static VBClassModuleSymbol? Current(ISymbolResolver resolver, string? library, string name)
        => VBProjectSymbol.ResolveQualifiedType(resolver, library, name, StaticSymbol.GlobalUri).Symbol as VBClassModuleSymbol;

    private object? HandleOf(VBRuntimeObjectId identity)
        => session.ExternalObjects.TryGet(identity, out var owner, out var handle) && ReferenceEquals(owner, server) ? handle : null;

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
