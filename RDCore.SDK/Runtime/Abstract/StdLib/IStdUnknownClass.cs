namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// <c>IUnknown</c>: the root interface every object is an instance of.
/// </summary>
/// <remarks>
/// It has no members VBA source can call, and exists to be nameable: an enumeration member declares what it returns as one
/// (<c>Property Get NewEnum() As IUnknown</c> with <c>VB_UserMemId = -4</c>, <strong>MS-VBAL §5.4.2.4</strong>), which is how every VBA class that wraps a collection
/// is written. An object of any class is compatible with it in a <c>Set</c> assignment, as one is with <c>Object</c>.
/// </remarks>
[StdLibClass("IUnknown", IsCreatable = false)]
public interface IStdUnknownClass
{
}
