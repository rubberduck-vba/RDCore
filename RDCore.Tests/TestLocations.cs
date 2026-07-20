using RDCore.SDK.Model.Source;
namespace RDCore.Tests;

internal static class TestLocations
{
    /// For the sake of a test involving a binary operator, the location of the LHS symbol or
    /// in the case of a unary operator, the location of the expression.
    /// </summary>
    /// <remarks>
    /// Consistency with the actual length of the values is not relevant here.
    /// </remarks>
    public static SourceLocation TestLocationLHS { get; } = new() { Uri = TestUri.TestBinaryOpLhsUri(), Range = new SourceRange(1, 1, 1, 1) };
    /// <summary>
    /// For the sake of a test, the location of the operator symbol.
    /// </summary>
    /// <remarks>
    /// Consistency with the actual length of the values is not relevant here.
    /// </remarks>
    internal static SourceLocation TestLocation { get; } = new() { Uri = TestUri.TestBinaryOpUri(), Range = new SourceRange(1, 2, 1, 2) };
    /// <summary>
    /// For the sake of a test involving a binary operator, the location of the RHS symbol.
    /// </summary>
    /// <remarks>
    /// Consistency with the actual length of the values is not relevant here.
    /// </remarks>
    internal static SourceLocation TestLocationRHS { get; } = new() { Uri = TestUri.TestBinaryOpRhsUri(), Range = new SourceRange(1, 3, 1, 3) };
}
