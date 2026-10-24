namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// States that the <c>VBA</c> type library declares a standard-library parameter by reference, though it is not a <c>Variant</c> (whose parameters it
/// declares <c>VARIANT*</c> without being told): a call-site <c>ByVal</c> written for it is a compile error, <em>Type mismatch</em>, as <strong>MS-VBA</strong>
/// reports it (<c>Model.Symbols.Abstract.SymbolProperties.TypeLibraryByRef</c>).
/// </summary>
/// <remarks>
/// It changes nothing of how the argument is passed: the parameter is the <c>ByVal</c> one the platform declares, and takes what it is given as it does. Which
/// parameters are so is recorded in Excel, not read off a type library: <c>Replace</c>'s, whatever their type - its <c>Expression</c>, <c>Find</c> and
/// <c>Start</c> recorded, its others taken to be declared as they are (RD-VBAL §5.6.13.1).
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class StdLibTypeLibraryByRefAttribute : Attribute;
