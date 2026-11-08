using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.Platform;

/// <summary>
/// The details of a run-time error travel with the result of the run, from the environment host through the language server to the client, as the
/// transport writes them: each part of them is read back as it was written.
/// </summary>
[TestClass]
public sealed class ExecuteResultErrorDetailsWireTests
{
    private static readonly SourceLocation Location = new(Wire.FileUri(Path.Combine(Path.GetTempPath(), "Module1.bas")), new SourceRange(4, 8, 4, 13));

    private static readonly VBErrorDetails Details = new("the value is out of range",
        new VBErrorExpression("VBBinaryOperatorExpressionNode", "/Module1.bas#4.0.2", Location),
        [
            new VBErrorFrame("/Module1.bas#4.0.2", [new VBErrorOperand("Long", "40000"), new VBErrorOperand("TypeDesc", null)],
                new VBErrorCoercion(new VBErrorOperand("Long", "40000"), "Integer")),
            new VBErrorFrame("/Module1.bas#4.0", [new VBErrorOperand("String", "")]),
        ]);

    [TestMethod]
    public void TheDetailsOfARunTimeError_AreReadBackAsTheyWereWritten()
    {
        var read = Wire.RoundTrip(new ExecuteResult { Outcome = ExecutionOutcome.RuntimeError, ErrorNumber = 6, ErrorDetails = Details }).ErrorDetails;

        Assert.IsNotNull(read);
        Assert.AreEqual(Details.Message, read.Message);
        Assert.AreEqual(Details.Expression, read.Expression);
        Assert.HasCount(2, read.Frames);
        for (var index = 0; index < Details.Frames.Count; index++)
        {
            var (written, back) = (Details.Frames[index], read.Frames[index]);
            Assert.AreEqual(written.NodeId, back.NodeId);
            CollectionAssert.AreEqual(written.Inputs.ToArray(), back.Inputs.ToArray(), $"frame {index}'s inputs");
            Assert.AreEqual(written.Coercion, back.Coercion, $"frame {index}'s coercion");
        }
    }

    [TestMethod]
    public void DetailsWhoseFramesWereCutShort_AreReadBackSayingSo()
        => Assert.IsTrue(Wire.RoundTrip(new ExecuteResult { Outcome = ExecutionOutcome.RuntimeError, ErrorDetails = Details with { FramesTruncated = true } })
            .ErrorDetails!.FramesTruncated);

    [TestMethod]
    public void AResultWithoutDetails_IsReadBackWithout()
        => Assert.IsNull(Wire.RoundTrip(new ExecuteResult { Outcome = ExecutionOutcome.Completed }).ErrorDetails);
}
