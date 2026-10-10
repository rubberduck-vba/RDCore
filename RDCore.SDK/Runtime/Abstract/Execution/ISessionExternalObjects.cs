using RDCore.SDK.Model.Values.Runtime;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// The objects of a session that something outside the workspace owns: an Excel <c>Workbook</c>, a <c>Scripting.Dictionary</c>.
/// </summary>
/// <remarks>
/// An external object is an <em>opaque handle owned by its provider</em>. The session knows it by the same identity as any other object
/// (<see cref="VBRuntimeObjectId"/>), so that references to it are counted, compared with <c>Is</c> and released by the machinery that already does those
/// things for the objects of the workspace; what stands behind the identity is the provider's own business, and nothing here looks inside it. Only arguments
/// and results ever cross to a provider, never an object graph.
/// <para>
/// ⚖️<strong>RDCore</strong> provides implementations of this interface <strong>licensed under GPLv3</strong>.
/// </para>
/// </remarks>
public interface ISessionExternalObjects
{
    /// <summary>
    /// Makes <paramref name="handle"/>, which <paramref name="owner"/> holds, what <paramref name="objectId"/> stands for.
    /// </summary>
    /// <param name="objectId">The identity of the object, minted by <see cref="ISessionObjects.CreateObject"/>.</param>
    /// <param name="owner">The provider that holds the handle, and is told when the object is let go of.</param>
    /// <param name="handle">Whatever the provider identifies the object by.</param>
    void Bind(VBRuntimeObjectId objectId, IExternalObjectOwner owner, object handle);

    /// <summary>
    /// Gets the provider and the handle behind an object, if it is an external one.
    /// </summary>
    /// <param name="objectId">The identity of the object.</param>
    /// <param name="owner">The provider that holds the handle.</param>
    /// <param name="handle">The handle.</param>
    bool TryGet(VBRuntimeObjectId objectId, [NotNullWhen(true)] out IExternalObjectOwner? owner, [NotNullWhen(true)] out object? handle);

    /// <summary>
    /// Gets the object that <paramref name="handle"/> is already bound to, if there is one: what makes two results that are the same external object the
    /// same object to a program, which is what <c>Is</c> asks.
    /// </summary>
    /// <param name="owner">The provider that holds the handle.</param>
    /// <param name="handle">The handle, compared by reference.</param>
    /// <param name="objectId">The identity of the object.</param>
    bool TryFind(IExternalObjectOwner owner, object handle, out VBRuntimeObjectId objectId);

    /// <summary>
    /// Lets go of the external object behind <paramref name="objectId"/>, if it has one: its provider is told, and the object is no longer known here.
    /// </summary>
    /// <param name="objectId">The identity of the object that has no reference left.</param>
    void Release(VBRuntimeObjectId objectId);

    /// <summary>
    /// Lets go of every external object: the objects of a program that ended, or of a session that is over.
    /// </summary>
    void ReleaseAll();
}

/// <summary>
/// The holder of external objects: the provider that created them and can let them go.
/// </summary>
public interface IExternalObjectOwner
{
    /// <summary>
    /// Lets go of an object this provider held for a session, which has no reference to it left.
    /// </summary>
    /// <remarks>
    /// Nothing is reported back: a release has no operation to fail, and an object that cannot be released is the provider's to log.
    /// </remarks>
    /// <param name="handle">The handle the provider bound.</param>
    void Release(object handle);
}
