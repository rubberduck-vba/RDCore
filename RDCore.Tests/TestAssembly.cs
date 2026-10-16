using RDCore.Runtime.Semantics;

namespace RDCore.Tests;

/// <summary>
/// What holds for every test of the assembly.
/// </summary>
[TestClass]
public static class TestAssembly
{
    /// <summary>
    /// Every expression without an effect is evaluated from its bound tree and from its syntax tree, and the two must agree (IR lowering S0.8a, parity
    /// mode): the whole suite is the parity test of the binder.
    /// </summary>
    [AssemblyInitialize]
    public static void Initialize(TestContext context)
        => RuntimeExpressionEvaluator.ComparesBoundEvaluation = true;
}
