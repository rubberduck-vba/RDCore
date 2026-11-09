using RDCore.SDK.Logging;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;

namespace RDCore.Runtime.LanguageCore.Errors;

public static class SyntaxErrors
{
    extension(VBSyntaxErrorInfo)
    {
        public static VBSyntaxErrorInfo For(VBCompileErrorId errorId, SourceLocation location, string verbose)
            => new((int)errorId, location, CompileErrors.MessageOf(errorId), verbose);
    }

    extension(VBSyntaxErrorInfo error)
    {
        public VBCompileErrorId VBCompileErrorId => (VBCompileErrorId)error.ErrorId;
    }

    extension(ModuleParseResult)
    {
        public static ModuleParseResult Failed(SourceLocation location, string verbose,
            SourcePathScrubMode scrub = SourcePathScrubMode.RepoRelative) => new()
        {
            SyntaxErrors = [VBSyntaxErrorInfo.For(VBCompileErrorId.SyntaxError, location,
                SourcePathAnonymizer.Scrub(verbose, scrub))]
        };
    }
}