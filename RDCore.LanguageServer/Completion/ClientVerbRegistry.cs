using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using RDCore.SDK.Platform.Commands;

namespace RDCore.LanguageServer.Completion;

/// <summary>
/// The verbs of the client's own commands, as it registered them (<c>rdcore/session/commands</c>): what the arguments typed after one are completed by.
/// </summary>
internal interface IClientVerbRegistry
{
    /// <summary>
    /// Registers verbs; one that was registered before is replaced, by its name and by each of its aliases.
    /// </summary>
    /// <param name="verbs">The verbs, described.</param>
    /// <returns>How many verbs are registered now.</returns>
    int Register(IEnumerable<VerbDescriptor> verbs);

    /// <summary>
    /// The verb registered by <paramref name="name"/>, or by an alias of it, in any case.
    /// </summary>
    /// <param name="name">The name or the alias.</param>
    /// <param name="verb">The verb, when there is one.</param>
    bool TryGet(string name, [NotNullWhen(true)] out VerbDescriptor? verb);
}

/// <inheritdoc cref="IClientVerbRegistry"/>
internal sealed class ClientVerbRegistry : IClientVerbRegistry
{
    // by every spelling of each verb: what is typed is a name or an alias.
    private readonly ConcurrentDictionary<string, VerbDescriptor> _bySpelling = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, VerbDescriptor> _byName = new(StringComparer.OrdinalIgnoreCase);

    public int Register(IEnumerable<VerbDescriptor> verbs)
    {
        foreach (var verb in verbs)
        {
            if (_byName.TryGetValue(verb.Name, out var previous))
            {
                foreach (var spelling in previous.Aliases)
                {
                    _bySpelling.TryRemove(spelling, out _);
                }
            }

            _byName[verb.Name] = verb;
            foreach (var spelling in verb.Aliases.Prepend(verb.Name))
            {
                _bySpelling[spelling] = verb;
            }
        }

        return _byName.Count;
    }

    public bool TryGet(string name, [NotNullWhen(true)] out VerbDescriptor? verb) => _bySpelling.TryGetValue(name, out verb);
}
