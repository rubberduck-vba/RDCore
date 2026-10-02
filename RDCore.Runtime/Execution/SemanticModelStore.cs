using System.Collections.Immutable;
using RDCore.SDK.Semantics;

namespace RDCore.Runtime.Execution;

/// <summary>
/// What the semantic analysis pass found out about the modules of a session: the model of each module as of the last time its code was loaded.
/// </summary>
/// <remarks>
/// The model of a module is kept whether or not its code could be loaded: what is wrong with it is what a diagnostics extension is asking after. A module that is
/// loaded again takes the place of its model. Reads see a consistent snapshot and never block.
/// </remarks>
public sealed class SemanticModelStore
{
    private readonly object _storing = new();
    private volatile ImmutableDictionary<string, ModuleSemanticModel> _models = ImmutableDictionary<string, ModuleSemanticModel>.Empty;

    /// <summary>
    /// Keeps a module's model, replacing the one it had.
    /// </summary>
    /// <param name="model">The model of a module.</param>
    public void Store(ModuleSemanticModel model)
    {
        lock (_storing)
        {
            _models = _models.SetItem(model.Module.AbsoluteUri, model);
        }
    }

    /// <summary>
    /// The model of a module, if its code has been analyzed.
    /// </summary>
    /// <param name="module">The <see cref="RDCore.SDK.Model.Symbols.Abstract.Symbol.Uri"/> of the module.</param>
    /// <param name="model">The model.</param>
    public bool TryGet(Uri module, out ModuleSemanticModel model)
    {
        var found = _models.TryGetValue(module.AbsoluteUri, out var stored);
        model = stored!;
        return found;
    }

    /// <summary>
    /// The model of every module whose code has been analyzed.
    /// </summary>
    public IReadOnlyCollection<ModuleSemanticModel> All => _models.Values.ToList();
}
