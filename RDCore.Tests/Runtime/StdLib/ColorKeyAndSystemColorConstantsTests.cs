using RDCore.Tests.Cli;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.2.1, §6.1.2.9, §6.1.2.12</strong>: the ColorConstants, KeyCodeConstants and SystemColorConstants modules, as MS-VBA has them - every
/// name, its type and its value, recorded in Excel (oracle\s27 grid-k, grid-p). A name MS-VBA does not have (<c>vbMajenta</c>, <c>vbButtonHighlight</c>) is
/// an undeclared variable there, as it is here.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.1 ColorConstants Module")]
[TestCategory("MS-VBAL 6.1.2.9 KeyCodeConstants")]
[TestCategory("MS-VBAL 6.1.2.12 SystemColorConstants")]
public sealed class ColorKeyAndSystemColorConstantsTests
{
    private static async Task<string> Run(params string[] lines)
    {
        var output = await ModuleWorkspace.RunAsync([], $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n");
        return string.Concat(output.Select(line => line.Trim() + "|"));
    }

    // what MS-VBA printed for each name: the name, its TypeName, and its value; the probe that printed it is made from the names.
    private static async Task AssertAsMSVBA(string printed)
    {
        var names = printed.Split('|', StringSplitOptions.RemoveEmptyEntries).Select(each => each.Split('=')[0]);
        Assert.AreEqual(printed, await Run([.. names.Select(name => $"Debug.Print \"{name}=\" & TypeName({name}) & \"=\" & {name}")]));
    }

    [TestMethod]
    public Task ColorConstants_AreLongs()
        => AssertAsMSVBA("vbBlack=Long=0|vbBlue=Long=16711680|vbCyan=Long=16776960|vbGreen=Long=65280|vbMagenta=Long=16711935|vbMajenta=Empty=|vbRed=Long=255|vbWhite=Long=16777215|vbYellow=Long=65535|");

    [TestMethod]
    public Task KeyCodeConstants_AreIntegers()
        => AssertAsMSVBA("vbKeyLButton=Integer=1|vbKeyRButton=Integer=2|vbKeyCancel=Integer=3|vbKeyMButton=Integer=4|vbKeyBack=Integer=8|vbKeyTab=Integer=9|vbKeyClear=Integer=12|vbKeyReturn=Integer=13|vbKeyShift=Integer=16|vbKeyControl=Integer=17|vbKeyMenu=Integer=18|vbKeyPause=Integer=19|vbKeyCapital=Integer=20|vbKeyEscape=Integer=27|vbKeySpace=Integer=32|vbKeyPageUp=Integer=33|vbKeyPageDown=Integer=34|vbKeyEnd=Integer=35|vbKeyHome=Integer=36|vbKeyLeft=Integer=37|vbKeyUp=Integer=38|vbKeyRight=Integer=39|vbKeyDown=Integer=40|vbKeySelect=Integer=41|vbKeyPrint=Integer=42|vbKeyExecute=Integer=43|vbKeySnapshot=Integer=44|vbKeyInsert=Integer=45|vbKeyDelete=Integer=46|vbKeyHelp=Integer=47|vbKeyNumlock=Integer=144|vbKeyA=Integer=65|vbKeyB=Integer=66|vbKeyC=Integer=67|vbKeyD=Integer=68|vbKeyE=Integer=69|vbKeyF=Integer=70|vbKeyG=Integer=71|vbKeyH=Integer=72|vbKeyI=Integer=73|vbKeyJ=Integer=74|vbKeyK=Integer=75|vbKeyL=Integer=76|vbKeyM=Integer=77|vbKeyN=Integer=78|vbKeyO=Integer=79|vbKeyP=Integer=80|vbKeyQ=Integer=81|vbKeyR=Integer=82|vbKeyS=Integer=83|"
            + "vbKeyT=Integer=84|vbKeyU=Integer=85|vbKeyV=Integer=86|vbKeyW=Integer=87|vbKeyX=Integer=88|vbKeyY=Integer=89|vbKeyZ=Integer=90|vbKey0=Integer=48|vbKey1=Integer=49|vbKey2=Integer=50|vbKey3=Integer=51|vbKey4=Integer=52|vbKey5=Integer=53|vbKey6=Integer=54|vbKey7=Integer=55|vbKey8=Integer=56|vbKey9=Integer=57|vbKeyNumpad0=Integer=96|vbKeyNumpad1=Integer=97|vbKeyNumpad2=Integer=98|vbKeyNumpad3=Integer=99|vbKeyNumpad4=Integer=100|vbKeyNumpad5=Integer=101|vbKeyNumpad6=Integer=102|vbKeyNumpad7=Integer=103|vbKeyNumpad8=Integer=104|vbKeyNumpad9=Integer=105|vbKeyMultiply=Integer=106|vbKeyAdd=Integer=107|vbKeySeparator=Integer=108|vbKeySubtract=Integer=109|vbKeyDecimal=Integer=110|vbKeyDivide=Integer=111|vbKeyF1=Integer=112|vbKeyF2=Integer=113|vbKeyF3=Integer=114|vbKeyF4=Integer=115|vbKeyF5=Integer=116|vbKeyF6=Integer=117|vbKeyF7=Integer=118|vbKeyF8=Integer=119|vbKeyF9=Integer=120|vbKeyF10=Integer=121|vbKeyF11=Integer=122|vbKeyF12=Integer=123|vbKeyF13=Integer=124|vbKeyF14=Integer=125|vbKeyF15=Integer=126|vbKeyF16=Integer=127|");

    [TestMethod]
    public Task SystemColorConstants_AreLongsWithTheHighBitSet()
        => AssertAsMSVBA("vbScrollBars=Long=-2147483648|vbDesktop=Long=-2147483647|vbActiveTitleBar=Long=-2147483646|vbInactiveTitleBar=Long=-2147483645|vbMenuBar=Long=-2147483644|vbWindowBackground=Long=-2147483643|vbWindowFrame=Long=-2147483642|vbMenuText=Long=-2147483641|vbWindowText=Long=-2147483640|vbTitleBarText=Long=-2147483639|vbActiveBorder=Long=-2147483638|vbInactiveBorder=Long=-2147483637|vbApplicationWorkspace=Long=-2147483636|vbHighlight=Long=-2147483635|vbHighlightText=Long=-2147483634|vbButtonFace=Long=-2147483633|vbButtonShadow=Long=-2147483632|vbGrayText=Long=-2147483631|vbButtonText=Long=-2147483630|vbInactiveCaptionText=Long=-2147483629|vb3DHighlight=Long=-2147483628|vb3DDKShadow=Long=-2147483627|vb3DLight=Long=-2147483626|vb3DFace=Long=-2147483633|vb3DShadow=Long=-2147483632|vbInfoText=Long=-2147483625|vbInfoBackground=Long=-2147483624|vbButtonHighlight=Empty=|");

    // T04-T10: by the constant's name, qualified by the library, by the module, or by both.
    [TestMethod]
    public async Task AConstant_IsReachedByItsName_QualifiedOrNot()
        => Assert.AreEqual("255|65|-2147483633|255|255|65|65|-2147483633|-2147483633|", await Run(
            "Debug.Print VBA.vbRed", "Debug.Print VBA.vbKeyA", "Debug.Print VBA.vbButtonFace",
            "Debug.Print VBA.ColorConstants.vbRed", "Debug.Print ColorConstants.vbRed",
            "Debug.Print VBA.KeyCodeConstants.vbKeyA", "Debug.Print KeyCodeConstants.vbKeyA",
            "Debug.Print VBA.SystemColorConstants.vbButtonFace", "Debug.Print SystemColorConstants.vbButtonFace"));

    // T12: a constant of the library is one in a constant expression, and in a Case.
    [TestMethod]
    public async Task AConstant_IsAConstantExpression()
        => Assert.AreEqual("320|ret|", await Run(
            "Const x As Long = vbRed + vbKeyA", "Debug.Print x", "Select Case 13", "Case vbKeyReturn", "Debug.Print \"ret\"", "End Select"));

    // T11: "Assignment to constant not permitted".
    [TestMethod]
    public async Task AConstant_CannotBeAssigned()
        => Assert.IsNotEmpty(await ModuleWorkspace.LoadErrorsAsync([], "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nvbRed = 1\r\nEnd Sub\r\n"));

    // T01-T03, T13: a module of constants is no type (MS-VBA: "Automation type not supported in Visual Basic").
    [TestMethod]
    [DataRow("ColorConstants")]
    [DataRow("KeyCodeConstants")]
    [DataRow("SystemColorConstants")]
    [DataRow("VBA.ColorConstants")]
    public async Task AModuleOfConstants_IsNoType(string declared)
        => Assert.IsNotEmpty(await ModuleWorkspace.LoadErrorsAsync([], $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nDim c As {declared}\r\nEnd Sub\r\n"));
}
