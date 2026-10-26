using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace RDCore.CLI.App.Console;

/// <summary>
/// A completion list as a dropdown at the console shows it: the items of the list that go with what is typed, one of them selected, and the few of them in view.
/// </summary>
/// <remarks>
/// <para>
/// The list is the whole list the language server answered with for a word (<see cref="CompletionList.IsIncomplete"/> is not), and it is narrowed here as the word
/// is typed: the items whose label starts with what is typed come first, then the ones that only have it in them, each in the order the server ranked them in
/// (<see cref="CompletionItem.SortText"/>). Case is not what tells a name from another in VBA (<strong>MS-VBAL §3.3.5.1</strong>), and it is not here either.
/// </para>
/// <para>
/// The selection is the first item when the list is narrowed - or the item whose label is what is typed, when there is one - and moves with the arrow keys and
/// the page keys; the view scrolls to keep it in sight.
/// </para>
/// </remarks>
public sealed class ConsoleCompletionList
{
    /// <summary>
    /// How many items are in view at a time, unless said otherwise: a few, which is what is read of a list - the rest is narrowed to as more is typed, and the
    /// list is drawn again at each key.
    /// </summary>
    public const int DefaultHeight = 6;

    private readonly IReadOnlyList<CompletionItem> _all;
    private List<CompletionItem> _shown = [];

    /// <summary>
    /// Creates a list.
    /// </summary>
    /// <param name="items">The items the language server answered with.</param>
    /// <param name="height">How many items are in view at a time.</param>
    public ConsoleCompletionList(IEnumerable<CompletionItem> items, int height = DefaultHeight)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        _all = [.. items.OrderBy(item => item.SortText ?? item.Label, StringComparer.Ordinal)];
        Height = height;
        Filter(string.Empty);
    }

    /// <summary>How many items are in view at a time, at the most.</summary>
    public int Height { get; }

    /// <summary>The items that go with what is typed, in the order they are shown.</summary>
    public IReadOnlyList<CompletionItem> Items => _shown;

    /// <summary>The index of the selected item in <see cref="Items"/>; <c>-1</c> when nothing goes with what is typed.</summary>
    public int Selected { get; private set; } = -1;

    /// <summary>The selected item, if there is one.</summary>
    public CompletionItem? SelectedItem => Selected >= 0 ? _shown[Selected] : null;

    /// <summary>The index in <see cref="Items"/> of the first item in view.</summary>
    public int Top { get; private set; }

    /// <summary>The items in view.</summary>
    public IEnumerable<CompletionItem> Visible => _shown.Skip(Top).Take(Height);

    /// <summary>Whether nothing in the list goes with what is typed: there is nothing to show.</summary>
    public bool IsEmpty => _shown.Count == 0;

    /// <summary>
    /// The length of the longest label of <see cref="Items"/>, which the dropdown is as wide as: known once the list is narrowed, rather than each time it is
    /// drawn.
    /// </summary>
    public int LongestLabel { get; private set; }

    /// <summary>
    /// Narrows the list to what goes with <paramref name="typed"/>.
    /// </summary>
    /// <param name="typed">The part of the word that is typed.</param>
    public void Filter(string typed)
    {
        _shown =
        [
            .. _all.Where(item => item.Label.StartsWith(typed, StringComparison.OrdinalIgnoreCase)),
            .. _all.Where(item => typed.Length > 0 && !item.Label.StartsWith(typed, StringComparison.OrdinalIgnoreCase)
                && item.Label.Contains(typed, StringComparison.OrdinalIgnoreCase)),
        ];

        LongestLabel = _shown.Count == 0 ? 0 : _shown.Max(item => item.Label.Length);
        var exact = _shown.FindIndex(item => string.Equals(item.Label, typed, StringComparison.OrdinalIgnoreCase));
        Selected = _shown.Count == 0 ? -1 : Math.Max(0, exact);
        Top = 0;
        Scroll();
    }

    /// <summary>
    /// Moves the selection by <paramref name="by"/> items, and no further than the first or the last.
    /// </summary>
    /// <param name="by">How many items down - or up, when it is negative.</param>
    public void Move(int by)
    {
        if (_shown.Count == 0)
        {
            return;
        }

        Selected = Math.Clamp(Selected + by, 0, _shown.Count - 1);
        Scroll();
    }

    // the view goes as far as it has to for the selection to be in it.
    private void Scroll()
    {
        if (Selected < Top)
        {
            Top = Math.Max(0, Selected);
        }
        else if (Selected >= Top + Height)
        {
            Top = Selected - Height + 1;
        }
    }
}
