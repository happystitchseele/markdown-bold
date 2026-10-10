using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

sealed class BoldButton : Form
{
    delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    readonly HookProc callback;
    readonly IntPtr hook;
    readonly ToolTip tip = new ToolTip();
    readonly ContextMenuStrip menu = new ContextMenuStrip();
    readonly System.Windows.Forms.Timer restore = new System.Windows.Forms.Timer { Interval = 150 };
    TaskCompletionSource<bool> copyReady;
    IDataObject previousClipboard;
    uint copySequence, clipboardOwned;
    bool enabled = true, busy, swallowed, dragged;
    Point dragOrigin;

    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool AddClipboardFormatListener(IntPtr window);
    [DllImport("user32.dll")] static extern bool RemoveClipboardFormatListener(IntPtr window);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public UNION data; }
    [StructLayout(LayoutKind.Explicit)] struct UNION { [FieldOffset(0)] public KEY key; [FieldOffset(0)] public MOUSE mouse; }
    [StructLayout(LayoutKind.Sequential)] struct KEY { public ushort vk, scan; public uint flags, time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSE { public int x, y; public uint data, flags, time; public UIntPtr extra; }

    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams
    {
        get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x80; return p; }
    }

    public BoldButton()
    {
        Text = "Markdown 加粗";
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;
        using (var g = CreateGraphics())
        {
            int size = (int)Math.Round(30 * g.DpiX / 96);
            ClientSize = new Size(size, size);
        }
        using (var circle = new GraphicsPath())
        {
            circle.AddEllipse(0, 0, Width, Height);
            Region = new Region(circle);
        }
        StartPosition = FormStartPosition.Manual;
        var area = Screen.PrimaryScreen.WorkingArea;
        Location = new Point(area.Right - Width - 24, area.Top + 180);
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
        menu.ShowImageMargin = false;
        menu.Items.Add("退出", null, (s, e) => Close());
        RefreshState();
        MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { dragOrigin = Cursor.Position; dragged = false; } };
        MouseMove += (s, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            var p = Cursor.Position;
            var delta = new Size(p.X - dragOrigin.X, p.Y - dragOrigin.Y);
            if (dragged || Math.Abs(delta.Width) + Math.Abs(delta.Height) > 4)
            {
                dragged = true; Location += delta; dragOrigin = p;
            }
        };
        MouseUp += (s, e) =>
        {
            if (e.Button == MouseButtons.Right) menu.Show(this, e.Location);
            else if (e.Button == MouseButtons.Left && !dragged) { enabled = !enabled; RefreshState(); }
        };
        callback = OnKey;
        hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) throw new InvalidOperationException("无法注册键盘快捷键。");
        if (!AddClipboardFormatListener(Handle)) throw new InvalidOperationException("无法监听剪贴板更新。");
        restore.Tick += (s, e) => RestoreClipboard();
        FormClosed += (s, e) =>
        {
            UnhookWindowsHookEx(hook); RemoveClipboardFormatListener(Handle);
            RestoreClipboard(); restore.Dispose(); menu.Dispose(); tip.Dispose();
        };
    }

    void RefreshState()
    {
        BackColor = enabled ? Color.FromArgb(222, 55, 65) : Color.FromArgb(130, 130, 130);
        tip.SetToolTip(this, (enabled ? "已启用：Ctrl+B 切换 ** 强调" : "已暂停：单击启用") + "\n单击启用/暂停 · 拖动移动 · 右键菜单退出");
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        PixelB.Draw(e.Graphics, ClientRectangle, Color.White);
    }

    static bool Down(int key) { return (GetAsyncKeyState(key) & 0x8000) != 0; }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg == 0x31D && copyReady != null && GetClipboardSequenceNumber() != copySequence)
            copyReady.TrySetResult(true);
    }

    IntPtr OnKey(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && Marshal.ReadInt32(data) == 0x42 && (Marshal.ReadInt32(data, 8) & 0x10) == 0)
        {
            int msg = message.ToInt32();
            if (msg == 0x101 && swallowed) { swallowed = false; return (IntPtr)1; }
            if (msg == 0x100 && (swallowed || (enabled && Down(0x11) && !Down(0x12) && !Down(0x10))))
            {
                if (!swallowed && !busy)
                {
                    busy = true;
                    var target = GetForegroundWindow();
                    BeginInvoke(new Action(async () => await ToggleSelection(target)));
                }
                swallowed = true;
                return (IntPtr)1;
            }
        }
        return CallNextHookEx(hook, code, message, data);
    }

    internal static void Chord(ushort key)
    {
        // Do not release Ctrl when the user is still physically holding it.
        bool held = Down(0x11);
        ushort[] keys = held ? new ushort[] { key, key } : new ushort[] { 0x11, key, key, 0x11 };
        var inputs = new INPUT[keys.Length];
        for (int i = 0; i < inputs.Length; i++)
        {
            inputs[i].type = 1;
            inputs[i].data.key.vk = keys[i];
            inputs[i].data.key.flags = i >= inputs.Length / 2 ? 2u : 0u;
        }
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT))) != inputs.Length)
            throw new InvalidOperationException("无法向编辑器发送按键。");
    }

    internal static string ToggleText(string text)
    {
        if (text.Length == 0) return text;
        return text.Length >= 4 && text.StartsWith("**", StringComparison.Ordinal) && text.EndsWith("**", StringComparison.Ordinal)
            ? text.Substring(2, text.Length - 4) : "**" + text + "**";
    }

    internal static IDataObject SnapshotClipboard()
    {
        var original = Clipboard.GetDataObject();
        if (original == null) return null;
        var snapshot = new DataObject();
        foreach (string format in original.GetFormats(false))
            snapshot.SetData(format, original.GetData(format, false));
        return snapshot;
    }

    async Task ToggleSelection(IntPtr target)
    {
        try
        {
            if (!enabled || IsDisposed || GetForegroundWindow() != target) return;
            restore.Stop();
            if (clipboardOwned == 0 || GetClipboardSequenceNumber() != clipboardOwned)
            {
                previousClipboard = SnapshotClipboard();
                clipboardOwned = 0;
            }
            copySequence = GetClipboardSequenceNumber();
            copyReady = new TaskCompletionSource<bool>();
            Chord(0x43);
            if (GetClipboardSequenceNumber() == copySequence)
                await Task.WhenAny(copyReady.Task, Task.Delay(800));
            if (GetClipboardSequenceNumber() == copySequence) return;
            clipboardOwned = GetClipboardSequenceNumber();
            if (!enabled || IsDisposed || GetForegroundWindow() != target || !Clipboard.ContainsText()) return;
            string selected = Clipboard.GetText(TextDataFormat.UnicodeText);
            if (selected.Length == 0) return;
            Clipboard.SetText(ToggleText(selected), TextDataFormat.UnicodeText);
            clipboardOwned = GetClipboardSequenceNumber();
            if (!enabled || GetForegroundWindow() != target) return;
            Chord(0x56);
        }
        catch { if (!IsDisposed) tip.Show("本次未完成，请重试。", this, 0, Height, 2000); }
        finally
        {
            copyReady = null;
            busy = false;
            if (!IsDisposed && clipboardOwned != 0) restore.Start();
        }
    }

    void RestoreClipboard()
    {
        restore.Stop();
        try
        {
            if (clipboardOwned != 0 && GetClipboardSequenceNumber() == clipboardOwned)
            {
                if (previousClipboard != null) Clipboard.SetDataObject(previousClipboard, true);
                else Clipboard.Clear();
            }
        }
        catch { }
        clipboardOwned = 0;
        previousClipboard = null;
    }

    [STAThread] static void Main()
    {
        bool created;
        using (var mutex = new Mutex(true, "Local\\MarkdownBoldFloatingButton", out created))
        {
            if (!created) return;
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new BoldButton());
        }
    }
}

static class PixelB
{
    static readonly string[] Pixels = { "11110", "11011", "11011", "11110", "11011", "11011", "11110" };
    internal static void Draw(Graphics g, Rectangle bounds, Color color)
    {
        int unit = Math.Max(1, bounds.Height / 12);
        int x = bounds.X + (bounds.Width - unit * 5) / 2 + Math.Max(1, bounds.Width / 32);
        int y = bounds.Y + (bounds.Height - unit * 7) / 2;
        using (var brush = new SolidBrush(color))
            for (int row = 0; row < 7; row++)
                for (int col = 0; col < 5; col++)
                    if (Pixels[row][col] == '1') g.FillRectangle(brush, x + col * unit, y + row * unit, unit, unit);
    }
}
