using RDCore.Tests.Cli;

namespace RDCore.Tests.Semantics.Static;

/// <summary>
/// A member of a module written with a type-declaration character, qualified by its module or its library, is the member of that name, and not the one
/// without it (<strong>MS-VBAL §5.6.12</strong>): <c>VBA.Left$</c> is <c>Left$</c>, which returns a String.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.12 Member Access Expressions")]
public sealed class QualifiedTypeCharacterBindingTests
{
    [TestMethod]
    public async Task AQualifiedStringForm_IsBoundToIt_AndIsAString()
    {
        var program = ModuleWorkspace.StandardModule("Program", "Option Explicit", "Public Sub Main()", "    Debug.Print VBA.Left$(\"abc\", 1)", "End Sub");
        var payload = await ModuleWorkspace.SemanticsAsync([], program);

        // the member access and the call of it are both bound to the function.
        var facts = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program").Procedures
            .SelectMany(procedure => procedure.Expressions)
            .Where(fact => fact.Binding?.Fragment is { } bound
                && (bound.EndsWith("Left$", StringComparison.Ordinal) || bound.EndsWith("Left", StringComparison.Ordinal)))
            .ToArray();

        Assert.IsNotEmpty(facts);
        Assert.IsTrue(facts.All(fact => fact.Binding!.Fragment.EndsWith("Left$", StringComparison.Ordinal) && fact.DeclaredType == "String"),
            string.Join("; ", facts.Select(fact => $"{fact.Binding} {fact.DeclaredType}")));
    }

    [TestMethod]
    public async Task AQualifiedNameWithoutTheCharacter_IsTheVariantForm()
    {
        var program = ModuleWorkspace.StandardModule("Program", "Option Explicit", "Public Sub Main()", "    Debug.Print Strings.Left(\"abc\", 1)", "End Sub");
        var payload = await ModuleWorkspace.SemanticsAsync([], program);

        var facts = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program").Procedures
            .SelectMany(procedure => procedure.Expressions)
            .Where(fact => fact.Binding?.Fragment is { } bound && bound.EndsWith("Left", StringComparison.Ordinal))
            .ToArray();

        Assert.IsNotEmpty(facts);
        Assert.IsTrue(facts.All(fact => fact.DeclaredType == "Variant"), string.Join("; ", facts.Select(fact => $"{fact.Binding} {fact.DeclaredType}")));
    }
}
