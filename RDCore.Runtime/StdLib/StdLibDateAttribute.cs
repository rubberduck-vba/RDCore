namespace RDCore.Runtime.StdLib;

// marks a double parameter of a typed form (StdLibIntrinsicAttribute), its out parameter or its return value as the serial of a Date: compiled code gives
// the parameter the payload of a Date, and never a Double's, and the form returns a Date (ProcedureAnalysis.Intrinsics.cs).
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.ReturnValue)]
internal sealed class StdLibDateAttribute : Attribute
{
}
