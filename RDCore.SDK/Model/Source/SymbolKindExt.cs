namespace RDCore.SDK.Model.Source;

/// <summary>
/// Describes the kind of a <c>Symbol</c>, extending the set defined by the LSP <c>SymbolKind</c>.
/// </summary>
/// <remarks>
/// Values below 128 numerically match the LSP <c>SymbolKind</c> constants; conversion to the protocol
/// type happens at the protocol boundary (<c>Server/ProtocolExtensions</c>).
/// </remarks>
public enum SymbolKindExt
{
    // NOTE: values 1-18 (from the initial LSP version) are deemed supported if DocumentSymbolClientCapabilities.SymbolKind is not specified.

    #region LSP@1.0
    //File = 1,
    Module = 2,
    //Namespace_ = 3,
    Project = 3,
    Class = 5,
    Procedure = 6,
    Property = 7,
    Field = 8,
    //Constructor_ = 9,
    Enum = 10,
    Interface = 11,
    Function = 12,
    Variable = 13,
    Constant = 14,
    StringLiteral = 15,
    NumberLiteral = 16,
    BooleanLiteral = 17,
    Array = 18,
    #endregion

    #region LSP@3.17
    Object = 19,
    Key = 20,
    Null = 21,
    EnumMember = 22,
    UserDefinedType = 23,
    Event = 24,
    Operator = 25,
    //TypeParameter_ = 26,
    #endregion

    /* padding */

    /* 128+: extensions */

    /// <summary>
    /// (Extension) A public kind of symbol that does not reach the client.
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
