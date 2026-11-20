namespace RDCore.Runtime.LanguageCore.StdLib.Abstract;

/// <summary>
/// States that a standard-library function is evaluated inline, as <strong>MS-VBA</strong> compiles it (recorded in Excel): an argument that is a call of
/// it is no call, and is evaluated with the arguments that are none, after the calls among them (MS-VBAL §5.3.1.11; RD-VBAL §5.3.1.11).
/// </summary>
/// <remarks>
/// <c>Len</c>, <c>Abs</c> and the conversions (<c>CLng</c>, <c>CStr</c>) are; <c>UCase$</c>, <c>Mid$</c>, <c>Format$</c> and <c>Sqr</c> are calls.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class StdLibInlineAttribute : Attribute;
