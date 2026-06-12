using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;

namespace RDCore.SDK.Model.AST.Directives
{
    /// <summary>
    /// A <c>BoundNode</c> representing an <c>Option</c> module directive.
    /// </summary>
    /// <param name="Location">The <c>Location</c> of the directive.</param>
    /// <param name="ModuleOption">The <c>ModuleOptions</c> value being configured.</param>
    public record class ModuleOptionDirectiveNode(Uri SemanticId, Location Location, ModuleOptions ModuleOption) : BoundDirective(SemanticId, Location) { }
    /// <summary>
    /// A <c>BoundNode</c> representing a <c>Def&lt;Type&gt;</c> module directive.
    /// </summary>
    /// <param name="Location">The <c>Location</c> of the directive.</param>
    /// <param name="VBType">The mapped <c>VBType</c> (per the semantics defined in MS-VBAL 5.2.2 Implicit Definition Directives).</param>
    /// <param name="Value">The prefixing scheme defined by this directive.</param>
    public record class TypeDefDirectiveNode(Uri SemanticId, Location Location, VBType VBType, DefTypePrefixMapping Value) : BoundDirective(SemanticId, Location) 
    {
        /// <summary>
        /// Gets the <c>Def&lt;Type&gt;</c> token corresponding to the specified <c>VBType</c>, as defined in <strong>MS-VBAL 5.2.2</strong> Implicit Definition Directives.
        /// </summary>
        /// <param name="type">The <c>VBType</c> to map.</param>
        /// <returns><c>null</c> given any <c>VBType</c> that does not have a defined implicit declaration token.</returns>
        public static string? Token(VBType type) => type switch
        {
            VBBooleanType => Tokens.DefBool,
            VBByteType => Tokens.DefByte,
            VBIntegerType => Tokens.DefInt,
            VBLongType => Tokens.DefLng,
            VBLongLongType => Tokens.DefLngLng,
            VBLongPtrType_x64 => Tokens.DefLngPtr,
            VBLongPtrType_x86 => Tokens.DefLngPtr,
            VBCurrencyType => Tokens.DefCur,
            VBSingleType => Tokens.DefSng,
            VBDoubleType => Tokens.DefDbl,
            VBDateType => Tokens.DefDate,
            VBStringType => Tokens.DefStr,
            VBObjectType => Tokens.DefObj,
            VBVariantType => Tokens.DefVar,
            _ => default
        };
    }
}
