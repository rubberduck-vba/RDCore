using System.Text.Json;
using System.Text.Json.Serialization;

namespace RDCore.SDK.Model.AST.Abstract;

/// <summary>
/// Bridges polymorphic (de)serialization for a property or collection element declared as a narrower
/// abstract <see cref="SyntaxNode"/> subtype (e.g. <see cref="ExpressionNode"/>) rather than
/// <see cref="SyntaxNode"/> itself.
/// </summary>
/// <remarks>
/// <c>[JsonPolymorphic]</c>/<c>[JsonDerivedType]</c> is declared once, on <see cref="SyntaxNode"/>.
/// <see cref="System.Text.Json"/> resolves polymorphism from the property's own <em>declared</em>
/// type, and does not apply an ancestor's attributes to a narrower abstract type that carries none of
/// its own — so a property like <c>IfBlockStatementNode.ConditionExpression : ExpressionNode</c>
/// would otherwise throw <c>NotSupportedException</c> ("Deserialization of interface or abstract
/// types is not supported") the moment it round-trips through JSON, since <see cref="ExpressionNode"/>
/// itself carries no polymorphism attributes. Delegating through <see cref="SyntaxNode"/> reuses the
/// one declaration instead of repeating the whole derived-type list on every abstract subtype.
/// </remarks>
public sealed class SyntaxNodeSubtypeJsonConverter<T> : JsonConverter<T> where T : SyntaxNode
{
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => (T?)JsonSerializer.Deserialize<SyntaxNode>(ref reader, options);

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, (SyntaxNode)value, options);
}
