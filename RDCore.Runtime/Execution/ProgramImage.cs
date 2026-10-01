using System.Collections;
using System.Collections.Immutable;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Semantics.Instructions;

namespace RDCore.Runtime.Execution;

/// <summary>
/// The code of a session: the lowered body of every procedure of every module that has been loaded into it, keyed by the
/// <see cref="Symbol.SemanticId"/> of the procedure it is the body of.
/// </summary>
/// <remarks>
/// What lets a procedure call another whichever module declares it, and an object of a class run the members of its
/// class: the invoker looks a callee's body up here at call time, so a module loaded after the one that calls it is
/// reached all the same. Loading is by module, and replaces: a module that is read again takes the place of what it
/// was, and a procedure it no longer declares is gone with it.
/// <para>
/// Reads see a consistent snapshot and never block; loads are serialized.
/// </para>
/// </remarks>
public sealed class ProgramImage : IReadOnlyDictionary<SemanticId, InstructionList>
{
    private readonly object _loading = new();
    private volatile ImmutableDictionary<SemanticId, InstructionList> _procedures = ImmutableDictionary<SemanticId, InstructionList>.Empty;
    private ImmutableDictionary<string, ImmutableArray<SemanticId>> _modules = ImmutableDictionary<string, ImmutableArray<SemanticId>>.Empty;

    /// <summary>
    /// Loads the procedures of a module, replacing every one that was loaded for it before.
    /// </summary>
    /// <param name="moduleUri">The <see cref="Symbol.Uri"/> of the module symbol the procedures are declared by.</param>
    /// <param name="procedures">Each procedure's body, keyed by the procedure's symbol.</param>
    public void Load(Uri moduleUri, IEnumerable<KeyValuePair<SemanticId, InstructionList>> procedures)
    {
        var loaded = procedures.ToList();
        lock (_loading)
        {
            var image = _procedures;
            if (_modules.TryGetValue(moduleUri.AbsoluteUri, out var previous))
            {
                image = image.RemoveRange(previous);
            }

            _procedures = image.SetItems(loaded);
            _modules = _modules.SetItem(moduleUri.AbsoluteUri, [.. loaded.Select(procedure => procedure.Key)]);
        }
    }

    /// <summary>
    /// Removes every procedure that was loaded for a module.
    /// </summary>
    /// <returns><see langword="false"/> if nothing was loaded for it.</returns>
    public bool Unload(Uri moduleUri)
    {
        lock (_loading)
        {
            if (!_modules.TryGetValue(moduleUri.AbsoluteUri, out var previous))
            {
                return false;
            }

            _procedures = _procedures.RemoveRange(previous);
            _modules = _modules.Remove(moduleUri.AbsoluteUri);
            return true;
        }
    }

    /// <summary>
    /// Whether anything has been loaded for a module.
    /// </summary>
    public bool IsLoaded(Uri moduleUri) => _modules.ContainsKey(moduleUri.AbsoluteUri);

    /// <inheritdoc/>
    public InstructionList this[SemanticId key] => _procedures[key];

    /// <inheritdoc/>
    public IEnumerable<SemanticId> Keys => _procedures.Keys;

    /// <inheritdoc/>
    public IEnumerable<InstructionList> Values => _procedures.Values;

    /// <inheritdoc/>
    public int Count => _procedures.Count;

    /// <inheritdoc/>
    public bool ContainsKey(SemanticId key) => _procedures.ContainsKey(key);

    /// <inheritdoc/>
    public bool TryGetValue(SemanticId key, out InstructionList value) => _procedures.TryGetValue(key, out value!);

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<SemanticId, InstructionList>> GetEnumerator() => _procedures.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
