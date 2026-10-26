using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using System.Runtime.CompilerServices;

namespace RDCore.Runtime.Execution.External.Forms;

/// <summary>
/// The forms of a session: those that are loaded, in the order they were, and those that were unloaded - what a UserForm is to the program between the moment it
/// is created and the moment it is let go of.
/// </summary>
/// <remarks>
/// <para>
/// A form is loaded when its instance is created: on <c>New</c>, and on the first reference to its default instance, whatever the reference is (Excel-measured,
/// <c>oracle\s41</c> L01-L08). It is unloaded by <c>Unload</c>, and then it is an object whose own variables are still there but whose form is gone: a member its
/// designer declares fails, and nothing loads it again (L15, L51, L52). It is let go of either way, and its <c>Terminate</c> runs once - at <c>Unload</c>, or when
/// the last reference to a form that was never unloaded goes (L14, L53).
/// </para>
/// <para>
/// The session outlives the pipelines that run its programs, so what it knows of its forms is kept by the session, not by a pipeline.
/// </para>
/// </remarks>
internal sealed class SessionForms
{
    private static readonly ConditionalWeakTable<IRuntimeSession, SessionForms> Sessions = [];

    private readonly object _gate = new();
    private readonly List<VBRuntimeObjectId> _loaded = [];
    private readonly HashSet<VBRuntimeObjectId> _unloaded = [];
    private readonly Dictionary<VBRuntimeObjectId, IBindingHandle> _held = [];

    /// <summary>
    /// The identity of the session's <c>UserForms</c> collection: one object, the same every time it is asked for (<c>oracle\s41</c> U02). Its instance is made
    /// by whatever first asks for it.
    /// </summary>
    public VBRuntimeObjectId? Collection { get; set; }

    /// <summary>
    /// Holds a form the collection loaded (<c>UserForms.Add</c>), which stays loaded with nothing else referring to it (U05), until it is unloaded.
    /// </summary>
    /// <param name="form">The form.</param>
    /// <param name="holder">The reference the collection holds it by.</param>
    public void Hold(VBRuntimeObjectId form, IBindingHandle holder)
    {
        lock (_gate)
        {
            _held[form] = holder;
        }
    }

    /// <summary>
    /// Lets go of a form the collection held, when it is unloaded.
    /// </summary>
    /// <param name="form">The form.</param>
    /// <param name="holder">The reference the collection held it by.</param>
    /// <returns>Whether the collection held it.</returns>
    public bool LetGo(VBRuntimeObjectId form, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IBindingHandle? holder)
    {
        lock (_gate)
        {
            return _held.Remove(form, out holder);
        }
    }

    /// <summary>The forms of <paramref name="session"/>.</summary>
    public static SessionForms Of(IRuntimeSession session) => Sessions.GetValue(session, _ => new SessionForms());

    /// <summary>The forms that are loaded, in the order they were.</summary>
    public IReadOnlyList<VBRuntimeObjectId> Loaded
    {
        get
        {
            lock (_gate)
            {
                return [.. _loaded];
            }
        }
    }

    /// <summary>A form is loaded: its instance was created.</summary>
    public void Load(VBRuntimeObjectId form)
    {
        lock (_gate)
        {
            if (!_loaded.Contains(form))
            {
                _loaded.Add(form);
            }
        }
    }

    /// <summary>Whether a form was unloaded.</summary>
    public bool IsUnloaded(VBRuntimeObjectId form)
    {
        lock (_gate)
        {
            return _unloaded.Contains(form);
        }
    }

    /// <summary>A form is unloaded: it is no longer loaded, and its object is one whose form is gone.</summary>
    public void Unload(VBRuntimeObjectId form)
    {
        lock (_gate)
        {
            _ = _loaded.Remove(form);
            _ = _unloaded.Add(form);
        }
    }

    /// <summary>
    /// A form's object is let go of, and forgotten.
    /// </summary>
    /// <returns>Whether the form was still loaded, and is terminated now; <see langword="false"/> for one that was unloaded, and terminated then.</returns>
    public bool Release(VBRuntimeObjectId form)
    {
        lock (_gate)
        {
            if (_unloaded.Remove(form))
            {
                return false;
            }

            return _loaded.Remove(form);
        }
    }
}

/// <summary>
/// The event procedures of a form's own code: <c>UserForm_Initialize</c>, <c>UserForm_QueryClose</c>, <c>UserForm_Terminate</c>, by the name VBA gives them
/// whatever the form is named.
/// </summary>
internal static class FormHandlers
{
    /// <summary>The <c>CloseMode</c> of a form <c>Unload</c> unloads (<strong>MS-VBAL §6.1.1.13</strong> <c>vbFormCode</c>).</summary>
    public const short FormCode = 1;

    /// <summary>
    /// Runs a form's handler of an event that has no arguments, if its code has one.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="bindings">What calls a procedure of the session's code.</param>
    /// <param name="form">The form's instance.</param>
    /// <param name="name">The event: <c>Initialize</c>, <c>Terminate</c>.</param>
    public static RuntimeSemanticsEvaluationResult Raise(IRuntimeSession session, ICallableBindingFactory bindings, VBRuntimeObjectId form, string name)
        => HandlerOf(session, form, name) is { } handler
            ? bindings.ForMember(handler).Call(session.Symbols.Resolver, [new VBObjectValue(form).RuntimeValue])
            : RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);

    /// <summary>
    /// Asks a form whether it may close (<c>UserForm_QueryClose(Cancel, CloseMode)</c>): the handler gets <c>Cancel</c> by reference, and a form whose handler
    /// leaves it anything but 0 stays loaded. A form whose code has no handler closes.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="bindings">What calls a procedure of the session's code.</param>
    /// <param name="form">The form's instance.</param>
    /// <param name="closeMode">Why it closes (<c>VbQueryClose</c>).</param>
    /// <param name="cancelled">Whether the handler cancelled it.</param>
    public static RuntimeSemanticsEvaluationResult QueryClose(
        IRuntimeSession session, ICallableBindingFactory bindings, VBRuntimeObjectId form, short closeMode, out bool cancelled)
    {
        cancelled = false;
        if (HandlerOf(session, form, "QueryClose") is not { } handler)
        {
            return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
        }

        // MS-VBAL §5.4.2.20: an argument passed by reference is a location the handler can write to, which is where the cancellation is read from.
        var parameters = RuntimeProcedureInvoker.GetParameters(handler).Where(parameter => parameter.Name != "Me").ToArray();
        var values = new[] { new VBIntegerValue(0), new VBIntegerValue(closeMode) };
        var temporaries = new List<MemoryAddress>();
        var arguments = new List<IRuntimeValue> { new VBObjectValue(form).RuntimeValue };
        MemoryAddress? cancel = null;
        try
        {
            for (var index = 0; index < parameters.Length; index++)
            {
                var value = index < values.Length ? values[index] : new VBIntegerValue(0);
                if (RuntimeProcedureInvoker.IsByRef(parameters[index].ParameterKind)
                    && session.Storage.TryAllocate(VBIntegerType.TypeInfo.DefaultValue.Size, new ValueBindingHandle(value.RuntimeValue), out var address))
                {
                    temporaries.Add(address);
                    cancel ??= index == 0 ? address : null;
                    arguments.Add(new VBRuntimeReference(address));
                }
                else
                {
                    arguments.Add(value.RuntimeValue);
                }
            }

            var handled = bindings.ForMember(handler).Call(session.Symbols.Resolver, [.. arguments]);
            if (!handled.IsSuccess)
            {
                return handled;
            }

            cancelled = cancel is { } location && session.Symbols.Resolver.TryRead(location, out var cell)
                && VBIntegerType.TypeInfo.CreateValue(new ValueBindingHandle(cell.Value)) is VBIntegerValue { Value: not 0 };
            return handled;
        }
        finally
        {
            foreach (var address in temporaries)
            {
                _ = session.Storage.TryDeallocate(address);
            }
        }
    }

    // UserForm_<event>: VBA names a form's event procedures after the UserForm, not after the form.
    private static VBTypeMemberSymbol? HandlerOf(IRuntimeSession session, VBRuntimeObjectId form, string name)
        => session.Symbols.TryGetInstance(form, out var live)
            ? live.ClassModule.Members.OfType<VBTypeMemberSymbol>().FirstOrDefault(member =>
                member is VBProcedureMemberSymbol && string.Equals(member.Name, $"UserForm_{name}", StringComparison.OrdinalIgnoreCase))
            : null;
}
