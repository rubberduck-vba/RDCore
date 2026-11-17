namespace RDCore.Runtime.StdLib;

// marks a double parameter of a typed form (StdLibIntrinsicAttribute), its out parameter or its return value as a Date's serial: the parameter takes an
// argument of the Date type, and the form returns a Date (ProcedureAnalysis.Intrinsics.cs).
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.ReturnValue)]
internal sealed class StdLibDateAttribute : Attribute { }
