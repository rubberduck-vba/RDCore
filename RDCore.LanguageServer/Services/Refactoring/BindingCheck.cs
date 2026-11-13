using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Platform;

namespace RDCore.LanguageServer.Services.Refactoring;

/// <summary>
/// How an edit changed what a name of the code refers to.
/// </summary>
internal enum BindingChangeKind
{
    /// <summary>A name the edit left as it was refers to something else: <see cref="BindingChange.Was"/> before, <see cref="BindingChange.Is"/> after.</summary>
    Retargeted,

    /// <summary>A name the edit left as it was is no longer a reference: it no longer resolves, or it is gone from the code.</summary>
    Lost,

    /// <summary>A name the edit left as it was is a reference to what is only known when the code runs, where it was known before.</summary>
    Unknown,

    /// <summary>A name the edit wrote, or one that was no reference before: what it refers to is the edit's to say.</summary>
    Written,
}

/// <summary>
/// What an edit changed of a reference: the name, where it is before and after the edit, and what it referred to before and refers to after.
/// </summary>
/// <param name="Kind">How it changed.</param>
/// <param name="Module">The module the name is written in.</param>
/// <param name="Name">The name, as it is written.</param>
/// <param name="Before">Where the name is before the edit; <see langword="null"/> for a name the edit wrote.</param>
/// <param name="After">Where the name is after the edit; <see langword="null"/> for a name that is gone.</param>
/// <param name="Was">What the name referred to before the edit; <see langword="null"/> when it was no reference.</param>
/// <param name="Is">What the name refers to after the edit; <see langword="null"/> when it is no reference.</param>
internal sealed record class BindingChange(
    BindingChangeKind Kind, Uri Module, string Name, SourceRange? Before, SourceRange? After, SemanticId? Was, SemanticId? Is);

/// <summary>
/// An edit of the text of a module: the range of the text it replaces, before the edit, and the text it writes there.
/// </summary>
/// <param name="Range">The range it replaces, empty for an insertion.</param>
/// <param name="NewText">The text it writes.</param>
internal sealed record class ModuleTextEdit(SourceRange Range, string NewText);

/// <summary>
/// What an edit of the code changes of the references of its names, which a refactoring verifies before it is offered: every name of the edited modules,
/// and every reference to one of those names in any module, is bound before the edit and again after it, the way a rename binds it
/// (<see cref="NameOccurrences"/>), and the two are compared.
/// </summary>
/// <remarks>
/// A name the edit left as it was is found where the edits moved it. One that refers to the same thing after the edit is no change; any other is
/// (<see cref="BindingChangeKind"/>). A name inside the text an edit replaced is the edit's own, and is reported as <see cref="BindingChangeKind.Written"/>
/// with what it refers to after the edit, for the refactoring to hold against what it meant to write.
/// </remarks>
internal static class BindingCheck
{
    /// <summary>
    /// The changes an edit makes to the references of the names of the modules it edits.
    /// </summary>
    /// <param name="before">The workspace before the edit.</param>
    /// <param name="after">The workspace after the edit: the same modules, the edited ones parsed from the text the edit leaves.</param>
    /// <param name="edits">The edits of each edited module, by the module's absolute URI; they do not overlap.</param>
    public static IReadOnlyList<BindingChange> Changes(RenameContext before, RenameContext after, IReadOnlyDictionary<string, IReadOnlyList<ModuleTextEdit>> edits)
    {
        // the static pass recurses once per expression level: one large-stack thread for the whole check.
        if (!LargeStackThread.IsCurrent)
        {
            return LargeStackThread.Run(() => Changes(before, after, edits));
        }

        var changes = new List<BindingChange>();
        foreach (var name in NamesOf(before, edits).Union(NamesOf(after, edits), StringComparer.OrdinalIgnoreCase))
        {
            var was = NameOccurrences.Find(before, name, before.Modules);
            var now = NameOccurrences.Find(after, name, after.Modules);
            var known = now.Occurrences.GroupBy(Key).ToDictionary(group => group.Key, group => group.First());
            var unknown = now.Unknown.Select(Key).ToHashSet();
            var kept = new HashSet<(string, SourcePosition)>();

            foreach (var occurrence in was.Occurrences)
            {
                // a name inside the text an edit replaced is not kept: what is written in its place is the edit's.
                if (Moved(before, after, edits, occurrence.Module, occurrence.Range) is not { } moved)
                {
                    continue;
                }

                var key = (occurrence.Module.AbsoluteUri, moved.Start);
                if (known.TryGetValue(key, out var found))
                {
                    kept.Add(key);
                    if (found.Binding != occurrence.Binding)
                    {
                        changes.Add(new(BindingChangeKind.Retargeted, occurrence.Module, name, occurrence.Range, moved, occurrence.Binding, found.Binding));
                    }
                }
                else
                {
                    var kind = unknown.Contains(key) ? BindingChangeKind.Unknown : BindingChangeKind.Lost;
                    changes.Add(new(kind, occurrence.Module, name, occurrence.Range, kind is BindingChangeKind.Unknown ? moved : null, occurrence.Binding, null));
                }
            }

            foreach (var occurrence in now.Occurrences.Where(occurrence => !kept.Contains(Key(occurrence))))
            {
                changes.Add(new(BindingChangeKind.Written, occurrence.Module, name, null, occurrence.Range, null, occurrence.Binding));
            }
        }

        return changes;
    }

    private static (string, SourcePosition) Key(NameOccurrence occurrence) => (occurrence.Module.AbsoluteUri, occurrence.Range.Start);

    // every name the edited modules write: their references, and their declarations, which a module that is not edited may refer to.
    private static IEnumerable<string> NamesOf(RenameContext context, IReadOnlyDictionary<string, IReadOnlyList<ModuleTextEdit>> edits)
    {
        foreach (var module in context.Modules.Where(module => edits.ContainsKey(module.Module.AbsoluteUri)))
        {
            var whole = new SourceRange(SourcePosition.Zero, PositionAt(module.Source, module.Source.Length));
            foreach (var name in NameOccurrences.NamesIn(module, whole))
            {
                yield return name;
            }

            foreach (var declared in NameOccurrences.DeclarationsIn(module, whole))
            {
                if (NameOccurrences.NameAt(module, declared) is { } name)
                {
                    yield return name;
                }
            }
        }
    }

    // where a range of a module is after the edits; null when an edit replaced any of it. A module that is not edited is as it was.
    private static SourceRange? Moved(
        RenameContext before, RenameContext after, IReadOnlyDictionary<string, IReadOnlyList<ModuleTextEdit>> edits, Uri module, SourceRange range)
    {
        if (!edits.TryGetValue(module.AbsoluteUri, out var moduleEdits) || moduleEdits.Count == 0)
        {
            return range;
        }

        var was = before.Modules.First(candidate => candidate.Module.AbsoluteUri == module.AbsoluteUri).Source;
        var now = after.Modules.First(candidate => candidate.Module.AbsoluteUri == module.AbsoluteUri).Source;
        var (start, end) = (OffsetOf(was, range.Start), OffsetOf(was, range.End));
        var shift = 0;
        foreach (var edit in moduleEdits)
        {
            var (from, to) = (OffsetOf(was, edit.Range.Start), OffsetOf(was, edit.Range.End));
            if ((from < end && start < to) || (from == to && start < from && from < end))
            {
                return null;
            }

            if (to <= start)
            {
                shift += edit.NewText.Length - (to - from);
            }
        }

        return new SourceRange(PositionAt(now, start + shift), PositionAt(now, end + shift));
    }

    // the offset of a position in a text, its lines ended by LF or CR LF.
    private static int OffsetOf(string text, SourcePosition position)
    {
        var offset = 0;
        for (var line = 0; line < position.Line; line++)
        {
            var ended = text.IndexOf('\n', offset);
            if (ended < 0)
            {
                return text.Length;
            }

            offset = ended + 1;
        }

        return Math.Min(offset + position.Character, text.Length);
    }

    // the position of an offset of a text.
    private static SourcePosition PositionAt(string text, int offset)
    {
        var line = 0;
        var start = 0;
        for (var at = text.IndexOf('\n'); at >= 0 && at < offset; at = text.IndexOf('\n', at + 1))
        {
            line++;
            start = at + 1;
        }

        return new SourcePosition(line, offset - start);
    }
}
