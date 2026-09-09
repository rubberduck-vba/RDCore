using RDCore.SDK.ConsoleIO;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
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
    public ImmutableArray<VBSyntaxErrorInfo> SyntaxErrors { get; init; } = [];

    public bool IsSuccess => SyntaxTree is not null && SyntaxErrors.Length == 0;
}
