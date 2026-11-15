using RDCore.External.Interaction;
using RDCore.SDK.Runtime.Abstract.StdLib;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace RDCore.External.Windows.Interaction;

/// <summary>
/// The dialogs of Windows: a <c>MsgBox</c> is the message box of <c>user32</c>, and an <c>InputBox</c> a dialog of the same make, laid out as MS-VBA lays out its own.
/// </summary>
/// <remarks>
/// <para>
/// The values of <c>VbMsgBoxStyle</c> are those of the <c>MB_*</c> flags and the values of <c>VbMsgBoxResult</c> those of the <c>ID*</c> answers, which is no accident:
/// a <c>MsgBox</c> has always been this call. The dialog has no owner window - the program runs in a process that has none - so it is brought to the foreground and kept
/// above the windows of other applications, where the person the program asks can see it. The help button is left out: the help system it opens is not supported.
/// </para>
/// <para>
/// Windows has no input box of its own. This one is built from a dialog template in memory: the prompt, the <em>OK</em> and <em>Cancel</em> buttons, and the text box,
/// which starts with the default text selected. <kbd>Esc</kbd>, the <em>Cancel</em> button and the close button cancel it.
/// </para>
/// <para>
/// A dialog whose run is stopped is closed as its close button would close it, from the thread that waited for it.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsUserInteraction : IUserInteraction
{
    private const uint MB_HELP = 0x4000;
    private const uint MB_SETFOREGROUND = 0x10000;
    private const uint MB_TOPMOST = 0x40000;

    /// <inheritdoc/>
    public VBMsgBoxResult ShowMessage(MessageBoxRequest request, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var style = ((uint)request.Style & ~MB_HELP) | MB_SETFOREGROUND | MB_TOPMOST;

        int answer;
        using (CloseOnCancel(token))
        {
            answer = MessageBoxW(IntPtr.Zero, request.Prompt, request.Title, style);
        }

        token.ThrowIfCancellationRequested();
        return answer == 0 ? throw new Win32Exception(Marshal.GetLastWin32Error()) : (VBMsgBoxResult)answer;
    }

    /// <inheritdoc/>
    public string? Prompt(InputBoxRequest request, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var dialog = new InputDialog(request);

        string? answer;
        using (CloseOnCancel(token))
        {
            answer = dialog.Show();
        }

        token.ThrowIfCancellationRequested();
        return answer;
    }

    // the dialog is modal to the thread that opened it, and is closed - from whichever thread cancels the run - by asking every window of that thread to close.
    private static CancellationTokenRegistration CloseOnCancel(CancellationToken token)
    {
        var thread = GetCurrentThreadId();
        return token.Register(() => EnumThreadWindows(thread, (window, parameter) =>
        {
            _ = PostMessageW(window, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            return true;
        }, IntPtr.Zero));
    }

    private const uint WM_CLOSE = 0x0010;

    /// <summary>
    /// An input box: a dialog template built in memory, run by <c>DialogBoxIndirectParam</c>.
    /// </summary>
    private sealed class InputDialog(InputBoxRequest request)
    {
        private const int IDOK = 1;
        private const int IDCANCEL = 2;
        private const int EditId = 100;
        private const int PromptId = 101;

        private const uint WM_INITDIALOG = 0x0110;
        private const uint WM_TAKEFOCUS = 0x8000 + 1; // WM_APP + 1
        private const uint WM_COMMAND = 0x0111;
        private const uint EM_SETSEL = 0x00B1;

        private const uint WS_POPUP = 0x80000000;
        private const uint WS_CHILD = 0x40000000;
        private const uint WS_VISIBLE = 0x10000000;
        private const uint WS_CAPTION = 0x00C00000;
        private const uint WS_SYSMENU = 0x00080000;
        private const uint WS_BORDER = 0x00800000;
        private const uint WS_TABSTOP = 0x00010000;
        private const uint WS_EX_TOPMOST = 0x00000008;
        private const uint DS_SETFONT = 0x40;
        private const uint DS_MODALFRAME = 0x80;
        private const uint DS_SETFOREGROUND = 0x200;
        private const uint DS_CENTER = 0x800;
        private const uint BS_DEFPUSHBUTTON = 0x1;
        private const uint ES_AUTOHSCROLL = 0x80;
        private const uint SS_NOPREFIX = 0x80;

        private const ushort ButtonClass = 0x0080;
        private const ushort EditClass = 0x0081;
        private const ushort StaticClass = 0x0082;

        private const uint SWP_NOSIZE = 0x1;
        private const uint SWP_NOZORDER = 0x4;
        private const int TwipsPerInch = 1440;

        private string? _answer;

        public string? Show()
        {
            var template = BuildTemplate();
            var memory = Marshal.AllocHGlobal(template.Length);
            DialogProc procedure = Procedure;
            try
            {
                Marshal.Copy(template, 0, memory, template.Length);
                var result = DialogBoxIndirectParamW(GetModuleHandleW(null), memory, IntPtr.Zero, procedure, IntPtr.Zero);
                if (result == -1)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                return result == IDOK ? _answer ?? string.Empty : null;
            }
            finally
            {
                Marshal.FreeHGlobal(memory);
                GC.KeepAlive(procedure);
            }
        }

        private IntPtr Procedure(IntPtr dialog, uint message, IntPtr wParam, IntPtr lParam)
        {
            switch (message)
            {
                case WM_INITDIALOG:
                    _ = SetDlgItemTextW(dialog, EditId, request.Default);
                    var edit = GetDlgItem(dialog, EditId);
                    _ = SendMessageW(edit, EM_SETSEL, IntPtr.Zero, new IntPtr(-1));
                    _ = SetFocus(edit);
                    if (request.XPos is not null || request.YPos is not null)
                    {
                        var dpi = SystemDpi();
                        _ = SetWindowPos(dialog, IntPtr.Zero,
                            (request.XPos ?? 0) * dpi / TwipsPerInch, (request.YPos ?? 0) * dpi / TwipsPerInch, 0, 0, SWP_NOSIZE | SWP_NOZORDER);
                    }

                    // the dialog is shown once this returns, and takes the keyboard when its loop runs.
                    _ = PostMessageW(dialog, WM_TAKEFOCUS, IntPtr.Zero, IntPtr.Zero);

                    // the focus was set here, which the dialog manager is told by answering FALSE.
                    return IntPtr.Zero;

                case WM_TAKEFOCUS:
                    TakeForeground(dialog, GetDlgItem(dialog, EditId));
                    return new IntPtr(1);

                case WM_COMMAND:
                    switch ((int)(wParam.ToInt64() & 0xFFFF))
                    {
                        case IDOK:
                            var control = GetDlgItem(dialog, EditId);
                            var text = new StringBuilder(GetWindowTextLengthW(control) + 1);
                            _ = GetWindowTextW(control, text, text.Capacity);
                            _answer = text.ToString();
                            _ = EndDialog(dialog, new IntPtr(IDOK));
                            return new IntPtr(1);

                        // Esc, the close button, and Cancel.
                        case IDCANCEL:
                            _ = EndDialog(dialog, new IntPtr(IDCANCEL));
                            return new IntPtr(1);
                    }

                    break;
            }

            return IntPtr.Zero;
        }

        // a topmost dialog is not given the keyboard while another process has the foreground, as a terminal does under rdc run: Windows only lets the foreground
        // be taken by the thread that has it. Attached to that thread's input for as long as it takes, the dialog takes the foreground, and its text box the focus.
        private static void TakeForeground(IntPtr dialog, IntPtr edit)
        {
            var ours = GetCurrentThreadId();
            var foreground = GetForegroundWindow();
            var theirs = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, out _);
            var attached = theirs != 0 && theirs != ours && AttachThreadInput(ours, theirs, true);
            try
            {
                _ = SetForegroundWindow(dialog);
                _ = SetFocus(edit);
            }
            finally
            {
                if (attached)
                {
                    _ = AttachThreadInput(ours, theirs, false);
                }
            }
        }

        private static int SystemDpi()
        {
            try
            {
                return (int)GetDpiForSystem();
            }
            catch (EntryPointNotFoundException)
            {
                return 96;
            }
        }

        // DLGTEMPLATE, then one DLGITEMTEMPLATE for each control, each on a DWORD boundary; sizes in dialog units.
        private byte[] BuildTemplate()
        {
            var centered = request.XPos is null && request.YPos is null;
            var writer = new TemplateWriter();
            writer.UInt32(WS_POPUP | WS_CAPTION | WS_SYSMENU | DS_SETFONT | DS_MODALFRAME | DS_SETFOREGROUND | (centered ? DS_CENTER : 0));
            writer.UInt32(WS_EX_TOPMOST);
            writer.UInt16(4);
            writer.Int16(0);
            writer.Int16(0);
            writer.Int16(260);
            writer.Int16(92);
            writer.UInt16(0); // no menu
            writer.UInt16(0); // the dialog class
            writer.String(request.Title);
            writer.UInt16(8);
            writer.String("MS Shell Dlg");

            writer.Item(WS_CHILD | WS_VISIBLE | SS_NOPREFIX, 7, 7, 190, 56, PromptId, StaticClass, request.Prompt);
            writer.Item(WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_DEFPUSHBUTTON, 203, 7, 50, 14, IDOK, ButtonClass, "OK");
            writer.Item(WS_CHILD | WS_VISIBLE | WS_TABSTOP, 203, 25, 50, 14, IDCANCEL, ButtonClass, "Cancel");
            writer.Item(WS_CHILD | WS_VISIBLE | WS_TABSTOP | WS_BORDER | ES_AUTOHSCROLL, 7, 70, 246, 14, EditId, EditClass, string.Empty);
            return writer.ToArray();
        }
    }

    private sealed class TemplateWriter
    {
        private readonly MemoryStream _stream = new();

        public void UInt32(uint value) => _stream.Write(BitConverter.GetBytes(value));

        public void UInt16(ushort value) => _stream.Write(BitConverter.GetBytes(value));

        public void Int16(short value) => _stream.Write(BitConverter.GetBytes(value));

        public void String(string value)
        {
            _stream.Write(Encoding.Unicode.GetBytes(value));
            UInt16(0);
        }

        public void Item(uint style, short x, short y, short cx, short cy, int id, ushort atom, string text)
        {
            while (_stream.Length % 4 != 0)
            {
                _stream.WriteByte(0);
            }

            UInt32(style);
            UInt32(0);
            Int16(x);
            Int16(y);
            Int16(cx);
            Int16(cy);
            UInt16((ushort)id);
            UInt16(0xFFFF);
            UInt16(atom);
            String(text);
            UInt16(0); // no creation data
        }

        public byte[] ToArray() => _stream.ToArray();
    }

    private delegate IntPtr DialogProc(IntPtr dialog, uint message, IntPtr wParam, IntPtr lParam);

    private delegate bool EnumThreadWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);

    [DllImport("user32", ExactSpelling = true, SetLastError = true)]
    private static extern nint DialogBoxIndirectParamW(IntPtr instance, IntPtr template, IntPtr owner, DialogProc procedure, IntPtr parameter);

    [DllImport("user32", ExactSpelling = true)]
    private static extern bool EndDialog(IntPtr dialog, IntPtr result);

    [DllImport("user32", ExactSpelling = true)]
    private static extern IntPtr GetDlgItem(IntPtr dialog, int id);

    [DllImport("user32", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern bool SetDlgItemTextW(IntPtr dialog, int id, string text);

    [DllImport("user32", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetWindowTextW(IntPtr window, StringBuilder text, int capacity);

    [DllImport("user32", ExactSpelling = true)]
    private static extern int GetWindowTextLengthW(IntPtr window);

    [DllImport("user32", ExactSpelling = true)]
    private static extern IntPtr SendMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32", ExactSpelling = true)]
    private static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32", ExactSpelling = true)]
    private static extern IntPtr SetFocus(IntPtr window);

    [DllImport("user32", ExactSpelling = true)]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32", ExactSpelling = true)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32", ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32", ExactSpelling = true)]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, bool doAttach);

    [DllImport("user32", ExactSpelling = true)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32", ExactSpelling = true)]
    private static extern bool EnumThreadWindows(uint thread, EnumThreadWindowsProc callback, IntPtr parameter);

    [DllImport("user32", ExactSpelling = true)]
    private static extern uint GetDpiForSystem();

    [DllImport("kernel32", ExactSpelling = true)]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr GetModuleHandleW(string? moduleName);
}
