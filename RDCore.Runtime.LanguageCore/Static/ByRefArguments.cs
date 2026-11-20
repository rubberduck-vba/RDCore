using RDCore.SDK.Model.Source;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §5.3.1.11: the arguments of a module's code that a call takes by reference, where the procedure called is one of the module's own
// (ArgumentParameters), by the range each is written at. Assigned: those the call is known to assign, which a count states are assigned through the
// call (DeclarationReference.AssignedThroughCall), whether or not the rest of the module was analyzed. Read: those that no procedure of the chain assigns,
// once every procedure of the module was fully analyzed, which a count reads.
public sealed record class ByRefArguments(IReadOnlySet<SourceRange> Assigned, IReadOnlySet<SourceRange> Read);
