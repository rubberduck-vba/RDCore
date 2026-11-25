using RDCore.Runtime.Semantics;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Execution.External.Scripting;

/// <summary>
/// What a member of a <see cref="ScriptingDictionary"/> refuses, which the provider raises as scrrun.dll's error.
/// </summary>
internal enum DictionaryFailure
{
    /// <summary>
    /// Nothing: the member did what it was asked.
    /// </summary>
    None,

    /// <summary>
    /// The value can be no key, an array (error 5).
    /// </summary>
    NoKey,

    /// <summary>
    /// The key is already there (error 457).
    /// </summary>
    Duplicate,

    /// <summary>
    /// The key is not there (error 32811).
    /// </summary>
    Missing,

    /// <summary>
    /// The compare mode of a dictionary that has keys, or a mode below 0 (error 5).
    /// </summary>
    CompareMode,
}

/// <summary>
/// The state of a <c>Scripting.Dictionary</c>: its keys and items, in the order they were added, and how it compares keys that are strings.
/// </summary>
/// <remarks>
/// <para>
/// A key is one of a kind: a number, a string, <c>Null</c>, an error value or an object. Numbers of any type are one key when their values are equal, as
/// scrrun.dll compares them: a <c>Single</c> to 7 significant digits and a <c>Double</c> or a <c>Date</c> to 15, as OLE's conversion to <c>Decimal</c> keeps
/// them, and <c>True</c> is -1. <c>Empty</c> is the empty string, and an object is its identity. An array cannot be a key. A key is given back as it was
/// added, with its own type.
/// </para>
/// <para>
/// The dictionary keeps a copy of each key and item in a cell of its own, which holds the objects in it, and lets them go when they are removed, replaced or
/// the dictionary is destroyed (<see cref="RemoveAll"/>).
/// </para>
/// </remarks>
internal sealed class ScriptingDictionary
{
    // Binary, Text, Database: what CompareMode takes, besides the identifier of a locale.
    private const int BinaryCompare = 0;
    private const int TextCompare = 1;
    private const int DatabaseCompare = 2;

    // the largest magnitude a Double keeps as a Decimal; past it, a number is its Double.
    private const double DecimalRange = 7.9e28;

    private sealed class Entry(VBTypedValue key, ValueBindingHandle keyCell, VBTypedValue item, ValueBindingHandle itemCell)
    {
        public VBTypedValue Key { get; set; } = key;

        public ValueBindingHandle KeyCell { get; set; } = keyCell;

        public VBTypedValue Item { get; set; } = item;

        public ValueBindingHandle ItemCell { get; set; } = itemCell;

        public LinkedListNode<Entry>? Node { get; set; }
    }

    private enum KeyKind : byte
    {
        Number,
        Real,
        Text,
        Null,
        Error,
        Object,
    }

    // what a key is compared as: a number (a Decimal, or a Double past its range), a string, Null, an error value's number or an object's identity.
    private readonly record struct Key(KeyKind Kind, decimal Number = 0, double Real = 0, string? Text = null, VBRuntimeObjectId Identity = default);

    // strings by the dictionary's comparison, everything else by its own value.
    private sealed class KeyComparer(StringComparer text) : IEqualityComparer<Key>
    {
        public bool Equals(Key x, Key y) => x.Kind == y.Kind && x.Kind switch
        {
            KeyKind.Number => x.Number == y.Number,
            KeyKind.Real or KeyKind.Error => x.Real.Equals(y.Real),
            KeyKind.Text => text.Equals(x.Text, y.Text),
            KeyKind.Object => x.Identity == y.Identity,
            _ => true,
        };

        public int GetHashCode(Key key) => HashCode.Combine(key.Kind, key.Kind switch
        {
            KeyKind.Number => key.Number.GetHashCode(),
            KeyKind.Real or KeyKind.Error => key.Real.GetHashCode(),
            KeyKind.Text => text.GetHashCode(key.Text!),
            KeyKind.Object => key.Identity.GetHashCode(),
            _ => 0,
        });
    }

    private readonly IRuntimeSession session;
    private readonly LinkedList<Entry> _order = new();
    private Dictionary<Key, Entry> _entries;

    /// <param name="session">The session the dictionary is an object of.</param>
    public ScriptingDictionary(IRuntimeSession session)
    {
        this.session = session;
        _entries = new(new KeyComparer(ComparerFor(BinaryCompare)));
    }

    /// <summary>
    /// How keys that are strings are compared: <c>vbBinaryCompare</c> (0), <c>vbTextCompare</c> (1), <c>vbDatabaseCompare</c> (2), or a locale's.
    /// </summary>
    public int CompareMode { get; private set; } = BinaryCompare;

    /// <summary>
    /// The number of keys.
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Adds <paramref name="item"/> under <paramref name="key"/>.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="item">The item.</param>
    public DictionaryFailure Add(VBTypedValue key, VBTypedValue item)
    {
        if (!TryNormalize(key, out var normal))
        {
            return DictionaryFailure.NoKey;
        }

        if (_entries.ContainsKey(normal))
        {
            return DictionaryFailure.Duplicate;
        }

        Insert(normal, key, item);
        return DictionaryFailure.None;
    }

    /// <summary>
    /// Whether <paramref name="key"/> is a key of the dictionary.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="exists">Whether it is.</param>
    public DictionaryFailure Exists(VBTypedValue key, out bool exists)
    {
        exists = false;
        if (!TryNormalize(key, out var normal))
        {
            return DictionaryFailure.NoKey;
        }

        exists = _entries.ContainsKey(normal);
        return DictionaryFailure.None;
    }

    /// <summary>
    /// The item of <paramref name="key"/>. A key that is not there is added, with an item that is <c>Empty</c>, as scrrun.dll adds it.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="item">The item.</param>
    public DictionaryFailure Item(VBTypedValue key, out VBTypedValue item)
    {
        item = VBEmptyValue.Empty;
        if (!TryNormalize(key, out var normal))
        {
            return DictionaryFailure.NoKey;
        }

        item = (_entries.TryGetValue(normal, out var entry) ? entry : Insert(normal, key, VBEmptyValue.Empty)).Item;
        return DictionaryFailure.None;
    }

    /// <summary>
    /// Sets the item of <paramref name="key"/>, which is added when it is not there.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="item">The item.</param>
    public DictionaryFailure SetItem(VBTypedValue key, VBTypedValue item)
    {
        if (!TryNormalize(key, out var normal))
        {
            return DictionaryFailure.NoKey;
        }

        if (!_entries.TryGetValue(normal, out var entry))
        {
            Insert(normal, key, item);
            return DictionaryFailure.None;
        }

        var (copied, cell) = ObjectReferences.Keep(session, item);
        var released = entry.ItemCell;
        (entry.Item, entry.ItemCell) = (copied, cell);
        ObjectReferences.Release(session, released);
        return DictionaryFailure.None;
    }

    /// <summary>
    /// Renames the key <paramref name="key"/> to <paramref name="renamed"/>, which keeps its place in the order.
    /// </summary>
    /// <param name="key">The key there is.</param>
    /// <param name="renamed">The key it becomes.</param>
    public DictionaryFailure Rename(VBTypedValue key, VBTypedValue renamed)
    {
        if (!TryNormalize(key, out var normal) || !TryNormalize(renamed, out var newNormal))
        {
            return DictionaryFailure.NoKey;
        }

        if (!_entries.TryGetValue(normal, out var entry))
        {
            return DictionaryFailure.Missing;
        }

        if (_entries.ContainsKey(newNormal))
        {
            return DictionaryFailure.Duplicate;
        }

        var (copied, cell) = ObjectReferences.Keep(session, renamed);
        var released = entry.KeyCell;
        _entries.Remove(normal);
        (entry.Key, entry.KeyCell) = (copied, cell);
        _entries.Add(newNormal, entry);
        ObjectReferences.Release(session, released);
        return DictionaryFailure.None;
    }

    /// <summary>
    /// Removes <paramref name="key"/> and its item.
    /// </summary>
    /// <param name="key">The key.</param>
    public DictionaryFailure Remove(VBTypedValue key)
    {
        if (!TryNormalize(key, out var normal))
        {
            return DictionaryFailure.NoKey;
        }

        if (!_entries.Remove(normal, out var entry))
        {
            return DictionaryFailure.Missing;
        }

        _order.Remove(entry.Node!);
        Release(entry);
        return DictionaryFailure.None;
    }

    /// <summary>
    /// Removes every key and item, and lets go of what they held.
    /// </summary>
    public void RemoveAll()
    {
        if (_order.Count == 0)
        {
            return;
        }

        var released = _order.ToArray();
        _entries.Clear();
        _order.Clear();
        foreach (var entry in released)
        {
            Release(entry);
        }
    }

    /// <summary>
    /// Sets how keys that are strings are compared, which a dictionary that has a key does not change.
    /// </summary>
    /// <param name="mode">The mode, from 0.</param>
    public DictionaryFailure SetCompareMode(int mode)
    {
        if (_entries.Count > 0 || mode < 0)
        {
            return DictionaryFailure.CompareMode;
        }

        CompareMode = mode;
        _entries = new(new KeyComparer(ComparerFor(mode)));
        return DictionaryFailure.None;
    }

    /// <summary>
    /// The keys, in order, as a <c>Variant</c> array from 0; one with no element when the dictionary is empty.
    /// </summary>
    public VBVariantValue Keys() => ArrayOf(entry => entry.Key);

    /// <summary>
    /// The items, in order, as a <c>Variant</c> array from 0.
    /// </summary>
    public VBVariantValue Items() => ArrayOf(entry => entry.Item);

    /// <summary>
    /// The keys, in order, for an enumerator to enumerate.
    /// </summary>
    public IReadOnlyList<VBTypedValue> KeyList()
    {
        var keys = new VBTypedValue[_order.Count];
        var index = 0;
        foreach (var entry in _order)
        {
            keys[index++] = entry.Key;
        }

        return keys;
    }

    private VBVariantValue ArrayOf(Func<Entry, VBTypedValue> part)
    {
        var array = new VBResizableArrayValue([(0, _order.Count - 1)], VBVariantType.TypeInfo);
        var index = 0;
        foreach (var entry in _order)
        {
            array.TrySetElement(ElementCells.OfVariant(new VBVariantValue(part(entry))), index++);
        }

        return new VBVariantValue(array);
    }

    private Entry Insert(Key normal, VBTypedValue key, VBTypedValue item)
    {
        var (keyCopy, keyCell) = ObjectReferences.Keep(session, key);
        var (itemCopy, itemCell) = ObjectReferences.Keep(session, item);
        var entry = new Entry(keyCopy, keyCell, itemCopy, itemCell);
        entry.Node = _order.AddLast(entry);
        _entries.Add(normal, entry);
        return entry;
    }

    private void Release(Entry entry)
    {
        ObjectReferences.Release(session, entry.KeyCell);
        ObjectReferences.Release(session, entry.ItemCell);
    }

    private StringComparer ComparerFor(int mode)
    {
        var rules = mode switch
        {
            BinaryCompare => session.StringComparisonIn(OptionCompare.Binary),
            TextCompare => session.StringComparisonIn(OptionCompare.Text),
            DatabaseCompare => session.StringComparisonIn(OptionCompare.Database),
            _ when StringComparisonRules.TryForLocale(mode, session.Environment.Culture, out var locale) => locale,
            // a mode that names no locale compares text as vbTextCompare does: scrrun.dll takes any mode from 0.
            _ => session.StringComparisonIn(OptionCompare.Text),
        };

        return rules.Comparer;
    }

    // what a key is compared as; false for a value that can be no key (an array, a record).
    private static bool TryNormalize(VBTypedValue key, out Key normal)
    {
        Key? found = key switch
        {
            VBStringValue text => new Key(KeyKind.Text, Text: text.Value),
            VBEmptyValue => new Key(KeyKind.Text, Text: string.Empty),
            VBNullValue => new Key(KeyKind.Null),
            VBErrorValue error => new Key(KeyKind.Error, Real: error.Value),
            VBObjectValue reference => new Key(KeyKind.Object, Identity: reference.Value),
            VBByteValue value => new Key(KeyKind.Number, value.Value),
            VBIntegerValue value => new Key(KeyKind.Number, value.Value),
            VBLongValue value => new Key(KeyKind.Number, value.Value),
            VBLongLongValue value => new Key(KeyKind.Number, value.Value),
            VBBooleanValue value => new Key(KeyKind.Number, value.Value.StoredValue),
            VBCurrencyValue value => new Key(KeyKind.Number, value.Value.Value),
            VBDecimalValue value => new Key(KeyKind.Number, value.Value),
            VBSingleValue value => Number(value.Value, SingleDigits),
            VBDoubleValue value => Number(value.Value, DoubleDigits),
            VBDateValue value => Number(value.SerialValue, DoubleDigits),
            _ => null,
        };

        normal = found.GetValueOrDefault();
        return found is not null;
    }

    // the significant digits OLE's conversions to Decimal keep (VarDecFromR4, VarDecFromR8), which scrrun.dll compares numbers by; System.Decimal's
    // conversions keep every digit the binary value has, which makes the Single 0.1 another number than the Double 0.1.
    private const int SingleDigits = 7;
    private const int DoubleDigits = 15;

    private static Key Number(double value, int digits)
        => double.IsFinite(value) && Math.Abs(value) < DecimalRange ? new Key(KeyKind.Number, Significant(value, digits)) : new Key(KeyKind.Real, Real: value);

    private static decimal Significant(double value, int digits)
    {
        if (value == 0)
        {
            return 0m;
        }

        var places = digits - 1 - (int)Math.Floor(Math.Log10(Math.Abs(value)));
        if (places >= 0)
        {
            return Math.Round((decimal)value, Math.Min(places, 28), MidpointRounding.ToEven);
        }

        var factor = 1m;
        for (var power = places; power < 0; power++)
        {
            factor *= 10;
        }

        return Math.Round((decimal)value / factor, MidpointRounding.ToEven) * factor;
    }
}
