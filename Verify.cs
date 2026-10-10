using System;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

static class Verify
{
    internal static TextBox Editor;
    internal static bool CtrlHeld, DeferredCopy;
    static uint inputCount;
    static bool sentCtrl;
    static Stopwatch clock;
    static long pasteMilliseconds;
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    internal static uint RecordInputs(uint count, Array inputs)
    {
        inputCount = count; sentCtrl = false; ushort key = 0;
        foreach (object input in inputs)
        {
            object data = input.GetType().GetField("data").GetValue(input);
            object keyboard = data.GetType().GetField("key").GetValue(data);
            ushort vk = (ushort)keyboard.GetType().GetField("vk").GetValue(keyboard);
            if (vk == 0x11) sentCtrl = true;
            else key = vk;
        }
        if (key == 0x43 && DeferredCopy)
            Editor.BeginInvoke(new Action(() => EditorChord(0x43)));
        else EditorChord(key);
        return count;
    }
    static void EditorChord(ushort key)
    {
        SendMessage(Editor.Handle, key == 0x43 ? 0x301u : 0x302u, IntPtr.Zero, IntPtr.Zero);
        if (key == 0x56) pasteMilliseconds = clock.ElapsedMilliseconds;
    }
    static void Check(bool passed, string label) { if (!passed) throw new Exception(label); Console.WriteLine("PASS " + label); }
    static object Get(object obj, string name) { return obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(obj); }
    static void Click(BoldButton app, MouseButtons button)
    {
        typeof(Control).GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(app, new object[] { new MouseEventArgs(button, 1, 5, 5, 0) });
    }
    static async Task Replace(BoldButton app, string input, string expected, int start, int length)
    {
        Editor.Text = input; Editor.Select(start, length);
        var action = typeof(BoldButton).GetMethod("ToggleSelection", BindingFlags.Instance | BindingFlags.NonPublic);
        pasteMilliseconds = -1;
        clock = Stopwatch.StartNew();
        await (Task)action.Invoke(app, new object[] { GetForegroundWindow() });
        Check(Editor.Text == expected, "native editor: " + input.Replace("\r\n", " / "));
        if (pasteMilliseconds >= 0) Console.WriteLine("TIMING paste=" + pasteMilliseconds + "ms ready=" + clock.ElapsedMilliseconds + "ms");
    }
    [STAThread] static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using (var host = new Form())
        using (var app = new BoldButton())
        {
            Editor = new TextBox { Multiline = true }; host.Controls.Add(Editor);
            var handle = Editor.Handle;
            IDataObject saved = BoldButton.SnapshotClipboard();
            var timer = new Timer { Interval = 20 };
            timer.Tick += async (s, e) =>
            {
                timer.Stop();
                try
                {
                    Check(BoldButton.ToggleText("中文🙂") == "**中文🙂**", "add markers");
                    Check(BoldButton.ToggleText("**中文🙂**") == "中文🙂", "remove markers");
                    Check(BoldButton.ToggleText("***文字***") == "*文字*", "preserve inner italic markers");
                    CtrlHeld = true;
                    BoldButton.Chord(0x43);
                    Check(inputCount == 2 && !sentCtrl, "held Ctrl stays held; only C key is sent");
                    CtrlHeld = false;
                    BoldButton.Chord(0x43);
                    Check(inputCount == 4 && sentCtrl, "released Ctrl uses a complete shortcut");
                    Clipboard.SetText("verification sentinel");
                    CtrlHeld = true;
                    DeferredCopy = true;
                    await Replace(app, "前文 中文🙂 后文", "前文 **中文🙂** 后文", 3, 4);
                    Check(inputCount == 2 && !sentCtrl, "paste completes while Ctrl is held");
                    CtrlHeld = false; DeferredCopy = false;
                    // The foreground path is ready immediately; restoration runs separately.
                    await Task.Delay(250);
                    Check(Clipboard.GetText() == "verification sentinel", "clipboard restored");
                    await Replace(app, "前文 **中文🙂** 后文", "前文 中文🙂 后文", 3, 8);
                    await Replace(app, "第一行\r\n第二行", "**第一行\r\n第二行**", 0, 8);
                    await Replace(app, "连续操作", "**连续操作**", 0, 4);
                    await Replace(app, "**连续操作**", "连续操作", 0, 8);
                    await Task.Delay(250);
                    Check(Clipboard.GetText() == "verification sentinel", "rapid operations restore the original clipboard");
                    await Replace(app, "无选区", "无选区", 3, 0);
                    Clipboard.SetText("new user clipboard");
                    await Task.Delay(250);
                    Check(Clipboard.GetText() == "new user clipboard", "user clipboard change is preserved");
                    Click(app, MouseButtons.Left);
                    Check(!(bool)Get(app, "enabled") && app.BackColor == Color.FromArgb(130,130,130), "single click pauses, gray fill");
                    await Replace(app, "暂停测试", "暂停测试", 0, 4);
                    Click(app, MouseButtons.Left);
                    Check((bool)Get(app, "enabled") && app.BackColor == Color.FromArgb(222,55,65), "single click enables, red fill");
                    Check(app.Region != null && !app.Region.IsVisible(0,0), "circular hit area");
                    Click(app, MouseButtons.Right);
                    Check(!app.IsDisposed && ((ContextMenuStrip)Get(app,"menu")).Items.Count == 1, "right click opens exit menu, keeps running");
                    ((ContextMenuStrip)Get(app,"menu")).Items[0].PerformClick();
                    Check(app.IsDisposed, "exit menu closes app");
                    using (var icon = Icon.ExtractAssociatedIcon(Path.Combine(args[0],"MarkdownBold.exe")))
                        Check(icon != null, "executable contains icon");
                    Console.WriteLine("All checks passed. Keyboard delivery was replaced by native WM_COPY/WM_PASTE; no foreground editor was modified.");
                }
                catch (Exception ex) { Console.WriteLine("FAIL " + ex); Environment.ExitCode = 1; }
                finally { if (saved != null) Clipboard.SetDataObject(saved, true); else Clipboard.Clear(); timer.Dispose(); Application.ExitThread(); }
            };
            timer.Start(); Application.Run();
        }
    }
}
