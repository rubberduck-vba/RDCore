using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace RDCore.Server.ProtocolExtensions;

internal enum SymbolKindExt
{
    // NOTE: values 1-18 (from the initial LSP version) are deemed supported if DocumentSymbolClientCapabilities.SymbolKind is not specified.

    #region LSP@1.0
    //File = SymbolKind.File,
    Module = SymbolKind.Module,
    //Namespace_ = SymbolKind.Namespace,
    Project = SymbolKind.Package,
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
    /// Marks a symbol that does not reach the client; see <see cref="Parsing.Model.IgnoredSymbol"/>
    /// </summary>
    Ignored = 128,

    Attribute,
    LineLabel,
    DateLiteral,
    VariantLiteral,

}
