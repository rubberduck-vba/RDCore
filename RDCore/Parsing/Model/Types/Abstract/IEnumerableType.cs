using RDCore.Parsing.Model.Symbols.Abstract;

namespace RDCore.Parsing.Model.Types.Abstract;

/// <summary>
/// Represents a type that can be iterated in a <c>For Each...Next</c> loop.
/// </summary>
internal interface IEnumerableType
{
    /// <summary>
    /// If <c>true</c>, flags <c>For Each</c> iteration as inefficient, for performance diagnostics.
    /// </summary>
    bool IsArray { get; }
}

internal interface IEnumerableObject : IEnumerableType
{
    VBReturningMember? NewEnumMember { get; }
}