using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.Runtime.StdLib;

/// <summary>
/// An implementation of a standard-library class whose instances have state of their own, and so has to know which of them a call is on.
/// </summary>
/// <remarks>
/// The library's implementations are one object for each class, written without the instance they are called on: the error object has no storage of
/// its own, being a view of the session's. A <c>Collection</c> is not: each has its members. <see cref="StdLibDispatcher"/> sets
/// <see cref="Receiver"/> to the object the member is called on, immediately before it invokes the member, and a member reads it for the length of its own call.
/// </remarks>
internal interface IStdLibReceiverBound
{
    /// <summary>
    /// The object the member being invoked is called on, or <see langword="null"/> for a call that is not on one.
    /// </summary>
    VBRuntimeObjectId? Receiver { get; set; }
}
