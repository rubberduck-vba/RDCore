namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// Marks a standard-library <c>Compare</c> parameter that a call omits as taking the <c>Option Compare</c> of the calling module.
/// </summary>
/// <remarks>
/// <strong>MS-VBAL §6.1.2.11</strong> declares each comparing member's <c>Compare</c> as <c>Optional Compare As VbCompareMethod =
/// vbBinaryCompare</c>, and its runtime semantics say "if omitted, it uses the &lt;option-compare-directive&gt; of the calling module". The
/// declaration keeps its C# default of <see cref="VBCompareMethod.VBBinaryCompare"/>, which is what a caller with no calling module - a
/// direct call to the dispatcher - gets. A call from source fills an omitted one at the call site, where the caller's frame is still the
/// current one, with <see cref="VBCompareMethod.VBTextCompare"/> when the caller compares in text mode and
/// <see cref="VBCompareMethod.VBBinaryCompare"/> otherwise, <c>Option Compare Database</c> standing for whatever the environment says it
/// does. The member then never needs to know whether its argument was written.
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class StdLibOptionCompareAttribute : Attribute { }
