using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents the <c>Nothing</c> <see cref="VBObjectValue"/> literal.
/// </summary>
public sealed record class VBNothingValue() : VBObjectValue(default(VBRuntimeObjectId)) { }