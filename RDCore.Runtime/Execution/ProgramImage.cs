using System.Collections;
using System.Collections.Immutable;
using System.Text.Json.Serialization.Metadata;
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
    /// The bodies of the procedures that were loaded for a module.
    /// </summary>
    /// <param name="moduleUri">The <see cref="Symbol.Uri"/> of the module symbol.</param>
    /// <returns>None when nothing has been loaded for it.</returns>
    public IReadOnlyList<InstructionList> BodiesOf(Uri moduleUri)
    {
        ImmutableDictionary<SemanticId, InstructionList> procedures;
        ImmutableDictionary<string, ImmutableArray<SemanticId>> modules;
        lock (_loading)
        {
            (procedures, modules) = (_procedures, _modules);
        }

        return modules.TryGetValue(moduleUri.AbsoluteUri, out var keys) ? [.. keys.Select(key => procedures[key])] : [];
    }

    /// <summary>
    /// What the code of each module is, as a value that is the same for the same code: two images with the same fingerprint of a module run the same program
    /// for that module, and one that differs does not.
    /// </summary>
    /// <returns>A hash of the lowered procedures of each module that is loaded, by the address of the module.</returns>
    /// <remarks>
    /// A program that waits at a <c>Stop</c> is resumed only on the code it was suspended with. The fingerprint is taken when it stops and again when it is
    /// asked to go on: what the analysis pass loaded meanwhile is of no matter if the code came out the same, and is the next run's if it did not.
    /// <para>
    /// The locations of the code are not part of it: a blank line inserted above a procedure, or a comment edited, leaves the program the same program. What the
    /// program reports from then on is a place in the source as it was when the code was loaded.
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
                Append(hash, FingerprintOf(procedures[key]));
            }

            fingerprints[module] = Convert.ToHexString(hash.GetHashAndReset());
        }

        return fingerprints.ToImmutable();
    }

    /// <summary>
    /// What the code of each procedure is, as <see cref="Fingerprints"/> says it of each module.
    /// </summary>
    /// <returns>A hash of the lowered body of each procedure that is loaded, by the address of the procedure.</returns>
    /// <remarks>
    /// A program that waits is resumed on the procedures it was suspended with: one that was added since is no change to a program that could not have called it
    /// (the immediate statement typed at a break is one, and so is a procedure typed in while it waits), and one that is changed or gone is.
    /// </remarks>
    public ImmutableDictionary<string, string> ProcedureFingerprints()
    {
        ImmutableDictionary<SemanticId, InstructionList> procedures;
        lock (_loading)
        {
            procedures = _procedures;
        }

        return procedures.ToImmutableDictionary(procedure => procedure.Key.ToString(), procedure => FingerprintOf(procedure.Value));
    }

    private static string FingerprintOf(InstructionList body)
    {
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        foreach (var instruction in body.Items)
        {
            Append(hash, $"{instruction.Offset}|{instruction.Kind}|{instruction.Target}|{string.Join(',', instruction.Targets)}|{instruction.Else}|{instruction.End}|{instruction.Matching}|{instruction.EnclosingWith}");
            if (instruction.Node is { } node)
            {
                Append(hash, System.Text.Json.JsonSerializer.Serialize<RDCore.SDK.Model.AST.Abstract.SyntaxNode>(node, LocationFree));
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    // the options a syntax node travels with, less every property that says where in the source it was written.
    private static readonly System.Text.Json.JsonSerializerOptions LocationFree = CreateLocationFreeOptions();

    private static System.Text.Json.JsonSerializerOptions CreateLocationFreeOptions()
    {
        var options = new System.Text.Json.JsonSerializerOptions(RDCore.SDK.Model.AST.Abstract.SyntaxNodeJson.Options);
        options.TypeInfoResolver = options.TypeInfoResolver!.WithAddedModifier(typeInfo =>
        {
            foreach (var property in typeInfo.Properties.Where(property => IsLocation(property.PropertyType)).ToList())
            {
                _ = typeInfo.Properties.Remove(property);
            }
        });

        return options;

        static bool IsLocation(Type type)
            => (Nullable.GetUnderlyingType(type) ?? type) is var underlying
                && (underlying == typeof(RDCore.SDK.Model.Source.SourceLocation) || underlying == typeof(RDCore.SDK.Model.Source.SourceRange));
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
