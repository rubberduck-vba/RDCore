using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// <strong>MS-VBAL 6.1.2.9 KeyCodeConstants</strong>
/// </summary>
/// <remarks>
/// The codes of the keys of a keyboard and the buttons of a mouse, each an <c>Integer</c>, as MS-VBA declares them (the colour constants are <c>Long</c>).
/// <para>
/// 👉 Each field is one of the module's <c>Const</c> members, declared as the value it is: <see cref="RDCore.SDK.Runtime.StdLib.StdLibSymbolReader"/> reads
/// it as a <c>Public Const</c> of that value's type. A constant is reached by its own name (<c>vbKeyReturn</c>), or qualified by the module or the library
/// (<c>KeyCodeConstants.vbKeyReturn</c>, <c>VBA.vbKeyReturn</c>). Nothing implements this interface.
/// </para>
/// </remarks>
[StdLibModule]
public interface IStdKeyCodeConstantsModule
{
    /// <summary><c>vbKeyLButton</c>: the left mouse button.</summary>
    public static readonly VBIntegerValue VBKeyLButton = new(1);

    /// <summary><c>vbKeyRButton</c>: the right mouse button.</summary>
    public static readonly VBIntegerValue VBKeyRButton = new(2);

    /// <summary><c>vbKeyCancel</c>: the <kbd>CANCEL</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyCancel = new(3);

    /// <summary><c>vbKeyMButton</c>: the middle mouse button.</summary>
    public static readonly VBIntegerValue VBKeyMButton = new(4);

    /// <summary><c>vbKeyBack</c>: the <kbd>BACKSPACE</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyBack = new(8);

    /// <summary><c>vbKeyTab</c>: the <kbd>TAB</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyTab = new(9);

    /// <summary><c>vbKeyClear</c>: the <kbd>CLEAR</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyClear = new(12);

    /// <summary><c>vbKeyReturn</c>: the <kbd>ENTER</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyReturn = new(13);

    /// <summary><c>vbKeyShift</c>: the <kbd>SHIFT</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyShift = new(16);

    /// <summary><c>vbKeyControl</c>: the <kbd>CTRL</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyControl = new(17);

    /// <summary><c>vbKeyMenu</c>: the <kbd>MENU</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyMenu = new(18);

    /// <summary><c>vbKeyPause</c>: the <kbd>PAUSE</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyPause = new(19);

    /// <summary><c>vbKeyCapital</c>: the <kbd>CAPS LOCK</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyCapital = new(20);

    /// <summary><c>vbKeyEscape</c>: the <kbd>ESC</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyEscape = new(27);

    /// <summary><c>vbKeySpace</c>: the <kbd>SPACEBAR</kbd>.</summary>
    public static readonly VBIntegerValue VBKeySpace = new(32);

    /// <summary><c>vbKeyPageUp</c>: the <kbd>PAGE UP</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyPageUp = new(33);

    /// <summary><c>vbKeyPageDown</c>: the <kbd>PAGE DOWN</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyPageDown = new(34);

    /// <summary><c>vbKeyEnd</c>: the <kbd>END</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyEnd = new(35);

    /// <summary><c>vbKeyHome</c>: the <kbd>HOME</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyHome = new(36);

    /// <summary><c>vbKeyLeft</c>: the <kbd>LEFT ARROW</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyLeft = new(37);

    /// <summary><c>vbKeyUp</c>: the <kbd>UP ARROW</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyUp = new(38);

    /// <summary><c>vbKeyRight</c>: the <kbd>RIGHT ARROW</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyRight = new(39);

    /// <summary><c>vbKeyDown</c>: the <kbd>DOWN ARROW</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyDown = new(40);

    /// <summary><c>vbKeySelect</c>: the <kbd>SELECT</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeySelect = new(41);

    /// <summary><c>vbKeyPrint</c>: the <kbd>PRINT SCREEN</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyPrint = new(42);

    /// <summary><c>vbKeyExecute</c>: the <kbd>EXECUTE</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyExecute = new(43);

    /// <summary><c>vbKeySnapshot</c>: the <kbd>SNAPSHOT</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeySnapshot = new(44);

    /// <summary><c>vbKeyInsert</c>: the <kbd>INSERT</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyInsert = new(45);

    /// <summary><c>vbKeyDelete</c>: the <kbd>DELETE</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyDelete = new(46);

    /// <summary><c>vbKeyHelp</c>: the <kbd>HELP</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyHelp = new(47);

    /// <summary><c>vbKeyNumlock</c>: the <kbd>NUM LOCK</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyNumlock = new(144);

    /// <summary><c>vbKeyA</c>: the <kbd>A</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyA = new(65);

    /// <summary><c>vbKeyB</c>: the <kbd>B</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyB = new(66);

    /// <summary><c>vbKeyC</c>: the <kbd>C</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyC = new(67);

    /// <summary><c>vbKeyD</c>: the <kbd>D</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyD = new(68);

    /// <summary><c>vbKeyE</c>: the <kbd>E</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyE = new(69);

    /// <summary><c>vbKeyF</c>: the <kbd>F</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyF = new(70);

    /// <summary><c>vbKeyG</c>: the <kbd>G</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyG = new(71);

    /// <summary><c>vbKeyH</c>: the <kbd>H</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyH = new(72);

    /// <summary><c>vbKeyI</c>: the <kbd>I</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyI = new(73);

    /// <summary><c>vbKeyJ</c>: the <kbd>J</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyJ = new(74);

    /// <summary><c>vbKeyK</c>: the <kbd>K</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyK = new(75);

    /// <summary><c>vbKeyL</c>: the <kbd>L</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyL = new(76);

    /// <summary><c>vbKeyM</c>: the <kbd>M</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyM = new(77);

    /// <summary><c>vbKeyN</c>: the <kbd>N</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyN = new(78);

    /// <summary><c>vbKeyO</c>: the <kbd>O</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyO = new(79);

    /// <summary><c>vbKeyP</c>: the <kbd>P</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyP = new(80);

    /// <summary><c>vbKeyQ</c>: the <kbd>Q</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyQ = new(81);

    /// <summary><c>vbKeyR</c>: the <kbd>R</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyR = new(82);

    /// <summary><c>vbKeyS</c>: the <kbd>S</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyS = new(83);

    /// <summary><c>vbKeyT</c>: the <kbd>T</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyT = new(84);

    /// <summary><c>vbKeyU</c>: the <kbd>U</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyU = new(85);

    /// <summary><c>vbKeyV</c>: the <kbd>V</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyV = new(86);

    /// <summary><c>vbKeyW</c>: the <kbd>W</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyW = new(87);

    /// <summary><c>vbKeyX</c>: the <kbd>X</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyX = new(88);

    /// <summary><c>vbKeyY</c>: the <kbd>Y</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyY = new(89);

    /// <summary><c>vbKeyZ</c>: the <kbd>Z</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyZ = new(90);

    /// <summary><c>vbKey0</c>: the <kbd>0</kbd> key.</summary>
    public static readonly VBIntegerValue VBKey0 = new(48);

    /// <summary><c>vbKey1</c>: the <kbd>1</kbd> key.</summary>
    public static readonly VBIntegerValue VBKey1 = new(49);

    /// <summary><c>vbKey2</c>: the <kbd>2</kbd> key.</summary>
    public static readonly VBIntegerValue VBKey2 = new(50);

    /// <summary><c>vbKey3</c>: the <kbd>3</kbd> key.</summary>
    public static readonly VBIntegerValue VBKey3 = new(51);

    /// <summary><c>vbKey4</c>: the <kbd>4</kbd> key.</summary>
    public static readonly VBIntegerValue VBKey4 = new(52);

    /// <summary><c>vbKey5</c>: the <kbd>5</kbd> key.</summary>
    public static readonly VBIntegerValue VBKey5 = new(53);

    /// <summary><c>vbKey6</c>: the <kbd>6</kbd> key.</summary>
    public static readonly VBIntegerValue VBKey6 = new(54);

    /// <summary><c>vbKey7</c>: the <kbd>7</kbd> key.</summary>
    public static readonly VBIntegerValue VBKey7 = new(55);

    /// <summary><c>vbKey8</c>: the <kbd>8</kbd> key.</summary>
    public static readonly VBIntegerValue VBKey8 = new(56);

    /// <summary><c>vbKey9</c>: the <kbd>9</kbd> key.</summary>
    public static readonly VBIntegerValue VBKey9 = new(57);

    /// <summary><c>vbKeyNumpad0</c>: the <kbd>0</kbd> key of the numeric keypad.</summary>
    public static readonly VBIntegerValue VBKeyNumpad0 = new(96);

    /// <summary><c>vbKeyNumpad1</c>: the <kbd>1</kbd> key of the numeric keypad.</summary>
    public static readonly VBIntegerValue VBKeyNumpad1 = new(97);

    /// <summary><c>vbKeyNumpad2</c>: the <kbd>2</kbd> key of the numeric keypad.</summary>
    public static readonly VBIntegerValue VBKeyNumpad2 = new(98);

    /// <summary><c>vbKeyNumpad3</c>: the <kbd>3</kbd> key of the numeric keypad.</summary>
    public static readonly VBIntegerValue VBKeyNumpad3 = new(99);

    /// <summary><c>vbKeyNumpad4</c>: the <kbd>4</kbd> key of the numeric keypad.</summary>
    public static readonly VBIntegerValue VBKeyNumpad4 = new(100);

    /// <summary><c>vbKeyNumpad5</c>: the <kbd>5</kbd> key of the numeric keypad.</summary>
    public static readonly VBIntegerValue VBKeyNumpad5 = new(101);

    /// <summary><c>vbKeyNumpad6</c>: the <kbd>6</kbd> key of the numeric keypad.</summary>
    public static readonly VBIntegerValue VBKeyNumpad6 = new(102);

    /// <summary><c>vbKeyNumpad7</c>: the <kbd>7</kbd> key of the numeric keypad.</summary>
    public static readonly VBIntegerValue VBKeyNumpad7 = new(103);

    /// <summary><c>vbKeyNumpad8</c>: the <kbd>8</kbd> key of the numeric keypad.</summary>
    public static readonly VBIntegerValue VBKeyNumpad8 = new(104);

    /// <summary><c>vbKeyNumpad9</c>: the <kbd>9</kbd> key of the numeric keypad.</summary>
    public static readonly VBIntegerValue VBKeyNumpad9 = new(105);

    /// <summary><c>vbKeyMultiply</c>: the multiplication sign (<kbd>*</kbd>) key of the numeric keypad.</summary>
    public static readonly VBIntegerValue VBKeyMultiply = new(106);

    /// <summary><c>vbKeyAdd</c>: the plus sign (<kbd>+</kbd>) key of the numeric keypad.</summary>
    public static readonly VBIntegerValue VBKeyAdd = new(107);

    /// <summary><c>vbKeySeparator</c>: the <kbd>ENTER</kbd> key of the numeric keypad.</summary>
    public static readonly VBIntegerValue VBKeySeparator = new(108);

    /// <summary><c>vbKeySubtract</c>: the minus sign (<kbd>-</kbd>) key of the numeric keypad.</summary>
    public static readonly VBIntegerValue VBKeySubtract = new(109);

    /// <summary><c>vbKeyDecimal</c>: the decimal point (<kbd>.</kbd>) key of the numeric keypad.</summary>
    public static readonly VBIntegerValue VBKeyDecimal = new(110);

    /// <summary><c>vbKeyDivide</c>: the division sign (<kbd>/</kbd>) key of the numeric keypad.</summary>
    public static readonly VBIntegerValue VBKeyDivide = new(111);

    /// <summary><c>vbKeyF1</c>: the <kbd>F1</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyF1 = new(112);

    /// <summary><c>vbKeyF2</c>: the <kbd>F2</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyF2 = new(113);

    /// <summary><c>vbKeyF3</c>: the <kbd>F3</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyF3 = new(114);

    /// <summary><c>vbKeyF4</c>: the <kbd>F4</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyF4 = new(115);

    /// <summary><c>vbKeyF5</c>: the <kbd>F5</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyF5 = new(116);

    /// <summary><c>vbKeyF6</c>: the <kbd>F6</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyF6 = new(117);

    /// <summary><c>vbKeyF7</c>: the <kbd>F7</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyF7 = new(118);

    /// <summary><c>vbKeyF8</c>: the <kbd>F8</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyF8 = new(119);

    /// <summary><c>vbKeyF9</c>: the <kbd>F9</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyF9 = new(120);

    /// <summary><c>vbKeyF10</c>: the <kbd>F10</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyF10 = new(121);

    /// <summary><c>vbKeyF11</c>: the <kbd>F11</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyF11 = new(122);

    /// <summary><c>vbKeyF12</c>: the <kbd>F12</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyF12 = new(123);

    /// <summary><c>vbKeyF13</c>: the <kbd>F13</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyF13 = new(124);

    /// <summary><c>vbKeyF14</c>: the <kbd>F14</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyF14 = new(125);

    /// <summary><c>vbKeyF15</c>: the <kbd>F15</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyF15 = new(126);

    /// <summary><c>vbKeyF16</c>: the <kbd>F16</kbd> key.</summary>
    public static readonly VBIntegerValue VBKeyF16 = new(127);
}
