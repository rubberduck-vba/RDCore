using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Workspace;

namespace RDCore.Runtime.Symbols;

/// <summary>
/// Produces the project-level conditional-compilation constants a session starts with: the built-in
/// host constants derived from the <see cref="IRuntimeEnvironmentProfile"/>, the <c>.rdproj</c>
/// <c>#Const</c>s, and (highest precedence) <c>--define</c> command-line overrides.
/// </summary>
/// <remarks>
/// ⚖️ GPLv3. Values that are not a recognised literal are skipped.
/// </remarks>
public sealed class ConfigurationSymbolProvider(
    IRuntimeEnvironmentProfile environment,
    RDCoreProject project,
    IReadOnlyDictionary<string, string>? overrides = null) : ISymbolProvider
{
    private static readonly IReadOnlyDictionary<string, string> _noOverrides = new Dictionary<string, string>();

    public IEnumerable<Symbol> ProvideSymbols()
    {
        var defined = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // highest precedence first; the HashSet makes the first definition of a name win.
        foreach (var (name, source) in ByPrecedence())
        {
            if (defined.Add(name) && PrecompilerConstantExpression.TryParse(source, out var value))
            {
                yield return new PrecompilerConstantSymbol(name, value);
            }
        }
    }

    private IEnumerable<(string Name, string Source)> ByPrecedence()
    {
        foreach (var entry in overrides ?? _noOverrides)
        {
            yield return (entry.Key, entry.Value);
        }

        foreach (var entry in project.PrecompilerConstants)
        {
            yield return (entry.Key, entry.Value);
        }

        yield return ("Win16", "0");
        yield return ("Win32", environment.Is64Bit ? "0" : "-1");
        yield return ("Win64", environment.Is64Bit ? "-1" : "0");
        yield return ("Mac", "0");
        yield return ("VBA6", "0");
        yield return ("VBA7", "-1");
    }
}
