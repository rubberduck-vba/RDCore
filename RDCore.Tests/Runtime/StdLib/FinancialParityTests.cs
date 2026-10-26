using System.Globalization;
using RDCore.Runtime.StdLib;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// The financial functions and the tangent as MS-VBA computes them, to the last bit: random calls of each, recorded in Excel (grid <c>oracle\s43</c>,
/// seed 4343 - R and T), as the eight bytes of the <c>Double</c> each returns, or the error it raises.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.6 Financial")]
public sealed class FinancialParityTests
{
    private static readonly StdFinancial Financial = new(NSubstitute.Substitute.For<IRuntimeSession>());
    private static readonly StdMath Math = new(NSubstitute.Substitute.For<IRuntimeSession>());

    private static VBVariantValue Variant(double value) => new(new VBDoubleValue(value));

    private static RuntimeSemanticsEvaluationResult<VBDoubleValue> Call(string function, double[] a) => function switch
    {
        "PV" => Financial.PV(new(a[0]), new(a[1]), new(a[2]), Variant(a[3]), Variant(a[4])),
        "FV" => Financial.FV(new(a[0]), new(a[1]), new(a[2]), Variant(a[3]), Variant(a[4])),
        "Pmt" => Financial.Pmt(new(a[0]), new(a[1]), new(a[2]), Variant(a[3]), Variant(a[4])),
        "PPmt" => Financial.PPmt(new(a[0]), new(a[1]), new(a[2]), new(a[3]), Variant(a[4]), Variant(a[5])),
        "IPmt" => Financial.IPmt(new(a[0]), new(a[1]), new(a[2]), new(a[3]), Variant(a[4]), Variant(a[5])),
        "NPer" => Financial.NPer(new(a[0]), new(a[1]), new(a[2]), Variant(a[3]), Variant(a[4])),
        "Rate" => Financial.Rate(new(a[0]), new(a[1]), new(a[2]), Variant(a[3]), Variant(a[4])),
        "DDB" => Financial.DDB(new(a[0]), new(a[1]), new(a[2]), new(a[3]), Variant(a[4])),
        "SYD" => Financial.SYD(new(a[0]), new(a[1]), new(a[2]), new(a[3])),
        _ => Financial.SLN(new(a[0]), new(a[1]), new(a[2])),
    };

    [TestMethod]
    [DataRow("PV", "0.155036, 54, -2640.401986, -19655.961785, 1", "0x40D335D2A8D575F5")]
    [DataRow("PV", "0.058536, 376, -209.779202, -727.528623, 1", "0x40ADA3160D903A7D")]
    [DataRow("PV", "0.08502, 349, -542.650596, -14405.717419, 1", "0x40BB0D46018DCFA9")]
    [DataRow("PV", "0.138082, 53, -4549.989627, 4344.834566, 0", "0x40E01201B12579A3")]
    [DataRow("PV", "0.107936, 94, -124.457421, 17111.079524, 0", "0x4091FF7D2AC739E7")]
    [DataRow("PV", "0.001449, 69, -4412.765652, -10653.399281, 0", "0x411242EE50EC57C1")]
    [DataRow("PV", "0.140779, 128, -3319.491716, -4269.625835, 0", "0x40D706DCE37ACEF8")]
    [DataRow("PV", "0.195297, 167, -2498.120576, -16883.132251, 1", "0x40CDDCC1AB9F8F6C")]
    [DataRow("PV", "0.047211, 173, -2449.902204, -18983.330018, 1", "0x40EA874D7FB49163")]
    [DataRow("PV", "0.234324, 90, -604.836984, 7290.230537, 0", "0x40A42A6618F2D642")]
    [DataRow("FV", "0.155036, 54, -2640.401986, 270450.28114, 1", "0xC1C1EF7D1608839A")]
    [DataRow("FV", "0.058536, 376, -209.779202, 162377.334666, 1", "0xC2F18C9722B0C71C")]
    [DataRow("FV", "0.08502, 349, -542.650596, 322530.124011, 1", "0xC3A46E0E6D07A771")]
    [DataRow("FV", "0.138082, 53, -4549.989627, 463120.130317, 0", "0xC1B854A0C947A3A0")]
    [DataRow("FV", "0.107936, 94, -124.457421, 55488.540774, 0", "0xC1C8C204B8675729")]
    [DataRow("FV", "0.001449, 69, -4412.765652, 260731.491661, 0", "0x40DF1B06E4D10D7E")]
    [DataRow("FV", "0.140779, 128, -3319.491716, 284421.216086, 0", "0xC293E87E8F5069E7")]
    [DataRow("FV", "0.195297, 167, -2498.120576, 223860.83883, 1", "0xC3B91EFF5781BFC1")]
    [DataRow("FV", "0.047211, 173, -2449.902204, 104836.623619, 1", "0xC1A19AC7D491BD8B")]
    [DataRow("FV", "0.234324, 90, -604.836984, 458890.180115, 0", "0xC2D19065F5124DAC")]
    [DataRow("Pmt", "0.155036, 54, 270450.28114, -19655.961785, 1", "0xC0E1BB70D1A8BE45")]
    [DataRow("Pmt", "0.058536, 376, 162377.334666, -727.528623, 1", "0xC0C189A74A4CC8F6")]
    [DataRow("Pmt", "0.08502, 349, 322530.124011, -14405.717419, 1", "0xC0D8AE343E488ABE")]
    [DataRow("Pmt", "0.138082, 53, 463120.130317, 4344.834566, 0", "0xC0EF4214EA62FB57")]
    [DataRow("Pmt", "0.107936, 94, 55488.540774, 17111.079524, 0", "0xC0B765B94497A42E")]
    [DataRow("Pmt", "0.001449, 69, 260731.491661, -10653.399281, 0", "0xC0ADE529099AABE7")]
    [DataRow("Pmt", "0.140779, 128, 284421.216086, -4269.625835, 0", "0xC0E38D112909386B")]
    [DataRow("Pmt", "0.195297, 167, 223860.83883, -16883.132251, 1", "0xC0E1DC0479B27723")]
    [DataRow("Pmt", "0.047211, 173, 104836.623619, -18983.330018, 1", "0xC0B277A1E7CECB7E")]
    [DataRow("Pmt", "0.234324, 90, 458890.180115, 7290.230537, 0", "0xC0FA408FBB3AFA65")]
    [DataRow("PPmt", "0.155036, 45, 54, 270450.28114, -19655.961785, 1", "0xC0BF20929E69C12C")]
    [DataRow("PPmt", "0.058536, 92, 376, 162377.334666, -727.528623, 1", "0xBF4AA57514000000")]
    [DataRow("PPmt", "0.08502, 184, 349, 322530.124011, -14405.717419, 1", "0xBFA0313906580000")]
    [DataRow("PPmt", "0.138082, 51, 53, 463120.130317, 4344.834566, 0", "0xC0E56767C73C3E6E")]
    [DataRow("PPmt", "0.107936, 75, 94, 55488.540774, 17111.079524, 0", "0xC08F86F798DCAF00")]
    [DataRow("PPmt", "0.001449, 27, 69, 260731.491661, -10653.399281, 0", "0xC0ABFA2E4B878E13")]
    [DataRow("PPmt", "0.140779, 71, 128, 284421.216086, -4269.625835, 0", "0xC032FA48C13E8000")]
    [DataRow("PPmt", "0.195297, 138, 167, 223860.83883, -16883.132251, 1", "0xC064093185313A00")]
    [DataRow("PPmt", "0.047211, 118, 173, 104836.623619, -18983.330018, 1", "0xC07246A3B20617F0")]
    [DataRow("PPmt", "0.234324, 17, 90, 458890.180115, 7290.230537, 0", "0xBF932ECBBF800000")]
    [DataRow("IPmt", "0.155036, 45, 54, 270450.28114, -19655.961785, 1", "0xC0DBAEBCFBB70C3F")]
    [DataRow("IPmt", "0.058536, 92, 376, 162377.334666, -727.528623, 1", "0xC0C189A72FA753E2")]
    [DataRow("IPmt", "0.08502, 184, 349, 322530.124011, -14405.717419, 1", "0xC0D8AE32382169F3")]
    [DataRow("IPmt", "0.138082, 51, 53, 463120.130317, 4344.834566, 0", "0xC0D3B55A464D79D1")]
    [DataRow("IPmt", "0.107936, 75, 94, 55488.540774, 17111.079524, 0", "0xC0B374DA517C0E4E")]
    [DataRow("IPmt", "0.001449, 27, 69, 260731.491661, -10653.399281, 0", "0xC06EAFABE131DD44")]
    [DataRow("IPmt", "0.140779, 71, 128, 284421.216086, -4269.625835, 0", "0xC0E38AB1DFF1109B")]
    [DataRow("IPmt", "0.195297, 138, 167, 223860.83883, -16883.132251, 1", "0xC0E1C7FB482D45E9")]
    [DataRow("IPmt", "0.047211, 118, 173, 104836.623619, -18983.330018, 1", "0xC0B15337ACAE69FF")]
    [DataRow("IPmt", "0.234324, 17, 90, 458890.180115, 7290.230537, 0", "0xC0FA408F6E7FCB67")]
    [DataRow("NPer", "0.155036, -2640.401986, 270450.28114, -19655.961785, 1", "Error5")]
    [DataRow("NPer", "0.058536, -209.779202, 162377.334666, -727.528623, 1", "Error5")]
    [DataRow("NPer", "0.08502, -542.650596, 322530.124011, -14405.717419, 1", "0xC046EE3ABD623BA4")]
    [DataRow("NPer", "0.138082, -4549.989627, 463120.130317, 4344.834566, 0", "Error5")]
    [DataRow("NPer", "0.107936, -124.457421, 55488.540774, 17111.079524, 0", "Error5")]
    [DataRow("NPer", "0.001449, -4412.765652, 260731.491661, -10653.399281, 0", "0x404DB26B7B4FADE0")]
    [DataRow("NPer", "0.140779, -3319.491716, 284421.216086, -4269.625835, 0", "Error5")]
    [DataRow("NPer", "0.195297, -2498.120576, 223860.83883, -16883.132251, 1", "0xC03B52AE66B4D1F4")]
    [DataRow("NPer", "0.047211, -2449.902204, 104836.623619, -18983.330018, 1", "Error5")]
    [DataRow("NPer", "0.234324, -604.836984, 458890.180115, 7290.230537, 0", "Error5")]
    [DataRow("Rate", "54, -2640.401986, 270450.28114, -19655.961785, 1", "0xBF902812A2910AEC")]
    [DataRow("Rate", "376, -209.779202, 162377.334666, -727.528623, 1", "0xBF6BD849C4D2EC6B")]
    [DataRow("Rate", "349, -542.650596, 322530.124011, -14405.717419, 1", "0xBF62D6784D3A910D")]
    [DataRow("Rate", "53, -4549.989627, 463120.130317, 4344.834566, 0", "0xBF975689E052B3EF")]
    [DataRow("Rate", "94, -124.457421, 55488.540774, 17111.079524, 0", "Error5")]
    [DataRow("Rate", "69, -4412.765652, 260731.491661, -10653.399281, 0", "0x3F763B288B1AD695")]
    [DataRow("Rate", "128, -3319.491716, 284421.216086, -4269.625835, 0", "0x3F7BFB4305D97D09")]
    [DataRow("Rate", "167, -2498.120576, 223860.83883, -16883.132251, 1", "0x3F820A562894E0B8")]
    [DataRow("Rate", "173, -2449.902204, 104836.623619, -18983.330018, 1", "0x3F9822D75F9FDAC5")]
    [DataRow("Rate", "90, -604.836984, 458890.180115, 7290.230537, 0", "0xBFA93DDF3F0919E6")]
    [DataRow("DDB", "279900.471769, 379.860759, 12, 11, 1.2203", "0x40C305D30000BD47")]
    [DataRow("DDB", "873781.751758, 172.664624, 38, 38, 1.483558", "0x40BE8858183F4D10")]
    [DataRow("DDB", "721836.563248, 435.130834, 18, 3, 2.95994", "0x40F43B728680C8BD")]
    [DataRow("DDB", "693255.705962, 207.004372, 31, 29, 1.98838", "0x40BB24F31F116DC3")]
    [DataRow("DDB", "154565.683518, 115.543111, 12, 3, 1.277005", "0x40C9A6F4E7387A43")]
    [DataRow("DDB", "812873.944737, 489.997144, 16, 13, 1.702882", "0x40D5E4ED7AE428A4")]
    [DataRow("DDB", "951731.235764, 199.533142, 38, 9, 2.813928", "0x40E2987C47A96FE4")]
    [DataRow("DDB", "928389.432555, 475.840036, 14, 13, 1.874691", "0x40D5A0AD5F62428D")]
    [DataRow("DDB", "460244.965667, 312.810863, 19, 11, 2.220267", "0x40CE5121F3C0BC3C")]
    [DataRow("DDB", "956933.564506, 105.359226, 25, 19, 2.636816", "0x40CA82FC48D34D79")]
    [DataRow("SYD", "279900.471769, 379.860759, 12, 11", "0x40BBFF31F59E8831")]
    [DataRow("SYD", "873781.751758, 172.664624, 38, 38", "0x40926BD6A9B541E7")]
    [DataRow("SYD", "721836.563248, 435.130834, 18, 3", "0x40F07AB8C9A73305")]
    [DataRow("SYD", "693255.705962, 207.004372, 31, 29", "0x40B05FD3AABF130E")]
    [DataRow("SYD", "154565.683518, 115.543111, 12, 3", "0x40D35653340E0E80")]
    [DataRow("SYD", "812873.944737, 489.997144, 16, 13", "0x40D75569502862B7")]
    [DataRow("SYD", "951731.235764, 199.533142, 38, 9", "0x40E2CF719F71CB7B")]
    [DataRow("SYD", "928389.432555, 475.840036, 14, 13", "0x40D142A2DB0B5E2B")]
    [DataRow("SYD", "460244.965667, 312.810863, 19, 11", "0x40D54690A341B908")]
    [DataRow("SYD", "956933.564506, 105.359226, 25, 19", "0x40D42026E140B604")]
    [DataRow("SLN", "279900.471769, 379.860759, 12", "0x40D6BF589790CEA8")]
    [DataRow("SLN", "873781.751758, 172.664624, 38", "0x40D6736D9ED4E851")]
    [DataRow("SLN", "721836.563248, 435.130834, 18", "0x40E391BB6F768C96")]
    [DataRow("SLN", "693255.705962, 207.004372, 31", "0x40D5D51A38FEC413")]
    [DataRow("SLN", "154565.683518, 115.543111, 12", "0x40C9236C2A12460C")]
    [DataRow("SLN", "812873.944737, 489.997144, 16", "0x40E8CABFE52AE8E2")]
    [DataRow("SLN", "951731.235764, 199.533142, 38", "0x40D87413B5AD8886")]
    [DataRow("SLN", "928389.432555, 475.840036, 14", "0x40F02E78AD5AA848")]
    [DataRow("SLN", "460244.965667, 312.810863, 19", "0x40D7A3BD272C94B3")]
    [DataRow("SLN", "956933.564506, 105.359226, 25", "0x40E2B0241A4E5FE0")]
    public void AFinancialFunction_IsMsVbasToTheLastBit(string function, string arguments, string expected)
    {
        var result = Call(function, [.. arguments.Split(',').Select(argument => double.Parse(argument, CultureInfo.InvariantCulture))]);
        if (expected == "Error5")
        {
            Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, result.ErrorInfo?.ErrorId);
            return;
        }

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Verbose);
        Assert.AreEqual(expected[2..], BitConverter.DoubleToInt64Bits(result.Result!.Value).ToString("X16"));
    }

    // MS-VBA's tangent is its sine over its cosine, divided in 80 bits.
    [TestMethod]
    [DataRow(-14797.699062525, 0xBFF065BC55841FA2UL)]
    [DataRow(-0.299390864, 0xBFD3C13B6B364CBBUL)]
    [DataRow(22.756590775, 0x3FEEBF6599C5B61DUL)]
    [DataRow(2619.288073675, 0xBFF07B15289EE270UL)]
    [DataRow(-0.03912879, 0xBFA40B4ECCE6AD12UL)]
    [DataRow(0.004531384, 0x3F728F8874B844ECUL)]
    [DataRow(-0.00029018, 0xBF330469A51A75A8UL)]
    [DataRow(-0.459530501, 0xBFDFABDAB80F3B56UL)]
    [DataRow(0.484562706, 0x3FE0D877429BEDDCUL)]
    [DataRow(-216.867884489, 0xBFB92A992B27E664UL)]
    [DataRow(1.940461144, 0xC004A5777D83E158UL)]
    [DataRow(-3062.332434609, 0x3FEC16C72E2D8372UL)]
    [DataRow(262.262974289, 0x4030A4ABC104E66DUL)]
    [DataRow(0.003041009, 0x3F68E97A4ED642B3UL)]
    [DataRow(700.70591555, 0x3FC0D52027AE7562UL)]
    [DataRow(4944.868730355, 0x3F5F065CF5096AE1UL)]
    [DataRow(22.91728485, 0x3FF5482ADF9F0662UL)]
    [DataRow(-0.005028945, 0xBF749946864A6A38UL)]
    [DataRow(-1402.654562333, 0xC02E013C16B03451UL)]
    public void Tan_IsTheSineOverTheCosine(double angle, ulong expected)
        => Assert.AreEqual(expected, (ulong)BitConverter.DoubleToInt64Bits(Math.Tan(new(angle)).Result!.Value));

    // MS-VBA's sine and cosine are within a unit of the last place of the true value, as the platform's are, and the platform's to the last bit for
    // all but about one argument in forty - these (grid oracle\s43 M01, M02).
    private const string LastBit = "Known gap: MS-VBA's sine and cosine differ from the platform's in the last bit for about one argument in forty.";

    [TestMethod]
    [Ignore(LastBit)]
    public void Sin_OfTwoAndAHalf_IsMsVbas()
        => Assert.AreEqual(0x3FE326AF0DCFCAB1UL, (ulong)BitConverter.DoubleToInt64Bits(Math.Sin(new(2.5)).Result!.Value));

    [TestMethod]
    [Ignore(LastBit)]
    public void Cos_OfOne_IsMsVbas()
        => Assert.AreEqual(0x3FE14A280FB5068BUL, (ulong)BitConverter.DoubleToInt64Bits(Math.Cos(new(1)).Result!.Value));
}