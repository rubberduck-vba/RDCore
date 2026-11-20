using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §5.3.1.11: the arguments of a module's code that a call takes by reference, where the procedure called is followed, by the range each is written
// at. Assigned: those the call is known to assign, which a count states are assigned through the call (DeclarationReference.AssignedThroughCall), whether
// or not the rest of the code was analyzed. Read: those that no procedure of the chain assigns, once every procedure the chain goes through was fully
// analyzed, which a count reads. A module's own count follows the calls of its own procedures (ArgumentParameters); a count across the project follows
// the calls of the procedures of its standard modules as well, from what the code of each module does with its parameters (Flow).
public sealed record class ByRefArguments(IReadOnlySet<SourceRange> Assigned, IReadOnlySet<SourceRange> Read)
{
    public ByRefFlow? Flow { get; init; }
}

// what the code of a module does with the parameters of its procedures, and with the arguments it passes by reference: Parameters, those the module
// declares; Assigned, those its code assigns; Arguments, each argument its code passes by reference; IsFullyAnalyzed, whether every procedure of the module
// was.
public sealed record class ByRefFlow(
    IReadOnlySet<SemanticId> Parameters, IReadOnlySet<SemanticId> Assigned, IReadOnlyList<ByRefArgument> Arguments, bool IsFullyAnalyzed);

// an argument passed by reference, written at Range, that names Binding. Parameter: the parameter it is given, where the procedure called is known and is
// neither a Declare nor a library's, and the parameter is not a ParamArray. CallsItsOwnProcedure: the procedure called is one of the module's own, called
// by its name or through Me (ArgumentParameters).
public readonly record struct ByRefArgument(SourceRange Range, SemanticId Binding, SemanticId? Parameter, bool CallsItsOwnProcedure);
