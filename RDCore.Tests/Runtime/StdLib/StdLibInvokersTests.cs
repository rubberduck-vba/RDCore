using RDCore.Runtime.StdLib;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Runtime.Shared;
using System.Reflection;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// Every member of the standard library's declarations that returns a result, and takes no argument by reference, is invoked by the call generated for it
/// (IR lowering S1.9): none is left to reflection by an oversight of the generator.
/// </summary>
[TestClass]
public sealed class StdLibInvokersTests
{
    private static IEnumerable<(Type Declaration, MethodInfo Method)> Members()
        => typeof(IStdConversionModule).Assembly.GetExportedTypes()
            .Where(type => type is { IsInterface: true, Namespace: "RDCore.SDK.Runtime.Abstract.StdLib" } && type.Name.StartsWith("IStd"))
            .SelectMany(type => type.GetMethods().Select(method => (type, method)));

    private static bool ReturnsAResult(MethodInfo method)
        => method.ReturnType == typeof(RuntimeSemanticsEvaluationResult)
            || method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(RuntimeSemanticsEvaluationResult<>);

    [TestMethod]
    public void EveryMemberThatReturnsAResult_HasAnInvoker()
    {
        var members = Members().Where(member => ReturnsAResult(member.Method) && !member.Method.GetParameters().Any(parameter => parameter.ParameterType.IsByRef)).ToArray();

        var missing = members.Where(member => StdLibInvokers.For(StdLibSymbolReader.ExternalTargetOf(member.Declaration, member.Method)) is null)
            .Select(member => StdLibSymbolReader.ExternalTargetOf(member.Declaration, member.Method))
            .ToArray();

        Assert.IsNotEmpty(members);
        Assert.IsEmpty(missing, string.Join(", ", missing));
    }

    [TestMethod]
    public void AKeyNoMemberHas_HasNoInvoker()
        => Assert.IsNull(StdLibInvokers.For("IStdConversionModule.CLng/7"));
}
