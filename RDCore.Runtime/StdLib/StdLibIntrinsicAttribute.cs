namespace RDCore.Runtime.StdLib;

// marks a static method of an implementation of a module of the standard library as the typed form of a member of the module: what the member returns for
// arguments of the method's parameter types, which the member's implementation calls for them, and which compiled code calls in place of the member when it
// knows its arguments to be of those types (ProcedureAnalysis.Intrinsics.cs). A parameter is a string (a String), a short (an Integer), an int (a Long, or
// an enumeration's), a long (a LongLong) or a double (a Double, or a Date's serial where it is marked StdLibDateAttribute), which takes an argument of its
// type, and also a whole number that widens to it where the member declares the parameter of a type of its own rather than a Variant. An int marked
// StdLibOptionCompareAttribute is also given for a Compare the call leaves out, as the calling module's Option Compare; and an IRuntimeSession is the
// session the call is made in, which is not one of the member's parameters. The method returns a short, an int, a long, a double (a Date where it is
// marked) or a bool (a Boolean), or a bool and that result in an out parameter: false when the member does not return it for those arguments, which leaves
// the call to the interpreter.
[AttributeUsage(AttributeTargets.Method)]
internal sealed class StdLibIntrinsicAttribute(Type module, string member) : Attribute
{
    // the interface of the module the member is declared in.
    public Type Module { get; } = module;

    // the member's name, as the module declares it.
    public string Member { get; } = member;
}
