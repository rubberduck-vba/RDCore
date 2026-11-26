using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Workspace;

namespace RDCore.Diagnostics.Analyzers;

// the line a module's code starts on in its file, which a finding of the module as a whole is reported on: the top of the file for the file's own module,
// and the line after its header for a block's, which is where the parse puts the block's tree (RD-VBAL §5.1).
internal static class ModuleStart
{
    public static int LineOf(ModuleAnalysisContext context)
        => RDCoreDocument.FileOf(context.Document) != context.Document && context.ParseResult.SyntaxTree is { } module
            ? module.SourceLocation.Range.Start.Line
            : 0;
}
