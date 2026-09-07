using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace RDCore.SDK.Model.AST.Directives;

/// <summary>
/// A <c>BoundNode</c> representing a <c>VB_Attribute</c> directive.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The <c>Location</c> of the directive.</param>
/// <param name="Name">The name of the attribute.</param>
/// <param name="ValueExpression">An expression node that statically evaluates to the value of the attribute.</param>
/// <param name="Binding">An optional qualifier used for binding the attribute to the member it belongs to.</param>
public record class AttributeDirectiveNode(SyntaxNodeId Identity, SourceLocation Location, string Name, SyntaxNode ValueExpression, string? Binding = null)
    : DirectiveNode(Identity, Location, [ValueExpression]);

/// <summary>
/// A <c>BoundNode</c> representing an <c>Option</c> module directive.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The <c>Location</c> of the directive.</param>
/// <param name="ModuleOption">The <c>ModuleOptions</c> value being configured.</param>
public record class ModuleOptionDirectiveNode(SyntaxNodeId Identity, SourceLocation Location, ModuleOptions ModuleOption)
    : DirectiveNode(Identity, Location, []);

/// <summary>
/// A <c>BoundNode</c> representing a <c>Def&lt;Type&gt;</c> module directive.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The <c>Location</c> of the directive.</param>
/// <param name="Token">The <c>DefType</c> token mapping to a specific <c>VBType</c> (per the semantics defined in MS-VBAL 5.2.2 Implicit Definition Directives).</param>
/// <param name="Mappings">The prefixing scheme defined by this directive.</param>
public record class TypeDefDirectiveNode(SyntaxNodeId Identity, SourceLocation Location, string Token, ImmutableArray<DefTypePrefixMapping> Mappings) 
    : DirectiveNode(Identity, Location, []) 
{
    public VBType GetVBType(bool is64bit) => Token switch
    {
        Tokens.DefBool => VBBooleanType.TypeInfo,
        Tokens.DefByte => VBByteType.TypeInfo,
        Tokens.DefCur => VBCurrencyType.TypeInfo,
        Tokens.DefDate => VBDateType.TypeInfo,
        Tokens.DefDbl => VBDoubleType.TypeInfo,
        Tokens.DefInt => VBIntegerType.TypeInfo,
        Tokens.DefLng => VBLongType.TypeInfo,
        Tokens.DefLngLng => VBLongLongType.TypeInfo,
        Tokens.DefLngPtr => is64bit ? VBLongPtrType_x64.TypeInfo : VBLongPtrType_x86.TypeInfo,
        Tokens.DefObj => VBObjectType.TypeInfo,
        Tokens.DefSng => VBSingleType.TypeInfo,
        Tokens.DefStr => VBStringType.TypeInfo,
        Tokens.DefVar => VBVariantType.TypeInfo,

        _ => VBUnknownType.TypeInfo // illegal
    };
}

/// <summary>
/// Represents an <c>Implements</c> directive.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The <c>Location</c> of the directive.</param>
public record class ImplementsDirectiveNode(SyntaxNodeId Identity, SourceLocation Location, SyntaxNode? NameExpression = null)
    : DirectiveNode(Identity, Location, NameExpression is null ? [] : [NameExpression])
{
    /// <summary>
    /// The name expression resolving the implemented interface — the directive's single child.
    /// </summary>
    [JsonIgnore]
    public SyntaxNode NameExpression => Children.Single();
}