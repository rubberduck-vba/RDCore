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
    /// A failed parse carrying one synthesized syntax error. <paramref name="verbose"/> is run
    /// through <see cref="SourcePathAnonymizer"/> first, so this funnel cannot leak a build-machine
    /// source path onto the wire regardless of the call site.
    /// </summary>
    public static ModuleParseResult Failed(SourceLocation location, string verbose,
        SourcePathScrubMode wireErrorDetail = SourcePathScrubMode.RepoRelative) => new()
    {
        SyntaxErrors = [VBSyntaxErrorInfo.For(VBCompileErrorId.SyntaxError, location,
            SourcePathAnonymizer.Scrub(verbose, wireErrorDetail))]
    };

    /// <summary>
    /// A failed parse whose detail is an exception. The stack trace is anonymized per
    /// <paramref name="wireErrorDetail"/>; the caller is expected to have logged the unredacted
    /// exception to the process log already.
    /// </summary>
    public static ModuleParseResult Failed(SourceLocation location, Exception exception,
        SourcePathScrubMode wireErrorDetail = SourcePathScrubMode.RepoRelative)
        => Failed(location, exception.ToString(), wireErrorDetail);

    public ModuleNode? SyntaxTree { get; init; }
    public ImmutableArray<SyntaxNode> PrecompilerTrivia { get; init; } = [];
    public ImmutableArray<VBSyntaxErrorInfo> SyntaxErrors { get; init; } = [];

    public bool IsSuccess => SyntaxTree is not null && SyntaxErrors.Length == 0;
}
