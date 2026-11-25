using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Workspace;

namespace RDCore.Symbols;

// the modules a document is the text of: its own, then the module of each of its blocks, in the order they are written. Each has its own part of the
// document's parse (RDCoreDocument.ParseOf) and is addressed under the project's root by its name.
public static class ParsedModules
{
    // the file's own module is named by its Attribute VB_Name, or else fallbackName (the file's), and is of the kind its file says; a block's is named by its
    // header and is of the kind its header says. A name two blocks share is the first one's part of the parse.
    public static IReadOnlyList<ParsedModule> Of(Uri root, Uri file, ModuleParseResult parse, string fallbackName, ModuleType ownKind)
    {
        var document = new RDCoreDocument(file, parse);
        var own = document.ParseOf(file) ?? parse;
        var name = NameOf(own, fallbackName);
        return
        [
            new ParsedModule(SymbolUri.Module(root, name), name, ownKind, own) { Document = file },
            .. parse.BlockModules.Select(block => new ParsedModule(SymbolUri.Module(root, block.Name), block.Name, block.Kind, document.ParseOf(block.Uri)!)
            {
                Document = file,
                Range = block.Range,
            }),
        ];
    }

    // the names a document's modules are defined under, its own first: what the host holds of the document, read off what was parsed of it. A document with no
    // parse is its own module alone, named fallbackName.
    public static IReadOnlyList<string> NamesOf(ModuleParseResult? parse, string fallbackName)
        => parse is null ? [fallbackName] : [NameOf(parse, fallbackName), .. parse.BlockModules.Select(block => block.Name)];

    // the file's own module's name: its Attribute VB_Name, or fallbackName. The tree is the file's own module's, and a block's attribute is not in it.
    private static string NameOf(ModuleParseResult parse, string fallbackName) => parse.SyntaxTree?.GetDeclaredName() ?? fallbackName;
}
