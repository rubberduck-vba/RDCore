using RDCore.Runtime.LanguageCore.Errors;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.Model.Errors;

[TestClass]
public sealed class LanguageErrorFactoryTests
{
    [TestMethod]
    public void ACompileError_IsAVbcCode_OfItsNumber()
        => Assert.AreEqual("VBC09303", VBCompileErrorId.DuplicateDeclaration.ToCode().ToString());

    [TestMethod]
    public void ARuntimeError_IsAVbrCode_OfItsNumber()
        => Assert.AreEqual("VBR00011", VBRuntimeErrorId.DivisionByZero.ToCode().ToString());

    [TestMethod]
    public void ARuntimeErrorOfACode_IsTheErrorOfItsId()
    {
        var location = TestLocations.TestLocation;

        Assert.AreEqual(
            VBRuntimeErrorInfo.For(VBRuntimeErrorId.FileNotFound, location, "Kill"),
            VBRuntimeErrorInfo.For(VBRuntimeErrorId.FileNotFound.ToCode(), location, "Kill"));
    }

    [TestMethod]
    public void ACodeOfAnotherFamily_IsNoRuntimeError()
        => Assert.ThrowsExactly<ArgumentException>(() => VBRuntimeErrorInfo.For(VBCompileErrorId.DuplicateDeclaration.ToCode(), TestLocations.TestLocation, "x"));

    [TestMethod]
    public void ACompileError_TravelsWithItsCodeAndItsMessage()
    {
        var error = VBCompileErrorInfo.For(VBCompileErrorId.DuplicateDeclaration, TestLocations.TestLocation, "Total");

        var travelled = PlatformJson.Deserialize<CompileErrorDto>(PlatformJson.Serialize(CompileErrorDto.From(error)));

        Assert.AreEqual(new RDCoreDiagnosticCode(RDCoreDiagnosticCode.CompilePrefix, (int)VBCompileErrorId.DuplicateDeclaration), travelled.Code);
        Assert.AreEqual(error, travelled.ToInfo());
    }

    [TestMethod]
    public void AnApplicationError_RaisedWithNoDescription_HasTheMessageOfItsNumber()
        => Assert.AreEqual(
            VBRuntimeErrorInfo.GetErrorString(VBRuntimeErrorId.DivisionByZero),
            VBApplicationErrorInfo.Raised((int)VBRuntimeErrorId.DivisionByZero, TestLocations.TestLocation, "Err.Raise 11").Description);
}
