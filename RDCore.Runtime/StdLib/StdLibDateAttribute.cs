namespace RDCore.Runtime.StdLib;

// marks a double parameter of a typed form (StdLibIntrinsicAttribute) as the serial of a Date: compiled code gives it the payload of a Date, and never a
// Double's (ProcedureAnalysis.Intrinsics.cs).
[AttributeUsage(AttributeTargets.Parameter)]
internal sealed class StdLibDateAttribute : Attribute
{
}
