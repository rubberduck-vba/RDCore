using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Execution;

/// <summary>
/// Keeps the reference counts of the session's objects (<see cref="ISessionObjects"/>) in step with the variables that
/// hold them, which is what makes an object's <c>Terminate</c> run when its last variable lets it go
/// (<strong>MS-VBAL §5.3.1.10</strong>).
/// </summary>
/// <remarks>
/// A reference is held by the <see cref="IBindingHandle"/> of the variable that stores it, by identity, and is
/// released only by that same handle. Whatever is not counted is never released: an object that is held by something
/// this does not see (a function's result, an array element, a field of another object) is not destroyed early by
/// it, only later than it could have been — an object nothing counts keeps living until the session ends.
/// <para>
/// 🚧 TODO only a <c>Set</c> to a variable and the end of the activation that declared it are counted. An object
/// stored in a field of another object, an array element or a <c>Variant</c>, one handed back as a function's result
/// and one a <c>ByRef</c> parameter's assignment replaces are not, and neither does destroying the holder of an
/// object release it; a module-level variable is released by nothing but its own <c>Set</c>.
/// </para>
/// </remarks>
internal static class ObjectReferences
{
    /// <summary>
    /// Records that <paramref name="holder"/> now holds <paramref name="assigned"/> where it held
    /// <paramref name="previous"/>, and lets the previous object go.
    /// </summary>
    /// <param name="session">The session whose objects they are.</param>
    /// <param name="holder">The handle of the variable the assignment wrote to.</param>
    /// <param name="previous">What the variable held before; <see langword="null"/> or <c>Nothing</c> when nothing.</param>
    /// <param name="assigned">What it holds now; <see langword="null"/> or <c>Nothing</c> when nothing.</param>
    public static void Rebind(IRuntimeSession session, IBindingHandle holder, VBObjectValue? previous, VBObjectValue? assigned)
    {
        // a handle that only aliases another variable's storage is not the holder: the one it points to is.
        if (holder is ReferenceBindingHandle)
        {
            return;
        }

        VBRuntimeObjectId? before = previous is { } held && !held.IsNothing() ? held.Value : null;
        VBRuntimeObjectId? after = assigned is { } given && !given.IsNothing() ? given.Value : null;
        if (before == after)
        {
            return;
        }

        if (after is { } acquired)
        {
            session.Objects.AddRef(acquired, holder);
        }

        if (before is { } released && session.Objects.IsHeldBy(released, holder))
        {
            session.ReleaseReference(released, holder);
        }
    }

    /// <summary>
    /// Lets go of what <paramref name="holder"/> holds because the variable it belongs to ceases to exist.
    /// </summary>
    /// <param name="session">The session whose objects they are.</param>
    /// <param name="holder">The handle of the variable.</param>
    /// <param name="held">What the variable holds.</param>
    /// <param name="exceptFor">An object that outlives the variable because something else is taking it: a function's result.</param>
    public static void Release(IRuntimeSession session, IBindingHandle holder, VBObjectValue? held, VBObjectValue? exceptFor = null)
    {
        if (held is not { } value || value.IsNothing() || !session.Objects.IsHeldBy(value.Value, holder))
        {
            return;
        }

        if (exceptFor is { } kept && !kept.IsNothing() && kept.Value == value.Value)
        {
            return;
        }

        session.ReleaseReference(value.Value, holder);
    }
}
