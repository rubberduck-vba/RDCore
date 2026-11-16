namespace RDCore.Runtime.StdLib;

// marks a static method of an implementation of a module of the standard library as the typed form of a member of the module: what the member returns for
// arguments of the method's parameter types, which the member's implementation calls for them, and which compiled code calls in place of the member when it
// knows its arguments to be of those types (ProcedureAnalysis.Intrinsics.cs). A parameter is a string (a String), a short (an Integer), an int (a Long), a
// long (a LongLong) or a double (a Double). The method returns one of those, or a bool and that result in an out parameter: false when the member does not
// return it for those arguments, which leaves the call to the interpreter. A method marked for several members is the typed form of each (Left and Left$).
// A typed form compares Strings as Option Compare Binary does: a member whose comparison left out is the calling module's Option Compare takes its form
// only in a module that compares that way.
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
internal sealed class StdLibIntrinsicAttribute(Type module, string member) : Attribute
{
    // the interface of the module the member is declared in.
    public Type Module { get; } = module;

    // the member's name, as the module declares it.
    public string Member { get; } = member;

    // whether the member let-coerces each of its Variant parameters that the form takes a number for to that number's type (Mid's Length, InStr's start),
    // so that a number that widens to it is given as the call would coerce it; a Variant parameter otherwise takes only its own type, which can be what
    // the member returns (Abs).
    public bool LetCoerces { get; init; }
}
