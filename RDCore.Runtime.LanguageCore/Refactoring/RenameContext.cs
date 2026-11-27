using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Workspace.Forms;
using RDCore.Runtime.LanguageCore.Static;
using RDCore.Runtime.LanguageCore.Static.Abstract;

namespace RDCore.Runtime.LanguageCore.Refactoring;

// the designer of a form's module names the class whose events the form handles, as its own (MS-VBAL §5.3.1.8).
// each part of a module of parts (RD-VBAL §5.1) is a module of its own here, at the module's address, with its own tree and the text of its document.
public sealed record class RenameModule(Uri Module, ModuleNode Syntax, string Source, FormDescriptor? Designer = null)
{
    // the document the part is written in, as its nodes name it (WorkspaceSymbol.DocumentUri): what tells the parts of one address apart.
    public Uri Document => Syntax.SourceLocation.Uri;

    // whether it is the part at the module's address that the document writes; with no document, any part at the address.
    public bool Is(Uri module, Uri? document)
        => Module.AbsoluteUri == module.AbsoluteUri && (document is null || Document.AbsoluteUri == document.AbsoluteUri);

    // what names the part among those of every address: its address and its document.
    public string Key => KeyOf(Module, Document);

    public static string KeyOf(Uri module, Uri document) => $"{module.AbsoluteUri} {document.AbsoluteUri}";
}

public sealed record class RenameContext(
    ISymbolResolver Resolver, ScopeTree ScopeTree, IReadOnlyList<RenameModule> Modules, StaticEnvironment? Environment = null,
    IReadOnlyList<IPossibleClassesSource>? ClassSources = null)
{
    // the part a member is written in: the one at its module's address that its document writes (WorkspaceSymbol.DocumentUri), or the first at the address
    // for a member read from no document.
    public RenameModule? PartOf(Symbol member)
    {
        var module = NameOccurrences.ModuleOf(member.Uri);
        var document = (member as WorkspaceSymbol)?.DocumentUri;
        return Modules.FirstOrDefault(candidate => candidate.Is(module, document)) ?? Modules.FirstOrDefault(candidate => candidate.Is(module, null));
    }

    // whether a location of a parse of a module is in a branch of an #If block that is not live (MS-VBAL §3.4), as the environment host evaluated the
    // module: null when that is not known, and a check reads every branch as an alternative of the others.
    public Func<ModuleParseResult, Func<SourceLocation, bool>?> DeadTestOf { get; init; } = static _ => null;

    // the lower bound Option Base gives the arrays of a module (MS-VBAL §5.2.1.2): a module of parts has one, which any of its parts states (RD-VBAL §5.1),
    // and each part is one of the modules of its address here, with its own tree.
    public int OptionBaseOf(Uri module)
        => Modules.Any(part => part.Module.AbsoluteUri == module.AbsoluteUri && part.Syntax.GetOptionBase() != 0) ? 1 : 0;
}
