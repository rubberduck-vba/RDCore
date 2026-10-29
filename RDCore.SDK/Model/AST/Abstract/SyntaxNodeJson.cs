using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using RDCore.SDK.Model.AST.Statements;

namespace RDCore.SDK.Model.AST.Abstract;

/// <summary>
/// Shared <see cref="System.Text.Json"/> configuration for (de)serializing a <see cref="SyntaxNode"/> tree.
/// </summary>
/// <remarks>
/// The AST crosses the ParseServer-to-LanguageServer process boundary as JSON on every parse
/// (<c>PlatformJson</c>). Most node types store the same children under two or three differently-named
/// properties: the generic inherited spine (<see cref="SyntaxNode.Children"/>, and
/// <see cref="ExpressionNode"/>/<see cref="StatementNode"/>'s own <c>Inputs</c>) and specific typed
/// properties (e.g. <c>InlineIfStatementNode.ConditionExpression</c>). Left alone,
/// <see cref="System.Text.Json"/> serializes all of them, so each nesting level <em>multiplies</em> the
/// payload rather than adding to it — an ordinary depth-16 nested expression (`1 + 1 + ... + 1`)
/// serializes to hundreds of megabytes.
/// <para>
/// <see cref="Options"/> installs a <see cref="DefaultJsonTypeInfoResolver"/> modifier that detects, per
/// concrete node type, whether the generic spine (<c>Children</c>/<c>Inputs</c>) is exactly
/// reconstructable from that type's <em>other</em>, non-<c>[JsonIgnore]</c>d node-bearing properties —
/// and if so, omits the spine from the JSON instead of the reverse. Never the reverse: a typed property
/// (read directly by consumers like <c>SymbolBuilder</c>, post-deserialize, in a different process)
/// silently losing data on the wire would be a far worse failure than a bloated payload. The check runs
/// per <em>instance</em>, not just per type, and falls back to keeping the spine on any shape it can't
/// prove — false negatives (a payload that could have shrunk further but didn't) are an acceptable
/// missed optimization; false positives (dropping data that wasn't actually reconstructable) are not.
/// </para>
/// </remarks>
public static class SyntaxNodeJson
{
    /// <summary>
    /// The options every AST (de)serialization call site should share — <see cref="PlatformJson"/> (the
    /// live wire path) and tests alike, so what a test exercises matches what actually ships.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNameCaseInsensitive = true,

        // System.Text.Json defaults to 64, which an AST reaches on source a person could have written:
        // a nested block statement still costs a JSON level or two with the spine omitted, so the parse
        // server answered a bogus L0C0 diagnostic for perfectly valid code (measured on this shape: 40
        // nested `If`s failed to serialize, 18 still fit).
        MaxDepth = MaxNodeDepth,

        // a null is what an absent property deserializes to: writing it says nothing a reader needs (measured: a parse result of a 2,400-line module was
        // 5 MB of JSON, its semantics 3.6 MB, on every keystroke).
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new SourceRangeJsonConverter(), new SourceLocationJsonConverter(), new SyntaxNodeIdJsonConverter() },

        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { OmitRedundantSpine, OmitRedundantLocation } },
    };

    /// <summary>
    /// <see cref="Options"/>, less every property that says where something was written: in the source (a <see cref="Source.SourceLocation"/>, a
    /// <see cref="Source.SourceRange"/>) or in the tree (a node's <see cref="SyntaxNodeId"/>, its path from the module). Two writings of the same code with
    /// these are the same text, however the code moved: a blank line inserted above it, a comment edited, or a declaration added before it leaves it as
    /// it was.
    /// </summary>
    /// <remarks>
    /// For comparing, and so for writing only: what is read with these has no location. The fingerprints of a program image are written with them, and
    /// so are those of the declarations a program that waits was suspended with.
    /// </remarks>
    public static JsonSerializerOptions LocationFreeOptions { get; } = CreateLocationFreeOptions();

    private static JsonSerializerOptions CreateLocationFreeOptions()
    {
        var options = new JsonSerializerOptions(Options);
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
                && (underlying == typeof(Source.SourceLocation) || underlying == typeof(Source.SourceRange) || underlying == typeof(SyntaxNodeId));
    }

    // an expression node has its location twice: its own Location, and the SourceLocation of the node it is, which it is constructed with. The second is
    // written only where it differs, and is constructed from the first when it is read.
    private static void OmitRedundantLocation(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object || !typeof(SyntaxNode).IsAssignableFrom(typeInfo.Type))
        {
            return;
        }

        var location = typeInfo.Properties.FirstOrDefault(property => property.Name == "Location" && property.PropertyType == typeof(Source.SourceLocation));
        var sourceLocation = typeInfo.Properties.FirstOrDefault(property => property.Name == nameof(SyntaxNode.SourceLocation));
        if (location?.Get is not { } read || sourceLocation is null)
        {
            return;
        }

        sourceLocation.ShouldSerialize = (instance, value) => !Equals(value, read(instance));
    }

    // a range is four numbers: [start line, start character, end line, end character].
    private sealed class SourceRangeJsonConverter : JsonConverter<Source.SourceRange>
    {
        public override Source.SourceRange Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            Span<int> numbers = stackalloc int[4];
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException("A range is an array of four numbers.");
            }

            for (var i = 0; i < 4; i++)
            {
                reader.Read();
                numbers[i] = reader.GetInt32();
            }

            reader.Read();
            if (reader.TokenType != JsonTokenType.EndArray)
            {
                throw new JsonException("A range is an array of four numbers.");
            }

            return new(new(numbers[0], numbers[1]), new(numbers[2], numbers[3]));
        }

        public override void Write(Utf8JsonWriter writer, Source.SourceRange value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(value.Start.Line);
            writer.WriteNumberValue(value.Start.Character);
            writer.WriteNumberValue(value.End.Line);
            writer.WriteNumberValue(value.End.Character);
            writer.WriteEndArray();
        }
    }

    // a location: {"Uri": ..., "Range": [...]}, the Uri written only where it is not the one written last, in a payload (PayloadDocuments).
    private sealed class SourceLocationJsonConverter : JsonConverter<Source.SourceLocation>
    {
        public override Source.SourceLocation Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            ExpectStart(ref reader, "location");
            var payload = PayloadDocuments.Current;
            var uri = payload?.Location;
            var range = default(Source.SourceRange);
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var name = reader.GetString();
                reader.Read();
                if (string.Equals(name, nameof(Source.SourceLocation.Uri), StringComparison.OrdinalIgnoreCase))
                {
                    uri = reader.TokenType == JsonTokenType.Null ? null : new Uri(reader.GetString()!, UriKind.RelativeOrAbsolute);
                    if (payload is not null)
                    {
                        payload.Location = uri;
                    }
                }
                else if (string.Equals(name, nameof(Source.SourceLocation.Range), StringComparison.OrdinalIgnoreCase))
                {
                    range = JsonSerializer.Deserialize<Source.SourceRange>(ref reader, options);
                }
                else
                {
                    reader.Skip();
                }
            }

            return new(uri!, range);
        }

        public override void Write(Utf8JsonWriter writer, Source.SourceLocation value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            var payload = PayloadDocuments.Current;
            if (payload is null || value.Uri is null || !SameUri(payload.Location, value.Uri))
            {
                writer.WritePropertyName(nameof(Source.SourceLocation.Uri));
                if (value.Uri is null)
                {
                    writer.WriteNullValue();
                }
                else
                {
                    writer.WriteStringValue(value.Uri.OriginalString);
                }

                if (payload is not null)
                {
                    payload.Location = value.Uri;
                }
            }

            writer.WritePropertyName(nameof(Source.SourceLocation.Range));
            JsonSerializer.Serialize(writer, value.Range, options);
            writer.WriteEndObject();
        }

        // the same address, spelled the same: Uri.Equals ignores the fragment, which a location keeps.
        private static bool SameUri(Uri? last, Uri uri) => last is not null && string.Equals(last.OriginalString, uri.OriginalString, StringComparison.Ordinal);
    }

    // a node's identity: {"DocumentUri": ..., "Lineage": [...]}, the document written only where it is not the one written last, in a payload (PayloadDocuments).
    private sealed class SyntaxNodeIdJsonConverter : JsonConverter<SyntaxNodeId>
    {
        public override SyntaxNodeId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            ExpectStart(ref reader, "node identity");
            var payload = PayloadDocuments.Current;
            var document = payload?.Node;
            var lineage = default(ImmutableArray<int>);
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var name = reader.GetString();
                reader.Read();
                if (string.Equals(name, nameof(SyntaxNodeId.DocumentUri), StringComparison.OrdinalIgnoreCase))
                {
                    document = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                    if (payload is not null)
                    {
                        payload.Node = document;
                    }
                }
                else if (string.Equals(name, nameof(SyntaxNodeId.Lineage), StringComparison.OrdinalIgnoreCase))
                {
                    lineage = reader.TokenType == JsonTokenType.Null ? default : [.. JsonSerializer.Deserialize<int[]>(ref reader, options)!];
                }
                else
                {
                    reader.Skip();
                }
            }

            return new(document!, lineage);
        }

        public override void Write(Utf8JsonWriter writer, SyntaxNodeId value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            var payload = PayloadDocuments.Current;
            if (payload is null || value.DocumentUri is null || !string.Equals(payload.Node, value.DocumentUri, StringComparison.Ordinal))
            {
                writer.WriteString(nameof(SyntaxNodeId.DocumentUri), value.DocumentUri);
                if (payload is not null)
                {
                    payload.Node = value.DocumentUri;
                }
            }

            writer.WritePropertyName(nameof(SyntaxNodeId.Lineage));
            if (value.Lineage.IsDefault)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.WriteStartArray();
                foreach (var position in value.Lineage)
                {
                    writer.WriteNumberValue(position);
                }
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }
    }

    private static void ExpectStart(ref Utf8JsonReader reader, string what)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"A {what} is an object.");
        }
    }

    /// <summary>
    /// The maximum nesting depth an AST payload may reach, in JSON levels.
    /// </summary>
    /// <remarks>
    /// A guard against a cyclic or hostile payload, not a language limit — VBA imposes no nesting depth
    /// of its own, so this is set well past anything anyone writes by hand rather than to a number
    /// derived from the language. At roughly three or four levels per nested source construct it admits
    /// a couple of hundred levels of nested statements.
    /// </remarks>
    public const int MaxNodeDepth = 1024;

    private static readonly ConcurrentDictionary<Type, Func<object, IEnumerable<SyntaxNode>>[]> _alternateAccessorsByType = new();

    private static void OmitRedundantSpine(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object || !typeof(SyntaxNode).IsAssignableFrom(typeInfo.Type))
        {
            return;
        }

        var alternates = ResolveAlternateAccessors(typeInfo.Type);
        if (alternates.Length == 0)
        {
            return;
        }

        foreach (var property in typeInfo.Properties)
        {
            if (property.Name is not ("Children" or "Inputs") || property.PropertyType != typeof(ImmutableArray<SyntaxNode>))
            {
                continue;
            }

            property.ShouldSerialize = (instance, value) =>
            {
                try
                {
                    var actual = (ImmutableArray<SyntaxNode>)value!;
                    var reconstructed = alternates.SelectMany(get => get(instance)).ToArray();
                    // exact-match only: a partial reconstruction (e.g. a condition expression that
                    // covers only part of what Children holds) must not be treated as redundant.
                    return !(actual.Length == reconstructed.Length && actual.SequenceEqual(reconstructed));
                }
                catch
                {
                    // can't safely prove redundancy on this shape - keep the spine, matching the
                    // unoptimized (always-correct) behavior this mechanism refines.
                    return true;
                }
            };
        }
    }

    /// <summary>
    /// The node-bearing properties of <paramref name="concreteType"/> other than the generic
    /// <c>Children</c>/<c>Inputs</c> spine itself, excluding any already marked <c>[JsonIgnore]</c> —
    /// an ignored property (e.g. <c>VBBinaryOperatorExpressionNode.Left</c>/<c>Right</c>, computed views
    /// that intentionally rely on <c>Children</c> remaining the JSON source of truth) is invisible on
    /// the wire, so it can never justify dropping the one property that actually carries the data.
    /// </summary>
    private static Func<object, IEnumerable<SyntaxNode>>[] ResolveAlternateAccessors(Type concreteType)
        => _alternateAccessorsByType.GetOrAdd(concreteType, static type => [.. type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name is not ("Children" or "Inputs") && p.GetIndexParameters().Length == 0
                && p.GetCustomAttribute<JsonIgnoreAttribute>() is null)
            .Select(TryBuildAccessor)
            .OfType<Func<object, IEnumerable<SyntaxNode>>>()]);

    private static Func<object, IEnumerable<SyntaxNode>>? TryBuildAccessor(PropertyInfo property)
    {
        var type = property.PropertyType;

        if (typeof(SyntaxNode).IsAssignableFrom(type))
        {
            return instance => property.GetValue(instance) is SyntaxNode node ? [node] : [];
        }

        if (type == typeof(StatementBlock))
        {
            return instance => property.GetValue(instance) is StatementBlock block ? block.Children : [];
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ImmutableArray<>)
            && typeof(SyntaxNode).IsAssignableFrom(type.GetGenericArguments()[0]))
        {
            return instance => property.GetValue(instance) is IEnumerable items ? items.Cast<SyntaxNode>() : [];
        }

        if (type.IsArray && typeof(SyntaxNode).IsAssignableFrom(type.GetElementType()!))
        {
            return instance => property.GetValue(instance) is IEnumerable items ? items.Cast<SyntaxNode>() : [];
        }

        return null;
    }
}

/// <summary>
/// The documents a platform payload has written or read so far, while one is (de)serialized (<see cref="Platform.Protocol.PlatformJson"/>): a location or a
/// node identity writes its document only where it is not the one written last, and is read with the one read last where it has none.
/// </summary>
/// <remarks>
/// A payload is written and read in the same order, start to end, in one call on one thread: a document that is written once is read once, before everything
/// that has it. Outside a payload - anything (de)serialized with <see cref="SyntaxNodeJson.Options"/> directly - every location and identity has its document.
/// </remarks>
internal sealed class PayloadDocuments
{
    [ThreadStatic]
    private static PayloadDocuments? _current;

    /// <summary>The payload (de)serialized on this thread, if one is.</summary>
    public static PayloadDocuments? Current => _current;

    /// <summary>The document of the last location written or read.</summary>
    public Uri? Location { get; set; }

    /// <summary>The document of the last node identity written or read.</summary>
    public string? Node { get; set; }

    /// <summary>Starts a payload on this thread, until the scope is disposed.</summary>
    public static IDisposable Begin()
    {
        var previous = _current;
        _current = new PayloadDocuments();
        return new Scope(previous);
    }

    private sealed class Scope(PayloadDocuments? previous) : IDisposable
    {
        public void Dispose() => _current = previous;
    }
}
