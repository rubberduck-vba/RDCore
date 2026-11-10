namespace RDCore.Runtime.LanguageCore.StdLib.Abstract;

/// <summary>
/// States that a standard-library parameter is <c>ByRef</c>, as the specification declares it, without the C# signature being a <c>ref</c> one: a variable
/// passed to it is passed by reference, and the member is told what the variable is declared as.
/// </summary>
/// <remarks>
/// A <c>Variant</c> parameter given a variable of another declared type takes it with <see cref="Model.Values.Intrinsic.VBVariantValue.ReferencedType"/>
/// set, which is how MS-VBA's <c>VT_BYREF</c> reaches a member that tells it apart: <c>IsMissing</c> of an empty <c>Dim e() As Variant</c> is
/// <c>False</c>, where <c>IsMissing(Array())</c> is <c>True</c> (<strong>MS-VBAL §6.1.2.7.1.6</strong>, recorded in Excel).
/// <para>
/// 👉 Only the reading half of a <c>ByRef</c> parameter: what the member assigns to it is not written back, the parameter not being a <c>ref</c> one. A member
/// that writes to its argument declares a C# <c>ref</c> parameter instead.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class StdLibByRefAttribute : Attribute;
