using RDCore.SDK.ConsoleIO;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Workspace.Forms;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.AST;

public record class ModuleParseResult
{
    public static ModuleParseResult Success(ModuleNode node) => new() { SyntaxTree = node };

    /// <summary>
    /// A failed parse carrying one synthesized syntax error. <paramref name="verbose"/> is scrubbed
    /// through <see cref="SourcePathAnonymizer"/> per <paramref name="scrub"/>, so this funnel cannot
    /// leak a build-machine source path onto the wire regardless of the call site.
    /// </summary>
    public static ModuleParseResult Failed(SourceLocation location, string verbose,
        SourcePathScrubMode scrub = SourcePathScrubMode.RepoRelative) => new()
    {
        SyntaxErrors = [VBSyntaxErrorInfo.For(VBCompileErrorId.SyntaxError, location,
            SourcePathAnonymizer.Scrub(verbose, scrub))]
    };

    public ModuleNode? SyntaxTree { get; init; }
    public ImmutableArray<SyntaxNode> PrecompilerTrivia { get; init; } = [];

    /// <summary>
    /// The source of the module that is not code, in the order it is written: the <c>.cls</c> file header (<see cref="ModuleHeaderTriviaNode"/>),
    /// comments (<see cref="CommentTriviaNode"/>) and the annotations in them (<see cref="AnnotationTriviaNode"/>, inside the range of the comment that holds them).
    /// </summary>
    /// <remarks>
    /// Together with <see cref="SyntaxTree"/> and <see cref="PrecompilerTrivia"/> it accounts for every token of the source, which is what a formatter
    /// or an exporter needs to write the module back out. Trivia is not among the children of the syntax nodes, since it can be written anywhere.
    /// </remarks>
    public ImmutableArray<SyntaxNode> Trivia { get; init; } = [];
    public ImmutableArray<VBSyntaxErrorInfo> SyntaxErrors { get; init; } = [];

    /// <summary>
    /// What the module's designer declares, when the module is a form module: the form's class and its controls, read from the module's designer block and
    /// its companion <c>.frx</c> file (<see cref="FormDesigner.Read"/>); <c>null</c> for any other module, and for a form whose designer data cannot be read.
    /// </summary>
    /// <remarks>
    /// The parser, which reads text alone, never sets it: the language server, which has the companion file, does, on the parse it caches. It travels with the
    /// parse to the host, whose checks of the module's declarations need to know the names its controls take.
    /// </remarks>
    public FormDescriptor? Designer { get; init; }

    public bool IsSuccess => SyntaxTree is not null && SyntaxErrors.Length == 0;
}
