using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00417</c>: a <c>'@Folder</c> annotation that does not name the folder its file is in, under the root of its project: <c>Parent/Child/Module1.bas</c>
/// is in <c>"Parent.Child"</c>, and a file at the root is in none.
/// </summary>
/// <remarks>
/// RD-VBA reads a module's folder from where its file is, and the annotation changes nothing. What it names is the folder legacy Rubberduck's Code Explorer
/// shows the module in, once the project is imported into MS-VBA: the fix makes it the file's, or removes it from a file at the root. The project's root is
/// the host's (<see cref="SymbolUri.ProjectRootOf"/> of the module). Without the module's semantics the analyzer reports nothing. Nor does it report a file
/// outside the root, or a file under a folder whose name has a <c>.</c> in it, which the annotation cannot write.
/// </remarks>
internal sealed class FolderAnnotationAnalyzer : IModuleAnalyzer
{
    private const string FolderAnnotation = "Folder";

    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.FolderAnnotationNotTheFilesFolder.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.Semantics is not { } semantics || !context.Document.IsFile || FolderOf(context.Document, semantics.Module) is not { } folder)
        {
            yield break;
        }

        foreach (var annotation in context.ParseResult.Trivia.OfType<AnnotationTriviaNode>())
        {
            if (!annotation.IsNamed(FolderAnnotation)
                || annotation.Children is not [LiteralExpressionNode { StaticValue: VBStringValue written } literal, ..]
                || string.Equals(written.Value, folder, StringComparison.Ordinal))
            {
                continue;
            }

            yield return folder.Length == 0
                ? new AnalyzerFinding(RDCoreDiagnosticId.FolderAnnotationNotTheFilesFolder.ToCode(), annotation.SourceLocation.Range,
                    DiagnosticSeverity.Information, RDCoreDiagnosticsResources.FolderAnnotationNotTheFilesFolder_RootMessage)
                {
                    Fixes = new DiagnosticFixes
                    {
                        Edits = [SyntaxEdits.Fix(RDCoreDiagnosticsResources.FolderAnnotationNotTheFilesFolder_RootFix,
                            SyntaxEdits.Remove(AnnotationEdits.RemovalOf(context.ParseResult, annotation)))],
                    },
                }
                : new AnalyzerFinding(RDCoreDiagnosticId.FolderAnnotationNotTheFilesFolder.ToCode(), annotation.SourceLocation.Range,
                    DiagnosticSeverity.Information, AnalyzerMessages.Format(RDCoreDiagnosticsResources.FolderAnnotationNotTheFilesFolder_Message, folder))
                {
                    // none for a folder name that no string literal writes (a line break, which a path can hold off Windows).
                    Fixes = ExpressionWriter.LiteralOf(new VBStringValue(folder)) is { } text
                        ? new DiagnosticFixes
                        {
                            Edits = [SyntaxEdits.Fix(AnalyzerMessages.Format(RDCoreDiagnosticsResources.FolderAnnotationNotTheFilesFolder_Fix, folder),
                                SyntaxEdits.Replace(literal.SourceLocation.Range, text))],
                        }
                        : null,
                };
        }
    }

    // the folders from the project's root to the file's, joined with a '.' as the annotation writes them: empty at the root; null for a file outside the
    // root, or under a folder whose name has a '.', which the annotation would read as two.
    private static string? FolderOf(Uri document, Uri module)
    {
        var relative = Path.GetRelativePath(SymbolUri.ProjectRootOf(module).LocalPath, Path.GetDirectoryName(document.LocalPath) ?? string.Empty);
        if (relative == ".")
        {
            return string.Empty;
        }

        var folders = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        return Path.IsPathRooted(relative) || folders.Any(name => name == ".." || name.Contains('.', StringComparison.Ordinal))
            ? null
            : string.Join('.', folders);
    }
}
