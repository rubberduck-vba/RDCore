using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using System.Collections.Immutable;
using System.Globalization;
using System.Resources;

namespace RDCore.LanguageServer.Services.Refactoring.ExtractMethod;

/// <summary>
/// The user's answers for one parameter of the new procedure, in the order the user put the parameters in.
/// </summary>
/// <param name="Id">The parameter: the name of the variable it is given (<see cref="ExtractionParameter.Name"/>).</param>
/// <param name="Name">The name the user gave it; <see langword="null"/> to keep the variable's.</param>
/// <param name="Passing">How the user passes it, <c>ByVal</c> or <c>ByRef</c>; <see langword="null"/> to pass it as the plan does.</param>
internal sealed record class ExtractionParameterAnswer(string Id, string? Name = null, string? Passing = null);

/// <summary>
/// A parameter of the new procedure as the user chose it.
/// </summary>
/// <param name="Variable">The variable of the source procedure it is given.</param>
/// <param name="Name">Its name in the new procedure.</param>
/// <param name="Passing">How it is passed.</param>
/// <param name="Names">Where the selection names the variable, which the new procedure names as <paramref name="Name"/>: none when the name is the variable's.</param>
internal sealed record class ExtractionParameterChoice(SemanticId Variable, string Name, ExtractionPassing Passing, ImmutableArray<SourceRange> Names);

/// <summary>
/// What is wrong with the user's answers for the parameters.
/// </summary>
internal enum ExtractionParameterProblemKind
{
    /// <summary>
    /// The answer is of no parameter of the new procedure, or of one the answers name twice.
    /// </summary>
    NotAParameter,

    /// <summary>
    /// The name is empty.
    /// </summary>
    Empty,

    /// <summary>
    /// The name is not an identifier (<see cref="VBIdentifierValidity.NotAnIdentifier"/>).
    /// </summary>
    NotAnIdentifier,

    /// <summary>
    /// The name is a keyword (<see cref="VBIdentifierValidity.Reserved"/>).
    /// </summary>
    Reserved,

    /// <summary>
    /// The name is longer than <see cref="ExtractionName.MaximumLength"/> characters.
    /// </summary>
    TooLong,

    /// <summary>
    /// Another parameter of the new procedure is given the name.
    /// </summary>
    Duplicate,

    /// <summary>
    /// The new procedure has the name already: the selection names something else so, or the extraction declares it there (the new procedure's own name, a
    /// local, the copy of the source's result, the object of a <c>With</c> block).
    /// </summary>
    Collision,

    /// <summary>
    /// The selection writes the variable's name where what it means is not known, as in an expression the parser kept as text: it cannot be renamed there.
    /// </summary>
    NotKnownWhereWritten,

    /// <summary>
    /// The passing is neither <c>ByVal</c> nor <c>ByRef</c>.
    /// </summary>
    NotAPassing,

    /// <summary>
    /// The parameter is passed by reference, and passing it by value would change what the code does: what the selection writes to it is read after, or it
    /// is an array, a variable of a <c>Type</c>, a fixed-length string, a <c>Static</c> local, or the source's <c>ParamArray</c>.
    /// </summary>
    PassingIsNotAChoice,

    /// <summary>
    /// An <c>Optional</c> parameter is put before one that is not: VBA declares every <c>Optional</c> parameter after the others.
    /// </summary>
    OptionalBeforeRequired,
}

/// <summary>
/// What is wrong with the user's answers for a parameter.
/// </summary>
/// <param name="Kind">What is wrong.</param>
/// <param name="Parameter">The parameter: the name of the variable it is given.</param>
/// <param name="Name">The name or the passing the user gave it, when the problem is of either; for
/// <see cref="ExtractionParameterProblemKind.OptionalBeforeRequired"/>, the parameter that is not <c>Optional</c>.</param>
internal sealed record class ExtractionParameterProblem(ExtractionParameterProblemKind Kind, string Parameter, string? Name = null)
{
    private static readonly ResourceManager Messages = new("RDCore.LanguageServer.Services.Refactoring.ExtractMethod.ExtractMethodMessages", typeof(ExtractionParameterProblem).Assembly);

    /// <summary>
    /// What the problem says, in the language of the reader.
    /// </summary>
    /// <param name="culture">The language of the reader; the current UI culture by default.</param>
    public string ToMessage(CultureInfo? culture = null)
    {
        var format = Messages.GetString($"ExtractMethodParameter_{Kind}", culture ?? CultureInfo.CurrentUICulture) ?? Kind.ToString();
        return string.Format(culture ?? CultureInfo.CurrentUICulture, format, Parameter, Name, ExtractionName.MaximumLength);
    }
}

/// <summary>
/// The parameters of the new procedure as the user edits them: renamed, put in another order, or passed by reference where the plan passes them by value.
/// </summary>
/// <remarks>
/// <para>
/// A parameter the plan passes by value can be passed by reference: the new procedure does not write it, or writes it with a value that is never read after.
/// One the plan passes by reference stays so (MS-VBAL §5.3.1.5): passing it by value would lose what it writes, or VBA passes it by
/// reference only.
/// </para>
/// <para>
/// A new name is a valid identifier that the new procedure does not have already: no other parameter is given it, the selection names nothing else so, and the
/// extraction does not declare it there. The selection's names of the variable become the new name; the call passes the variable as it is named in the source.
/// </para>
/// </remarks>
internal static class ExtractionParameters
{
    /// <summary>
    /// Whether the user chooses how the parameter is passed: by value, as the plan passes it, or by reference.
    /// </summary>
    /// <param name="parameter">The parameter.</param>
    public static bool PassingIsAChoice(ExtractionParameter parameter) => parameter.Reason is ExtractionPassingReason.ReadOnly;

    /// <summary>
    /// The parameters of the new procedure as the user's answers make them, in the order of the plan with the answered ones put in the user's order; or what
    /// is wrong with the answers.
    /// </summary>
    /// <param name="answers">The user's answers, in the order the user put the parameters in. A parameter that is not answered is kept as it is, where it is.</param>
    /// <param name="plan">What the extraction takes.</param>
    /// <param name="source">The module, and the procedure the selection is in.</param>
    /// <param name="name">The name of the new procedure.</param>
    /// <param name="context">The workspace, whose modules are bound.</param>
    /// <param name="module">The address of the module in <paramref name="context"/> (<c>workspace#Module</c>).</param>
    public static (ImmutableArray<ExtractionParameterChoice> Parameters, ExtractionParameterProblem? Problem) Choose(
        IReadOnlyList<ExtractionParameterAnswer> answers, ExtractionPlan plan, ExtractionSource source, string name, RenameContext context, Uri module)
    {
        var byId = new Dictionary<string, ExtractionParameterAnswer>(StringComparer.OrdinalIgnoreCase);
        foreach (var answer in answers)
        {
            if (!plan.Parameters.Any(parameter => parameter.Name.Equals(answer.Id, StringComparison.OrdinalIgnoreCase)) || !byId.TryAdd(answer.Id, answer))
            {
                return ([], new(ExtractionParameterProblemKind.NotAParameter, answer.Id));
            }
        }

        // the answered parameters take the places of the answered ones, in the user's order; the others stay where they are.
        var ordered = new Queue<ExtractionParameter>(answers.Select(answer => plan.Parameters.First(parameter => parameter.Name.Equals(answer.Id, StringComparison.OrdinalIgnoreCase))));
        var parameters = InDeclarationOrder(plan).Select(parameter => byId.ContainsKey(parameter.Name) ? ordered.Dequeue() : parameter).ToArray();
        var optional = parameters.TakeWhile(parameter => !parameter.IsOptional).Count();
        if (parameters.Skip(optional).FirstOrDefault(parameter => !parameter.IsOptional) is { } required)
        {
            return ([], new(ExtractionParameterProblemKind.OptionalBeforeRequired, parameters[optional].Name, required.Name));
        }

        var chosen = new List<(ExtractionParameter Parameter, string Name, ExtractionPassing Passing)>();
        foreach (var parameter in parameters)
        {
            var answer = byId.GetValueOrDefault(parameter.Name);
            var passing = parameter.Passing;
            if (answer?.Passing is { } written)
            {
                if (PassingOf(written) is not { } answered)
                {
                    return ([], new(ExtractionParameterProblemKind.NotAPassing, parameter.Name, written));
                }

                passing = answered;
                if (passing != parameter.Passing && !PassingIsAChoice(parameter))
                {
                    return ([], new(ExtractionParameterProblemKind.PassingIsNotAChoice, parameter.Name, written));
                }
            }

            var given = answer?.Name?.Trim() is { } typed ? typed : parameter.Name;
            if (Validity(given, parameter.Name) is { } invalid)
            {
                return ([], invalid);
            }

            chosen.Add((parameter, given, passing));
        }

        if (chosen.GroupBy(each => each.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1) is { } twice)
        {
            return ([], new(ExtractionParameterProblemKind.Duplicate, twice.Last().Parameter.Name, twice.Key));
        }

        var renamed = chosen.Where(each => !each.Name.Equals(each.Parameter.Name, StringComparison.Ordinal)).ToArray();
        if (renamed.Length == 0)
        {
            return ([.. chosen.Select(each => new ExtractionParameterChoice(each.Parameter.Variable, each.Name, each.Passing, []))], null);
        }

        var syntax = context.Modules.FirstOrDefault(candidate => candidate.Module == module);
        var declared = ExtractMethodEdit.NewProcedureNamesOf(plan, source, name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selection = plan.Selection.Range;
        bool InTheSelection(SourceRange range) => range.Start >= selection.Start && range.End <= selection.End;
        var names = new Dictionary<SemanticId, ImmutableArray<SourceRange>>();
        foreach (var (parameter, given, _) in renamed)
        {
            // a name the new procedure has already, unless it is one of a parameter the user renames to another.
            var collides = declared.Contains(given);
            if (!collides && syntax is not null && !given.Equals(parameter.Name, StringComparison.OrdinalIgnoreCase))
            {
                var found = NameOccurrences.Find(context, given, [syntax]);
                collides = found.Occurrences.Concat(found.Unknown)
                    .Where(occurrence => occurrence.Kind is OccurrenceKind.Name or OccurrenceKind.Declaration && InTheSelection(occurrence.Range))
                    .Any(occurrence => occurrence.Binding is not { } binding
                        || !chosen.Any(each => each.Parameter.Variable == binding && !each.Name.Equals(given, StringComparison.OrdinalIgnoreCase)));
            }

            if (collides)
            {
                return ([], new(ExtractionParameterProblemKind.Collision, parameter.Name, given));
            }

            if (syntax is null)
            {
                return ([], new(ExtractionParameterProblemKind.NotKnownWhereWritten, parameter.Name, given));
            }

            var occurrences = NameOccurrences.Find(context, parameter.Name, [syntax]);
            if (occurrences.Unknown.Any(occurrence => occurrence.Kind is OccurrenceKind.Name && InTheSelection(occurrence.Range)))
            {
                return ([], new(ExtractionParameterProblemKind.NotKnownWhereWritten, parameter.Name, given));
            }

            names[parameter.Variable] = [.. occurrences.Occurrences
                .Where(occurrence => occurrence.Kind is OccurrenceKind.Name && occurrence.Binding == parameter.Variable && InTheSelection(occurrence.Range))
                .Select(occurrence => occurrence.Range)
                .Order()];
        }

        return ([.. chosen.Select(each => new ExtractionParameterChoice(each.Parameter.Variable, each.Name, each.Passing, names.GetValueOrDefault(each.Parameter.Variable, [])))], null);
    }

    /// <summary>
    /// The parameters of the plan in the order the new procedure declares them by default: those that are not <c>Optional</c>, then those that are, each in
    /// the order the selection first touches them.
    /// </summary>
    /// <param name="plan">What the extraction takes.</param>
    public static IEnumerable<ExtractionParameter> InDeclarationOrder(ExtractionPlan plan)
        => plan.Parameters.Where(parameter => !parameter.IsOptional).Concat(plan.Parameters.Where(parameter => parameter.IsOptional));

    /// <summary>
    /// The value of the answer that passes a parameter as <paramref name="passing"/> says.
    /// </summary>
    /// <param name="passing">How it is passed.</param>
    public static string ValueOf(ExtractionPassing passing) => passing is ExtractionPassing.ByRef ? "ByRef" : "ByVal";

    private static ExtractionPassing? PassingOf(string written)
        => written.Equals("ByVal", StringComparison.OrdinalIgnoreCase) ? ExtractionPassing.ByVal
            : written.Equals("ByRef", StringComparison.OrdinalIgnoreCase) ? ExtractionPassing.ByRef
            : null;

    private static ExtractionParameterProblem? Validity(string name, string parameter)
    {
        switch (VBIdentifier.Validate(name))
        {
            case VBIdentifierValidity.Empty:
                return new(ExtractionParameterProblemKind.Empty, parameter);
            case VBIdentifierValidity.NotAnIdentifier:
                return new(ExtractionParameterProblemKind.NotAnIdentifier, parameter, name);
            case VBIdentifierValidity.Reserved:
                return new(ExtractionParameterProblemKind.Reserved, parameter, name);
        }

        return name.Length > ExtractionName.MaximumLength ? new(ExtractionParameterProblemKind.TooLong, parameter, name) : null;
    }
}
