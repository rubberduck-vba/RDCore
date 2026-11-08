using RDCore.SDK.Logging;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;

namespace RDCore.Runtime.LanguageCore.Errors;

/// <summary>
/// Creates the syntax errors of the language from their <see cref="VBCompileErrorId"/>, with the standard (localized) message of each.
/// </summary>
public static class SyntaxErrors
{
    extension(VBSyntaxErrorInfo)
    {
        /// <summary>
        /// Creates a <see cref="VBSyntaxErrorInfo"/> for the specified <see cref="VBCompileErrorId"/> at the specified <see cref="SourceLocation"/>.
        /// </summary>
        /// <param name="errorId">The formal error code.</param>
        /// <param name="location">The document location of the problematic node.</param>
        /// <param name="verbose">A detailed message appended depending on the server trace configuration.</param>
        public static VBSyntaxErrorInfo For(VBCompileErrorId errorId, SourceLocation location, string verbose)
            => new((int)errorId, location, CompileErrors.MessageOf(errorId), verbose);
    }

    extension(VBSyntaxErrorInfo error)
    {
        /// <summary>
        /// The formal error ID: a class of <em>compilation error</em> that occurs during parsing as the
        /// syntax tree is assembled.
        /// </summary>
        public VBCompileErrorId VBCompileErrorId => (VBCompileErrorId)error.ErrorId;
    }

    extension(ModuleParseResult)
    {
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
    }
}