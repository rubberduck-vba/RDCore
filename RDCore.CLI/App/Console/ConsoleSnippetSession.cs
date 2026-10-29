using RDCore.SDK.Formatting;
using System.Text;

namespace RDCore.CLI.App.Console;

/// <summary>
/// A snippet inserted in the prompt's line, while its tab stops are being filled in: Tab goes from one to the next, and a tab stop that is in several places
/// (<c>For ${1:i} = 1 To 10: Debug.Print ${1:i}: Next</c>) is changed in all of them as it is typed in the first.
/// </summary>
/// <remarks>
/// The tab stops are where the line has them now: an edit of the one the cursor is in moves those after it. A tab stop that was in the text of another
/// (<c>${1:Dim ${2:x}}</c>) is gone once that one's text is typed over. The last stop is <c>$0</c>, or the end of the snippet when it has none; the snippet is
/// done once the cursor is there.
/// </remarks>
internal sealed class ConsoleSnippetSession
{
    private sealed class Place(int index, int start, int length)
    {
        public int Index { get; } = index;
        public int Start { get; set; } = start;
        public int Length { get; set; } = length;
        public int End => Start + Length;
    }

    private readonly List<Place> _places;
    private readonly int[] _order;
    private int _at;

    private ConsoleSnippetSession(List<Place> places, int[] order)
    {
        _places = places;
        _order = order;
    }

    /// <summary>
    /// The tab stops of a snippet inserted at <paramref name="offset"/> of the line; the cursor is at none of them yet.
    /// </summary>
    /// <param name="snippet">The snippet.</param>
    /// <param name="offset">Where its text starts in the line.</param>
    public static ConsoleSnippetSession Start(Snippet snippet, int offset)
    {
        var places = snippet.TabStops.Select(stop => new Place(stop.Index, offset + stop.Start, stop.Length)).ToList();
        if (!places.Any(place => place.Index == 0))
        {
            places.Add(new Place(0, offset + snippet.Text.Length, 0));
        }

        int[] order = [.. places.Select(place => place.Index).Where(index => index > 0).Distinct().Order(), 0];
        return new ConsoleSnippetSession(places, order) { _at = -1 };
    }

    /// <summary>Whether the cursor is at the end of the snippet: there is no tab stop left to go to.</summary>
    public bool IsDone => _ended || (_at >= 0 && _order[_at] == 0);

    // no tab stop is left to go to: the ones after the cursor's were in the text of one that was typed over.
    private bool _ended;

    /// <summary>Whether the text of the tab stop the cursor is at is selected, as it is when the cursor gets there: what is typed then replaces it.</summary>
    public bool IsSelected { get; private set; }

    /// <summary>Where the tab stop the cursor is at is, in the line: the first of its places.</summary>
    public (int Start, int Length) Current => Primary() is { } place ? (place.Start, place.Length) : (0, 0);

    /// <summary>The places of the tab stop the cursor is at, in the line, in its order: what is drawn as being edited.</summary>
    public IEnumerable<(int Start, int Length)> CurrentPlaces
        => _at < 0 ? [] : _places.Where(place => place.Index == _order[_at]).OrderBy(place => place.Start).Select(place => (place.Start, place.Length));

    /// <summary>Goes to the next tab stop that is still in the line; at the last one, the end of the snippet.</summary>
    /// <returns>
    /// Where the cursor is: at the end of the stop's text, which is selected; <see langword="null"/> when no tab stop is left - the ones after were in the
    /// text of one that was typed over - and the snippet is done where the cursor is.
    /// </returns>
    public int? Next()
    {
        for (var at = _at + 1; at < _order.Length; at++)
        {
            if (Has(_order[at]))
            {
                return Go(at);
            }
        }

        _ended = true;
        return null;
    }

    /// <summary>Goes back to the tab stop before the one the cursor is at that is still in the line; at the first, the cursor stays where it is.</summary>
    /// <returns>Where the cursor is; <see langword="null"/> when it stays.</returns>
    public int? Previous()
    {
        for (var at = _at - 1; at >= 0; at--)
        {
            if (Has(_order[at]))
            {
                return Go(at);
            }
        }

        return null;
    }

    private bool Has(int index) => _places.Any(place => place.Index == index);

    private int Go(int at)
    {
        _at = at;
        var place = Primary()!;
        IsSelected = place.Length > 0 && !IsDone;
        return place.End;
    }

    /// <summary>The cursor moved without an edit: the text of the tab stop is not selected any more.</summary>
    public void Deselect() => IsSelected = false;

    /// <summary>Whether a position of the line is in the tab stop the cursor is at, its ends included.</summary>
    /// <param name="position">The position.</param>
    public bool IsInCurrent(int position) => Primary() is { } place && position >= place.Start && position <= place.End;

    /// <summary>
    /// Makes an edit of the line in the tab stop the cursor is at, and in each of its other places: the <paramref name="removed"/> characters at
    /// <paramref name="position"/> are replaced by <paramref name="inserted"/>.
    /// </summary>
    /// <param name="text">The line.</param>
    /// <param name="cursor">Where the cursor is, which is after what was inserted once the edit is made.</param>
    /// <param name="position">Where the edit is.</param>
    /// <param name="removed">How many characters it removes.</param>
    /// <param name="inserted">What it inserts.</param>
    /// <returns>Whether the edit was made: not when it is not all in the tab stop the cursor is at, which is the end of the snippet's tab stops.</returns>
    public bool TryEdit(StringBuilder text, ref int cursor, int position, int removed, string inserted)
    {
        if (_at < 0 || IsDone || Primary() is not { } primary || position < primary.Start || position + removed > primary.End)
        {
            return false;
        }

        var offset = position - primary.Start;
        var value = text.ToString(primary.Start, primary.Length).Remove(offset, removed).Insert(offset, inserted);
        var index = primary.Index;

        // the places of the tab stop from the last to the first, so that a place's change moves only the places after it, which are done.
        foreach (var place in _places.Where(place => place.Index == index).OrderByDescending(place => place.Start).ToList())
        {
            var (start, end) = (place.Start, place.End);
            var delta = value.Length - place.Length;
            text.Remove(start, place.Length).Insert(start, value);

            // a tab stop in this one's text was typed over; one after it moves with the text.
            _places.RemoveAll(other => other != place && other.Index != index && other.Start >= start && other.End <= end
                && !(other.Length == 0 && (other.Start == start || other.Start == end)));
            foreach (var other in _places.Where(other => other != place && other.Start >= end))
            {
                other.Start += delta;
            }

            place.Length = value.Length;
        }

        IsSelected = false;
        cursor = primary.Start + offset + inserted.Length;
        return true;
    }

    private Place? Primary() => _at < 0 ? null : _places.Where(place => place.Index == _order[_at]).OrderBy(place => place.Start).FirstOrDefault();
}
