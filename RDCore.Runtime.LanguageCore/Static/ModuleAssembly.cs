using RDCore.Runtime.LanguageCore.Errors;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Workspace;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace RDCore.Runtime.LanguageCore.Static;

// one part of a module: the module of a file, or of one of its Class or Module blocks (IsBlock), with its own parse (RDCoreDocument.ParseOf), whose tree
// is not null. A block's part has what its header states (IsPartial, IsCreatable, Visibility) and the header line (Header), where those words are written.
public sealed record class ModulePart(string Name, ModuleType Kind, ModuleParseResult Parse)
{
    public bool IsBlock { get; init; }

    public bool IsPartial { get; init; }

    public bool IsCreatable { get; init; }

    public AccessModifier? Visibility { get; init; }

    public BlockBoundaryTriviaNode? Header { get; init; }

    // the parts a file declares: its own module, named and of the kind given, then each block's, in the order the file writes them.
    public static IEnumerable<ModulePart> PartsOf(Uri file, string name, ModuleType kind, ModuleParseResult parse)
    {
        var document = new RDCoreDocument(file, parse);
        yield return new ModulePart(name, kind, document.ParseOf(file)!);
        foreach (var block in parse.BlockModules)
        {
            yield return new ModulePart(block.Name, block.Kind, document.ParseOf(block.Uri)!)
            {
                IsBlock = true,
                IsPartial = block.IsPartial,
                IsCreatable = block.IsCreatable,
                Visibility = block.Visibility,
                Header = parse.Trivia.OfType<BlockBoundaryTriviaNode>()
                    .FirstOrDefault(node => !node.IsEnd && node.SourceLocation.Range.Start.Line == block.Range.Start.Line),
            };
        }
    }
}

// a module of a project, as its parts make it (ModuleAssembly.Of). Redundant is where a part states a directive to no effect: the same way as a part
// before it, or Creatable on a class that no other project sees.
public sealed record class AssembledModule(string Name, ModuleType Kind, ModuleParseResult Parse, ModuleDirectives Directives)
{
    public ImmutableArray<SourceLocation> Redundant { get; init; } = [];
}

// a module that its parts make invalid: none of its parts loads.
public sealed record class RefusedModule(string Name, ImmutableArray<VBCompileErrorInfo> Errors);

// RD-VBAL §5.1: the parts of one name are one module. At most one is plain; the Partial parts join it, or make the module on their own. The module is of
// its plain part's kind, or of its first part's, and every part is of that kind. Each directive of the module is stated by one part or the same by each
// part that states it: a file part states it with an attribute (Option Private Module for a standard module), a block part with its header's words and
// the '@PredeclaredId annotation. A directive that no part states is the default of a module that declares none, except that a class of blocks alone is
// not creatable (VB_Creatable = False) unless a header writes Creatable, as a new MS-VBA class is not: a project that references its project cannot
// New it, and its own project can.
public static class ModuleAssembly
{
    private enum Directive
    {
        Exposed,
        Creatable,
        PrivateModule,
        PredeclaredId,
        Extensible,
    }

    // a directive as a part states it: the word that states it, and whether that word is a modifier of the part's header.
    private readonly record struct Statement(Directive Directive, bool Value, SourceLocation Location, string Word, bool IsModifier = false);

    // the modules the parts make, and those they make invalid, in the order of each name's first part. Parts of one name are taken in the order given:
    // the order the project lists its files, and the order each file writes its blocks.
    public static (ImmutableArray<AssembledModule> Modules, ImmutableArray<RefusedModule> Refused) Of(IEnumerable<ModulePart> parts)
    {
        var modules = ImmutableArray.CreateBuilder<AssembledModule>();
        var refused = ImmutableArray.CreateBuilder<RefusedModule>();
        foreach (var named in parts.GroupBy(part => part.Name, StringComparer.OrdinalIgnoreCase))
        {
            // the plain part first, then the Partial parts as they come.
            var ordered = named.OrderBy(part => part.IsPartial).ToArray();
            var errors = new List<VBCompileErrorInfo>();
            var name = ordered[0].Name;
            var kind = ordered[0].Kind;

            errors.AddRange(NameTaken(ordered, name));
            errors.AddRange(TwiceInAFile(ordered, name));
            errors.AddRange(ordered.Where(part => part.Kind != kind).Select(part => VBCompileErrorInfo.For(VBCompileErrorId.ModulePartOfAnotherKind,
                KindLocationOf(part), string.Format(Exceptions.VBCompileError_ModulePartOfAnotherKind_Verbose, name))));

            var redundant = ImmutableArray.CreateBuilder<SourceLocation>();
            var creatableWords = new List<SourceLocation>();
            var stated = new Dictionary<Directive, bool>();
            if (errors.Count == 0)
            {
                foreach (var statements in ordered.SelectMany(part => StatementsOf(part, kind).DistinctBy(statement => statement.Directive))
                    .GroupBy(statement => statement.Directive))
                {
                    var first = statements.First();
                    stated[first.Directive] = first.Value;
                    creatableWords.AddRange(statements.Where(statement => statement.IsModifier && statement.Directive is Directive.Creatable)
                        .Select(statement => statement.Location));
                    foreach (var statement in statements.Skip(1))
                    {
                        if (statement.Value == first.Value)
                        {
                            redundant.Add(statement.Location);
                        }
                        else
                        {
                            errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.ModuleDirectiveStatedDifferently, statement.Location,
                                string.Format(Exceptions.VBCompileError_ModuleDirectiveStatedDifferently_Verbose, name, statement.Word)));
                        }
                    }
                }
            }

            if (errors.Count > 0)
            {
                refused.Add(new RefusedModule(name, [.. errors]));
                continue;
            }

            var parse = ordered.Length == 1 ? ordered[0].Parse : Assembled(ordered);
            // what no part states is what the tree reads of a module that states nothing (a block states none of its directives with an attribute).
            bool Value(Directive directive, bool otherwise) => stated.TryGetValue(directive, out var value) ? value : otherwise;
            var read = parse.SyntaxTree.GetModuleDirectives();
            var directives = read with
            {
                Exposed = Value(Directive.Exposed, read.Exposed),
                Creatable = Value(Directive.Creatable, ordered.Any(part => !part.IsBlock) && read.Creatable),
                PrivateModule = Value(Directive.PrivateModule, read.PrivateModule),
                PredeclaredId = Value(Directive.PredeclaredId, read.PredeclaredId),
                Extensible = Value(Directive.Extensible, read.Extensible),
            };

            // what no other project sees, no other project creates.
            if (kind.IsClass() && !directives.Exposed)
            {
                redundant.AddRange(creatableWords.Where(word => !redundant.Contains(word)));
            }

            modules.Add(new AssembledModule(name, kind, parse, directives) { Redundant = redundant.ToImmutable() });
        }

        return (modules.ToImmutable(), refused.ToImmutable());
    }

    // MS-VBAL §4.2: two plain parts are two modules of one name, and neither is the module of that name (ModuleNameStaticSemantics.NamesTakenTwice). Each
    // is reported where it says its name: a file's VB_Name, a block's header.
    private static IEnumerable<VBCompileErrorInfo> NameTaken(IReadOnlyList<ModulePart> parts, string name)
    {
        var plain = parts.Where(part => !part.IsPartial).ToArray();
        if (plain.Length < 2)
        {
            return [];
        }

        return plain.Select(part => part.IsBlock
            ? VBCompileErrorInfo.For(VBCompileErrorId.NameConflictsWithModuleProjectOrLibrary, HeaderLocationOf(part, part.Name),
                string.Format(Exceptions.VBCompileError_ModuleNameTaken_Verbose, name))
            : ModuleNameStaticSemantics.NameTaken(part.Parse.SyntaxTree!, name));
    }

    // RD-VBAL §5.1: one file holds one part of a module at most. Two plain parts are two modules of one name (NameTaken).
    private static IEnumerable<VBCompileErrorInfo> TwiceInAFile(IReadOnlyList<ModulePart> parts, string name)
    {
        foreach (var file in parts.GroupBy(part => part.Parse.SyntaxTree!.SourceLocation.Uri))
        {
            var first = file.First();
            foreach (var part in file.Skip(1).Where(part => part.IsPartial || first.IsPartial))
            {
                yield return VBCompileErrorInfo.For(VBCompileErrorId.NameConflictsWithModuleProjectOrLibrary, KindLocationOf(part),
                    string.Format(Exceptions.VBCompileError_ModulePartTwiceInAFile_Verbose, name));
            }
        }
    }

    // what a part states of the module's directives, in the order it writes them. Of two statements of one directive in one part, the first is the part's,
    // as the module's tree reads its attributes.
    private static IEnumerable<Statement> StatementsOf(ModulePart part, ModuleType kind)
    {
        var tree = part.Parse.SyntaxTree!;
        if (!part.IsBlock)
        {
            foreach (var attribute in tree.Children.OfType<AttributeDirectiveNode>().Where(attribute => attribute.Binding is null))
            {
                if (AttributeDirectiveOf(attribute.Name) is { } directive)
                {
                    var value = string.Equals(attribute.Value.Trim(), "True", StringComparison.OrdinalIgnoreCase);
                    yield return new Statement(directive, value, attribute.SourceLocation, attribute.Name);
                }
            }

            foreach (var option in tree.Children.OfType<ModuleOptionDirectiveNode>().Where(option => option.ModuleOption is ModuleOptions.OptionPrivateModule))
            {
                yield return new Statement(Directive.PrivateModule, true, option.SourceLocation, "Option Private Module");
            }

            yield break;
        }

        if (part.Visibility is { } visibility)
        {
            var word = visibility is AccessModifier.Public ? "Public" : "Private";
            yield return kind.IsClass()
                ? new Statement(Directive.Exposed, visibility is AccessModifier.Public, HeaderLocationOf(part, word), word, IsModifier: true)
                : new Statement(Directive.PrivateModule, visibility is AccessModifier.Private, HeaderLocationOf(part, word), word, IsModifier: true);
        }

        if (part.IsCreatable)
        {
            yield return new Statement(Directive.Creatable, true, HeaderLocationOf(part, "Creatable"), "Creatable", IsModifier: true);
        }

        foreach (var annotation in part.Parse.Trivia.OfType<AnnotationTriviaNode>()
            .Where(annotation => string.Equals(annotation.Name, "PredeclaredId", StringComparison.OrdinalIgnoreCase)))
        {
            yield return new Statement(Directive.PredeclaredId, true, annotation.SourceLocation, "@PredeclaredId");
        }
    }

    private static Directive? AttributeDirectiveOf(string name) => name switch
    {
        _ when string.Equals(name, Tokens.VB_Exposed, StringComparison.OrdinalIgnoreCase) => Directive.Exposed,
        _ when string.Equals(name, Tokens.VB_Creatable, StringComparison.OrdinalIgnoreCase) => Directive.Creatable,
        _ when string.Equals(name, Tokens.VB_PredeclaredId, StringComparison.OrdinalIgnoreCase) => Directive.PredeclaredId,
        _ when string.Equals(name, Tokens.VB_Extensible, StringComparison.OrdinalIgnoreCase) => Directive.Extensible,
        _ => null,
    };

    // one module of the parts: the first part's tree, with every part's declarations, then every part's procedures, so that the module declares its names
    // before its procedures as one file writes them; every part's trivia, directives and syntax errors; the first part's line ending and designer. Each
    // node keeps its identity and its location, in its own part's file.
    private static ModuleParseResult Assembled(IReadOnlyList<ModulePart> parts)
    {
        var trees = parts.Select(part => part.Parse.SyntaxTree!).ToArray();
        return parts[0].Parse with
        {
            SyntaxTree = trees[0] with
            {
                Children =
                [
                    .. trees.SelectMany(tree => tree.Children.Where(child => !IsProcedure(child))),
                    .. trees.SelectMany(tree => tree.Children.Where(IsProcedure)),
                ],
            },
            Trivia = [.. parts.SelectMany(part => part.Parse.Trivia)],
            PrecompilerTrivia = [.. parts.SelectMany(part => part.Parse.PrecompilerTrivia)],
            SyntaxErrors = [.. parts.SelectMany(part => part.Parse.SyntaxErrors)],
            BlockModules = [],
        };
    }

    // MS-VBAL §5.3: a procedure of the code section, which follows the declarations of the module.
    private static bool IsProcedure(SyntaxNode node) => node is MemberDeclarationNode
    {
        MemberKind: MemberKind.Procedure or MemberKind.Function or MemberKind.PropertyGet or MemberKind.PropertyLet or MemberKind.PropertySet,
    };

    // a part is reported at the word of its kind in its header, and a file part where it says its name, or at the start of its file.
    private static SourceLocation KindLocationOf(ModulePart part) => part.IsBlock
        ? HeaderLocationOf(part, part.Kind.IsClass() ? "Class" : "Module")
        : part.Parse.SyntaxTree!.GetDeclaredNameDirective()?.Location
            ?? new SourceLocation(part.Parse.SyntaxTree!.SourceLocation.Uri, new SourceRange(0, 0, 0, 0));

    // where the header writes a word, the header line when it does not, and the start of the part's tree without a header.
    private static SourceLocation HeaderLocationOf(ModulePart part, string word)
    {
        if (part.Header is not { } header)
        {
            return part.Parse.SyntaxTree!.SourceLocation;
        }

        var match = Regex.Match(header.Source, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return header.SourceLocation;
        }

        var start = header.SourceLocation.Range.Start;
        return new SourceLocation(header.SourceLocation.Uri,
            new SourceRange(start.Line, start.Character + match.Index, start.Line, start.Character + match.Index + match.Length));
    }
}
