namespace RDCore.Runtime.LanguageCore.StdLib.Abstract;

/// <summary>
/// States that a standard-library <c>ParamArray</c> is given its elements as the variables they refer to: an element whose argument is a variable is a
/// <see cref="Model.Values.Intrinsic.VBVariantValue"/> bound to the element's <see cref="Model.Values.Bindings.VariantReferenceBindingHandle"/>, which the
/// member can pass on by reference. Any other <c>ParamArray</c> is given its elements' values.
/// </summary>
/// <remarks>
/// <strong>MS-VBAL §5.3.1.5</strong>: a param array "behaves as if it was an array whose elements were 'byref' &lt;positional-param&gt; elements whose
/// declared types were Variant". A member that only reads its arguments, such as <c>Array</c> or <c>Choose</c>, is given values it can keep. A member that
/// passes its arguments on to another, as <c>CallByName</c> does, is declared with this attribute: a <c>ByRef</c> parameter of the member it calls writes
/// back to the caller's variable.
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class StdLibParamArrayOfVariablesAttribute : Attribute;
