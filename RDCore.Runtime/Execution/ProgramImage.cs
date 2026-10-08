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
    /// What the semantic analysis pass found out about the code of the session: the model of each module, as of the last time it was loaded.
    /// </summary>
    public SemanticModelStore Semantics { get; } = new();

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

    /// <summary>
    /// What the code of each module is, as a value that is the same for the same code: two images with the same fingerprint of a module run the same program
    /// for that module, and one that differs does not.
    /// </summary>
    /// <returns>A hash of the lowered procedures of each module that is loaded, by the address of the module.</returns>
    /// <remarks>
    /// A program that waits at a <c>Stop</c> is resumed only on the code it was suspended with. The fingerprint is taken when it stops and again when it is
    /// asked to go on: what the analysis pass loaded meanwhile is of no matter if the code came out the same, and is the next run's if it did not.
    /// <para>
    /// The locations of the code are part of it for now, so that a blank line inserted above a procedure is a change; the program waits at an instruction that
    /// has a place in the source, and the place is where the person looking at it expects to be.
    /// </para>
    /// </remarks>
    public ImmutableDictionary<string, string> Fingerprints()
    {
        ImmutableDictionary<SemanticId, InstructionList> procedures;
        ImmutableDictionary<string, ImmutableArray<SemanticId>> modules;
        lock (_loading)
        {
            (procedures, modules) = (_procedures, _modules);
        }

        var fingerprints = ImmutableDictionary.CreateBuilder<string, string>();
        foreach (var (module, keys) in modules)
        {
            using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            foreach (var key in keys.OrderBy(key => key.ToString(), StringComparer.Ordinal))
            {
                Append(hash, key.ToString());
                foreach (var instruction in procedures[key].Items)
                {
                    Append(hash, $"{instruction.Offset}|{instruction.Kind}|{instruction.Target}|{string.Join(',', instruction.Targets)}|{instruction.Else}|{instruction.End}|{instruction.Matching}|{instruction.EnclosingWith}");
                    if (instruction.Node is { } node)
                    {
                        Append(hash, RDCore.SDK.Platform.Protocol.PlatformJson.Serialize<RDCore.SDK.Model.AST.Abstract.SyntaxNode>(node));
                    }
                }
            }

            fingerprints[module] = Convert.ToHexString(hash.GetHashAndReset());
        }

        return fingerprints.ToImmutable();
    }

    private static void Append(System.Security.Cryptography.IncrementalHash hash, string text)
        => hash.AppendData(System.Text.Encoding.UTF8.GetBytes(text + "\n"));

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
