namespace RDCore.Runtime.StdLib;

// marks a static method of an implementation of a module of the standard library as the typed form of a member of the module: what the member returns for
// arguments of the method's parameter types, which the member's implementation calls for them, and which compiled code calls in place of the member when it
// knows its arguments to be of those types (ProcedureAnalysis.Intrinsics.cs). A parameter is a string (a String), a long (a whole number: a Byte, an Integer,
// a Long or a LongLong) or a double (a Double). The method returns an int (a Long) or a double (a Double), or a bool and that result in an out parameter:
// false when the member does not return it for those arguments, which leaves the call to the interpreter.
[AttributeUsage(AttributeTargets.Method)]
internal sealed class StdLibIntrinsicAttribute(Type module, string member) : Attribute
{
    // the interface of the module the member is declared in.
    public Type Module { get; } = module;

    // the member's name, as the module declares it.
    public string Member { get; } = member;
}
