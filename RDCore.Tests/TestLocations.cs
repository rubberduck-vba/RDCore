using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
namespace RDCore.Tests;

internal static class TestLocations
{
    /// For the sake of a test involving a binary operator, the location of the LHS symbol or
    /// in the case of a unary operator, the location of the expression.
    /// </summary>
    /// <remarks>
    /// Consistency with the actual length of the values is not relevant here.
    /// </remarks>
    public static Location TestLocationLHS { get; } = new() { Uri = TestUri.TestBinaryOpLhsUri(), Range = new Range(1, 1, 1, 1) };
    /// <summary>
    /// For the sake of a test, the location of the operator symbol.
    /// </summary>
    /// <remarks>
    /// Consistency with the actual length of the values is not relevant here.
    /// </remarks>
    internal static Location TestLocation { get; } = new() { Uri = TestUri.TestBinaryOpUri(), Range = new Range(1, 2, 1, 2) };
    /// <summary>
    /// For the sake of a test involving a binary operator, the location of the RHS symbol.
    /// </summary>
    /// <remarks>
    /// Consistency with the actual length of the values is not relevant here.
    /// </remarks>
    internal static Location TestLocationRHS { get; } = new() { Uri = TestUri.TestBinaryOpRhsUri(), Range = new Range(1, 3, 1, 3) };
}
