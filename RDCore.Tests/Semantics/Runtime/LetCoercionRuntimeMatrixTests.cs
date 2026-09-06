using NSubstitute;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Characterization matrix for <see cref="VBNumericLetCoercionTypeRuntimeSemantics"/> success paths.
/// Mirrors the static-semantics matrix style (a data-row table, result asserted by type and value);
/// this is the seed for a broader runtime-semantics harness.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.5.1.2.1 Let-Coercion between numeric types")]
public sealed class LetCoercionRuntimeMatrixTests
{
    private static readonly VBNumericLetCoercionTypeRuntimeSemantics _sut =
        new(Substitute.For<IVerboseMessageBuilder>(), Substitute.For<ILetCoercionRuntimeSemanticsProvider>());

    private static VBTypedValue Coerce(VBTypedValue source, VBType destination)
    {
        // resolver/expression are only dereferenced on the error (overflow) paths, which these
        // in-range rows never hit.
        var frame = new LetCoercionStackFrame(default, InputIndex.CoercionSourceValue, source, new VBTypeDescValue(destination));
        var result = _sut.EvaluateLetCoercion(null!, null!, frame);
        Assert.IsTrue(result.IsSuccess, $"coercion {source.TypeInfo.Name} -> {destination.Name} was not successful");
        return result.Result!;
    }

    [DataTestMethod]
    [DataRow(2.5, (short)2)]    // banker's rounding: tie -> even
    [DataRow(3.5, (short)4)]    // banker's rounding: tie -> even
    [DataRow(2.67, (short)3)]   // regression guard: BankersRounding used to truncate this to 2
    [DataRow(-2.67, (short)-3)]
    [DataRow(10.2, (short)10)]
    public void FloatToInteger_RoundsWithBankersRounding(double source, short expected)
    {
        var result = Coerce(new VBDoubleValue(source), VBIntegerType.TypeInfo);
        Assert.IsInstanceOfType<VBIntegerValue>(result);
        Assert.AreEqual(expected, ((VBIntegerValue)result).Value);
    }
}
