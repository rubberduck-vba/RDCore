using System.Collections;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace RDCore.SDK.Model.AST.Abstract;

/// <summary>
/// A syntax tree written as a flat array of its nodes (<see cref="SyntaxNodeJson"/>): <c>{"$nodes": [...], "$root": n}</c>. Each node is written by itself, with
/// the nodes it holds written as their index in the array, and every node comes after the nodes it holds. A reader constructs each node from nodes it has
/// already constructed, in one pass, so writing and reading a tree takes the same stack however deep the tree is.
/// </summary>
/// <remarks>
/// A node that two properties hold is written once, and read back as one node. The array is written and read in the same order, start to end, which is what a
/// payload's documents need (<see cref="PayloadDocuments"/>).
/// </remarks>
internal static class FlatSyntaxTreeJson
{
    private const string NodesProperty = "$nodes";
    private const string RootProperty = "$root";

    /// <summary>
    /// Options that write and read every syntax tree flat: <paramref name="shape"/>, which says what a node is written with, and the converters of the tree.
    /// </summary>
    /// <param name="shape">The options a node's own properties are written with: its resolver and its converters.</param>
    public static JsonSerializerOptions Create(JsonSerializerOptions shape)
    {
        var nodes = new JsonSerializerOptions(shape);
        nodes.TypeInfoResolver = shape.TypeInfoResolver!.WithAddedModifier(WriteHeldNodesAsIndexes);
        nodes.MakeReadOnly(populateMissingResolver: true);

        var trees = new JsonSerializerOptions(shape);
        trees.Converters.Add(new TreeConverterFactory(nodes));
        // a node is polymorphic as the array writes it (nodes), and a tree is not: its root says nothing of its own type.
        trees.TypeInfoResolver = shape.TypeInfoResolver!.WithAddedModifier(static typeInfo =>
        {
            if (typeof(SyntaxNode).IsAssignableFrom(typeInfo.Type))
            {
                typeInfo.PolymorphismOptions = null;
            }
        });
        return trees;
    }

    // the tree written or read on this thread, if one is: the index of each node written, and each node read.
    [ThreadStatic]
    private static Dictionary<SyntaxNode, int>? _written;

    [ThreadStatic]
    private static List<SyntaxNode>? _read;

    // a node's own properties that hold nodes: a node, or a sequence of them, is written as indexes.
    private static void WriteHeldNodesAsIndexes(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        foreach (var property in typeInfo.Properties)
        {
            if (IndexConverterFor(property.PropertyType) is { } converter)
            {
                property.CustomConverter = converter;
            }
        }
    }

    private static JsonConverter? IndexConverterFor(Type type)
    {
        if (typeof(SyntaxNode).IsAssignableFrom(type))
        {
            return (JsonConverter)Activator.CreateInstance(typeof(NodeIndexConverter<>).MakeGenericType(type))!;
        }

        if (NodeElementOf(type) is { } element)
        {
            var converter = type.IsArray ? typeof(NodeIndexArrayConverter<>)
                : type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ImmutableArray<>) ? typeof(NodeIndexImmutableArrayConverter<>)
                : type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>) ? typeof(NodeIndexEnumerableConverter<>)
                : null;
            return converter is null ? null : (JsonConverter)Activator.CreateInstance(converter.MakeGenericType(element))!;
        }

        return null;
    }

    // the node type a sequence of nodes holds: an array, an ImmutableArray or an IEnumerable of them.
    private static Type? NodeElementOf(Type type)
    {
        var element = type.IsArray ? type.GetElementType()
            : type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(ImmutableArray<>) || type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                ? type.GetGenericArguments()[0]
                : null;
        return element is not null && typeof(SyntaxNode).IsAssignableFrom(element) ? element : null;
    }

    // the nodes of the tree under root, each after the nodes it holds, and the index of each in that order: a walk with a stack of its own, over the
    // properties the node is written with.
    private static (List<SyntaxNode> Order, Dictionary<SyntaxNode, int> Indexes) PostOrder(SyntaxNode root, JsonSerializerOptions nodes)
    {
        var order = new List<SyntaxNode>();
        var indexes = new Dictionary<SyntaxNode, int>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<(SyntaxNode Node, bool Expanded)>();
        pending.Push((root, false));
        var held = new List<SyntaxNode>();
        while (pending.Count > 0)
        {
            var (node, expanded) = pending.Pop();
            if (expanded)
            {
                if (indexes.TryAdd(node, order.Count))
                {
                    order.Add(node);
                }

                continue;
            }

            if (indexes.ContainsKey(node))
            {
                continue;
            }

            pending.Push((node, true));
            held.Clear();
            CollectHeld(node, nodes.GetTypeInfo(node.GetType()), held);
            for (var i = held.Count - 1; i >= 0; i--)
            {
                if (!indexes.ContainsKey(held[i]))
                {
                    pending.Push((held[i], false));
                }
            }
        }

        return (order, indexes);
    }

    // a written property that may hold nodes, with the type info of the type it is declared as.
    private sealed record HeldProperty(JsonPropertyInfo Property, Func<object, object?> Get, JsonTypeInfo Declared);

    private static readonly ConditionalWeakTable<JsonTypeInfo, HeldProperty[]> _heldProperties = new();

    private static HeldProperty[] HeldPropertiesOf(JsonTypeInfo typeInfo)
        => _heldProperties.GetValue(typeInfo, static typeInfo => [.. typeInfo.Properties
            .Where(property => property.Get is not null && MayHoldNodes(property.PropertyType, typeInfo.Options))
            .Select(property => new HeldProperty(property, property.Get!, typeInfo.Options.GetTypeInfo(property.PropertyType)))]);

    // the nodes an object's written properties hold, in the order they are written: the object is a node, or a record of a node's (a StatementBlock, an
    // array's bound) written inside it, whose own properties are looked into the same way.
    private static void CollectHeld(object instance, JsonTypeInfo typeInfo, List<SyntaxNode> held)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        foreach (var (property, get, declared) in HeldPropertiesOf(typeInfo))
        {
            if (get(instance) is { } value && property.ShouldSerialize?.Invoke(instance, value) != false)
            {
                Collect(value, declared, held);
            }
        }
    }

    // a value is written as the type it is declared as (a VBTypedValue by its own converter, whatever value it is), and looked into as that type.
    private static void Collect(object value, JsonTypeInfo declared, List<SyntaxNode> held)
    {
        if (value is SyntaxNode node)
        {
            held.Add(node);
            return;
        }

        if (declared.Kind == JsonTypeInfoKind.Enumerable && value is IEnumerable sequence && !IsDefaultImmutableArray(value))
        {
            JsonTypeInfo? element = null;
            foreach (var item in sequence)
            {
                if (item is SyntaxNode heldNode)
                {
                    held.Add(heldNode);
                }
                else if (item is not null)
                {
                    Collect(item, element ??= declared.Options.GetTypeInfo(declared.ElementType!), held);
                }
            }
        }
        else if (declared.Kind == JsonTypeInfoKind.Object)
        {
            CollectHeld(value, declared, held);
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, bool> _mayHoldNodes = new();

    [ThreadStatic]
    private static HashSet<Type>? _deciding;

    // whether a value declared as a type can hold a node: a node, a sequence of what can, or an object one of whose written properties can. A type that holds
    // itself (a value's type, whose default value is a value of it) is decided by its other properties.
    private static bool MayHoldNodes(Type declared, JsonSerializerOptions nodes)
    {
        if (_mayHoldNodes.TryGetValue(declared, out var known))
        {
            return known;
        }

        if (typeof(SyntaxNode).IsAssignableFrom(declared))
        {
            return _mayHoldNodes[declared] = true;
        }

        _deciding ??= [];
        if (!_deciding.Add(declared))
        {
            return false;
        }

        try
        {
            var typeInfo = nodes.GetTypeInfo(declared);
            var mayHold = typeInfo.Kind switch
            {
                JsonTypeInfoKind.Enumerable or JsonTypeInfoKind.Dictionary => MayHoldNodes(typeInfo.ElementType!, nodes),
                JsonTypeInfoKind.Object => typeInfo.Properties.Any(property => MayHoldNodes(property.PropertyType, nodes)),
                _ => false,
            };
            return _mayHoldNodes[declared] = mayHold;
        }
        finally
        {
            _deciding.Remove(declared);
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Func<object, bool>?> _isDefault = new();

    // an ImmutableArray that holds no array, which is written as null, and which is not enumerated.
    private static bool IsDefaultImmutableArray(object value)
        => _isDefault.GetOrAdd(value.GetType(), static type =>
            type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ImmutableArray<>) && type.GetProperty(nameof(ImmutableArray<int>.IsDefault)) is { } isDefault
                ? instance => (bool)isDefault.GetValue(instance)!
                : null) is { } test && test(value);

    private static int IndexOf(SyntaxNode node)
        => _written is { } written && written.TryGetValue(node, out var index)
            ? index
            : throw new JsonException($"A {node.GetType().Name} is held by a property the walk of its tree did not look into.");

    private static SyntaxNode NodeAt(int index)
        => _read is { } read && index >= 0 && index < read.Count
            ? read[index]
            : throw new JsonException($"A node index ({index}) names no node read before it.");

    private static T ReadIndex<T>(ref Utf8JsonReader reader) where T : SyntaxNode
        => reader.TokenType == JsonTokenType.Number && NodeAt(reader.GetInt32()) is T node
            ? node
            : throw new JsonException($"A {typeof(T).Name} is written as the index of a node of that type.");

    private sealed class TreeConverterFactory(JsonSerializerOptions nodes) : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeof(SyntaxNode).IsAssignableFrom(typeToConvert);

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
            => (JsonConverter)Activator.CreateInstance(typeof(TreeConverter<>).MakeGenericType(typeToConvert), nodes)!;
    }

    // a tree, as the outermost node of it a payload holds: {"$nodes": [...], "$root": n}.
    private sealed class TreeConverter<T>(JsonSerializerOptions nodes) : JsonConverter<T> where T : SyntaxNode
    {
        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            var (order, indexes) = PostOrder(value, nodes);
            var outer = _written;
            _written = indexes;
            try
            {
                var node = (JsonTypeInfo<SyntaxNode>)nodes.GetTypeInfo(typeof(SyntaxNode));
                writer.WriteStartObject();
                writer.WritePropertyName(NodesProperty);
                writer.WriteStartArray();
                foreach (var each in order)
                {
                    JsonSerializer.Serialize(writer, each, node);
                }
                writer.WriteEndArray();
                writer.WriteNumber(RootProperty, indexes[value]);
                writer.WriteEndObject();
            }
            finally
            {
                _written = outer;
            }
        }

        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("A syntax tree is an object of its nodes and its root.");
            }

            var outer = _read;
            _read = [];
            try
            {
                T? root = null;
                while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
                {
                    var name = reader.GetString();
                    reader.Read();
                    if (name == NodesProperty)
                    {
                        if (reader.TokenType != JsonTokenType.StartArray)
                        {
                            throw new JsonException("The nodes of a syntax tree are an array.");
                        }

                        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                        {
                            _read.Add(JsonSerializer.Deserialize<SyntaxNode>(ref reader, nodes)
                                ?? throw new JsonException("A node of a syntax tree is not null."));
                        }
                    }
                    else if (name == RootProperty)
                    {
                        root = ReadIndex<T>(ref reader);
                    }
                    else
                    {
                        reader.Skip();
                    }
                }

                return root ?? throw new JsonException("A syntax tree has a root.");
            }
            finally
            {
                _read = outer;
            }
        }
    }

    // a node a node holds: its index.
    private sealed class NodeIndexConverter<T> : JsonConverter<T> where T : SyntaxNode
    {
        public override bool HandleNull => false;

        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => ReadIndex<T>(ref reader);

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WriteNumberValue(IndexOf(value));
    }

    private static void WriteIndexes<T>(Utf8JsonWriter writer, IEnumerable<T> nodes) where T : SyntaxNode
    {
        writer.WriteStartArray();
        foreach (var node in nodes)
        {
            if (node is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.WriteNumberValue(IndexOf(node));
            }
        }
        writer.WriteEndArray();
    }

    private static List<T?> ReadIndexes<T>(ref Utf8JsonReader reader) where T : SyntaxNode
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException($"The {typeof(T).Name}s a node holds are an array of indexes.");
        }

        var nodes = new List<T?>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            nodes.Add(reader.TokenType == JsonTokenType.Null ? null : ReadIndex<T>(ref reader));
        }

        return nodes;
    }

    private sealed class NodeIndexImmutableArrayConverter<T> : JsonConverter<ImmutableArray<T>> where T : SyntaxNode
    {
        public override ImmutableArray<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.TokenType == JsonTokenType.Null ? default : [.. ReadIndexes<T>(ref reader)!];

        public override void Write(Utf8JsonWriter writer, ImmutableArray<T> value, JsonSerializerOptions options)
        {
            if (value.IsDefault)
            {
                writer.WriteNullValue();
                return;
            }

            WriteIndexes(writer, value);
        }
    }

    private sealed class NodeIndexArrayConverter<T> : JsonConverter<T[]> where T : SyntaxNode
    {
        public override T[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => [.. ReadIndexes<T>(ref reader)!];

        public override void Write(Utf8JsonWriter writer, T[] value, JsonSerializerOptions options) => WriteIndexes(writer, value);
    }

    private sealed class NodeIndexEnumerableConverter<T> : JsonConverter<IEnumerable<T>> where T : SyntaxNode
    {
        public override IEnumerable<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => [.. ReadIndexes<T>(ref reader)!];

        public override void Write(Utf8JsonWriter writer, IEnumerable<T> value, JsonSerializerOptions options) => WriteIndexes(writer, value);
    }
}
