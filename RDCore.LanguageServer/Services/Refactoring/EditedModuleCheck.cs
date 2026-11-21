using RDCore.LanguageServer.Services.Refactoring.ExtractMethod;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;

namespace RDCore.LanguageServer.Services.Refactoring;

/// <summary>
/// What is wrong with a module a refactoring's edit would leave, whatever the refactoring.
/// </summary>
internal enum EditedModuleProblemKind
{
    /// <summary>
    /// The edited module does not parse.
    /// </summary>
    DoesNotParse,

    /// <summary>
    /// A name of an edited procedure declares a variable by being named, which no name of the procedures did before: it binds to nothing that was declared.
    /// </summary>
    NameDoesNotBind,

    /// <summary>
    /// The edited module has a compile error it did not have, in an edited procedure or the declarations its syntax shows.
    /// </summary>
    NewCompileError,
}

/// <summary>
/// What is wrong with a module a refactoring's edit would leave, and what it is about.
/// </summary>
/// <param name="Kind">What.</param>
/// <param name="Detail">The syntax error, the name, or the compile error's description; <see langword="null"/> when there is nothing to name.</param>
internal sealed record class EditedModuleProblem(EditedModuleProblemKind Kind, string? Detail);

/// <summary>
/// The checks every refactoring makes of a module its edit would leave, before the edit is offered: it parses, its names bind as they did, and it has no
/// compile error it did not have. What each refactoring checks of what its edit does is its own.
/// </summary>
internal static class EditedModuleCheck
{
    /// <summary>
    /// Whether the edited module parses.
    /// </summary>
    /// <param name="after">The module as the edit leaves it.</param>
    /// <returns>The first syntax error; <see langword="null"/> when it parses.</returns>
    public static EditedModuleProblem? ParseOf(ExtractionModuleAnalysis after)
        => after.Parse.IsSuccess ? null : new(EditedModuleProblemKind.DoesNotParse, after.Parse.SyntaxErrors.FirstOrDefault()?.Verbose);

    /// <summary>
    /// Whether the edited procedures bind every name they write, and have no compile error the procedures they replace did not have.
    /// </summary>
    /// <param name="before">The module as it is.</param>
    /// <param name="originals">The procedures the edit changes, as they are.</param>
    /// <param name="after">The module as the edit leaves it.</param>
    /// <param name="edited">The procedures the edit changes or adds, as it leaves them.</param>
    /// <returns>What is wrong; <see langword="null"/> when nothing is.</returns>
    public static EditedModuleProblem? Of(
        ExtractionModuleAnalysis before, IReadOnlyList<ExtractionProcedureAnalysis> originals,
        ExtractionModuleAnalysis after, IReadOnlyList<ExtractionProcedureAnalysis> edited)
    {
        // a variable declared by being named is one a procedure had, whichever procedure now names it.
        var implicitBefore = originals.SelectMany(ImplicitNamesOf).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (edited.SelectMany(ImplicitNamesOf).FirstOrDefault(implicitName => !implicitBefore.Contains(implicitName)) is { } unbound)
        {
            return new(EditedModuleProblemKind.NameDoesNotBind, unbound);
        }

        // an error is new when there are more of its kind than there were: the edit moves where every error is.
        var errorsBefore = before.DeclarationErrors.Concat(originals.SelectMany(procedure => procedure.Model.CompileErrors))
            .CountBy(error => error.ErrorId).ToDictionary();
        var errorsAfter = after.DeclarationErrors.Concat(edited.SelectMany(procedure => procedure.Model.CompileErrors)).ToArray();
        if (errorsAfter.CountBy(error => error.ErrorId)
            .FirstOrDefault(count => count.Value > errorsBefore.GetValueOrDefault(count.Key)) is { Key: var newError, Value: > 0 })
        {
            return new(EditedModuleProblemKind.NewCompileError, errorsAfter.First(error => error.ErrorId == newError).Description);
        }

        return null;
    }

    private static IEnumerable<string> ImplicitNamesOf(ExtractionProcedureAnalysis procedure)
        => procedure.Scope.Declarations.OfType<VBLocalVariableSymbol>().Where(local => local.DeclaredBy.IsImplicit()).Select(local => local.Name);
}
