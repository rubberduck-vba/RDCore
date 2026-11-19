using System.Runtime.CompilerServices;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// The codes an analyzer declares decide whether it is run: one whose every code is disabled is not. Each analyzer of the platform declares the codes it reports.
/// </summary>
[TestClass]
public sealed class AnalyzerCodesTests
{
    private static IEnumerable<Type> Analyzers()
        => typeof(RDCore.Diagnostics.CoreDiagnosticsApp).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IModuleAnalyzer).IsAssignableFrom(type));

    [TestMethod]
    public void EachAnalyzerOfThePlatform_DeclaresTheCodesItReports()
    {
        // the codes of an analyzer are of its type, and none of what it is built with: it is not built to be asked.
        var undeclared = Analyzers().Where(type => ((IModuleAnalyzer)RuntimeHelpers.GetUninitializedObject(type)).Codes.Count == 0).Select(type => type.Name).ToArray();

        Assert.IsNotEmpty(Analyzers());
        Assert.IsEmpty(undeclared, string.Join(", ", undeclared));
    }
}
