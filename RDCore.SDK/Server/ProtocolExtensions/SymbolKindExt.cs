using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace RDCore.SDK.Server.ProtocolExtensions;

public enum SymbolKindExt
{
    // NOTE: values 1-18 (from the initial LSP version) are deemed supported if DocumentSymbolClientCapabilities.SymbolKind is not specified.

    #region LSP@1.0
    //File = SymbolKind.File,
    Module = SymbolKind.Module,
    //Namespace_ = SymbolKind.Namespace,
    Project = SymbolKind.Namespace,
    Class = SymbolKind.Class,
    Procedure = SymbolKind.Method,
    Property = SymbolKind.Property,
    Field = SymbolKind.Field,
    //Constructor_ = SymbolKind.Constructor,
    Enum = SymbolKind.Enum,
    Interface = SymbolKind.Interface,
    Function = SymbolKind.Function,
    Variable = SymbolKind.Variable,
    Constant = SymbolKind.Constant,
    StringLiteral = SymbolKind.String,
    NumberLiteral = SymbolKind.Number,
    BooleanLiteral = SymbolKind.Boolean,
    Array = SymbolKind.Array,
    #endregion

    #region LSP@3.17
    Object = SymbolKind.Object,
    Key = SymbolKind.Key,
    Null = SymbolKind.Null,
    EnumMember = SymbolKind.EnumMember,
    UserDefinedType = SymbolKind.Struct,
    Event = SymbolKind.Event,
    Operator = SymbolKind.Operator,
    //TypeParameter_ = SymbolKind.TypeParameter,
    #endregion

    /* padding */

    /* 128+: extensions */

    /// <summary>
    /// (Extension) A public kind of symbol that does not reach the client; see <see cref="Model.Symbols.IgnoredSymbol"/>
    /// </summary>
    Ignored = 128,

    /// <summary>
    /// (Extension) A symbol that represents an attribute name, hidden in the VBE.
    /// </summary>
    Attribute,
    /// <summary>
    /// (Extension) A symbol that represents a module directive (e.g. <c>Option</c> or <c>Def&lt;Type&gt;</c> statements).
    /// </summary>
    Directive,
    /// <summary>
    /// (Extension) A symbol that represents a line label.
    /// </summary>
    LineLabel,
    /// <summary>
    /// (Extension) A symbol that represents a <c>Date</c> literal (reverse-formatted as <c>#M/D/YYYY#</c> in the VBE).
    /// </summary>
    DateLiteral,
    /// <summary>
    /// (Extension) A symbol that represents a <c>Variant</c> literal, i.e. <c>Empty</c>.
    /// </summary>
    VariantLiteral,

    /// <summary>
    /// (Extension) A symbol that represents a data type descriptor, e.g. the result of a <c>TypeOf</c> operator.
    /// </summary>
    TypeDescriptor,
}
