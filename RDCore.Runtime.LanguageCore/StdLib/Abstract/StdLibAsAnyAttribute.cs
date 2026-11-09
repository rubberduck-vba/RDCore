namespace RDCore.Runtime.LanguageCore.StdLib.Abstract;

/// <summary>
/// States that a standard-library parameter is <c>ByRef … As Any</c>, as a <c>Declare</c>'s can be: it takes a variable of any type but an array, and the
/// member is given where the variable is - its address in the session's memory - rather than what it holds.
/// </summary>
/// <remarks>
/// The parameter is declared as a <see cref="Model.Values.Intrinsic.VBLongLongValue"/>, whatever the width of the environment's pointers: what it is given is
/// <list type="bullet">
/// <item>the address of a variable passed to it;</item>
/// <item>the address of a temporary, for what is no variable (a literal, an expression, what a function returns), which is freed when the statement that
/// passes it is over;</item>
/// <item>the bytes a <c>ByVal</c> argument is stored in, read as a whole number - which is how <strong>MS-VBA</strong> passes it (recorded in Excel):
/// a whole number is itself, a <c>Double</c> its bits, a <c>Variant</c> its <c>VARIANT</c>'s type.</item>
/// </list>
/// <c>VarPtr</c> is declared so, as the <c>VBA</c> type library declares it: <c>VarPtr(Ptr As Any) As LongPtr</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class StdLibAsAnyAttribute : Attribute;
