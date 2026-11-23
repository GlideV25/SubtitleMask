using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MaskForm());
    }
}

// 置顶黑色条：遮挡中英字幕的中文部分。
// 拖动条内部移动，拖动边缘缩放，点击正中 X 关闭，右键菜单可调不透明度/高度。
internal sealed class MaskForm : Form
{
    private const int MinWidth = 120;
    private const int MinHeight = 18;
    private const int EdgeSize = 8;
    private const int CornerRadius = 6;
    private const int CloseRadius = 12;

    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int MK_CONTROL = 0x0008;
    private const int VK_CONTROL = 0x11;

    private static MaskForm hookForm;
    private static IntPtr mouseHook;
    private static LowLevelMouseProc hookProc;

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public NativePoint pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private readonly Timer uiTimer;
    private readonly Font toastFont;
    private readonly ContextMenuStrip menu;

    private bool moving;
    private bool resizing;
    private int resizeDir;
    private Point dragStartCursor;
    private Rectangle dragStartBounds;

    private bool hovering;
    private float hoverAlpha;
    private bool alwaysShowClose;
    private double opacity = 1.0;
    private string settingsPath;
    private DateTime revealUntil = DateTime.MinValue;

    private string toastText;
    private DateTime toastUntil = DateTime.MinValue;

    public MaskForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.None;
        Text = "字幕遮挡条";
        MinimumSize = new Size(MinWidth, MinHeight);

        toastFont = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);

        uiTimer = new Timer();
        uiTimer.Interval = 30;
        uiTimer.Tick += UiTimerTick;

        settingsPath = ChooseSettingsPath();
        LoadSettings();
        menu = BuildMenu();
        Opacity = opacity;
        UpdateRegion();

        revealUntil = DateTime.Now.AddMilliseconds(2600);
        ShowToast("拖动移动 · 拖边缘缩放 · 中间 X 关闭 · Ctrl+滚轮调透明度", 3400);
    }

    // 启动时不抢当前窗口（播放器）的焦点
    protected override bool ShowWithoutActivation
    {
        get { return true; }
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
            cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
            return cp;
        }
    }

    // 用低级鼠标钩子监听滚轮，这样无需激活窗口（不抢播放器焦点）也能调透明度
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        hookForm = this;
        hookProc = MouseHookCallback;
        mouseHook = SetWindowsHookEx(WH_MOUSE_LL, hookProc, GetModuleHandle(null), 0);
    }

    private static bool IsCtrlDown()
    {
        return (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
    }

    private static IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_MOUSEWHEEL)
            {
                MaskForm form = hookForm;
                if (form != null && !form.IsDisposed && form.IsHandleCreated)
                {
                    MSLLHOOKSTRUCT data = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                    if (form.Bounds.Contains(data.pt.X, data.pt.Y) && IsCtrlDown())
                    {
                        int delta = (short)((data.mouseData >> 16) & 0xFFFF);
                        if (delta != 0)
                        {
                            form.AdjustOpacity(delta > 0 ? 0.05 : -0.05);
                            return (IntPtr)1; // 吞掉，避免播放器同时缩放
                        }
                    }
                }
            }
        }
        catch (Exception)
        {
        }
        return CallNextHookEx(mouseHook, nCode, wParam, lParam);
    }

    private void AdjustOpacity(double delta)
    {
        double value = Math.Round((opacity + delta) * 100.0) / 100.0;
        if (value < 0.25) value = 0.25;
        if (value > 1.0) value = 1.0;

        if (Math.Abs(value - opacity) > 0.001)
        {
            opacity = value;
            Opacity = opacity;
            SaveSettings();
        }
        ShowToast("不透明度 " + (int)Math.Round(opacity * 100) + "%", 900);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateRegion();
    }

    private void UpdateRegion()
    {
        if (Width <= 0 || Height <= 0) return;
        using (GraphicsPath path = RoundedRect(new RectangleF(0f, 0f, Width, Height), CornerRadius))
        {
            Region old = Region;
            Region = new Region(path);
            if (old != null) old.Dispose();
        }
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        GraphicsPath p = new GraphicsPath();
        float d = radius * 2f;
        if (d > r.Width) d = r.Width;
        if (d > r.Height) d = r.Height;
        if (d <= 0.5f)
        {
            p.AddRectangle(r);
            p.CloseFigure();
            return p;
        }
        p.AddArc(r.X, r.Y, d, d, 180f, 90f);
        p.AddArc(r.Right - d, r.Y, d, d, 270f, 90f);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0f, 90f);
        p.AddArc(r.X, r.Bottom - d, d, d, 90f, 90f);
        p.CloseFigure();
        return p;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        bool toastVisible = toastText != null && DateTime.Now < toastUntil;
        float a = alwaysShowClose ? 1f : hoverAlpha;

        if (a > 0.02f && !toastVisible)
        {
            using (GraphicsPath border = RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), CornerRadius))
            using (Pen pen = new Pen(Color.FromArgb((int)(90 * a), 175, 175, 175), 1f))
            {
                g.DrawPath(pen, border);
            }

            int cx = ClientSize.Width / 2;
            int cy = ClientSize.Height / 2;
            int r = 5;
            using (Pen pen = new Pen(Color.FromArgb((int)(235 * a), 245, 245, 245), 2f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, cx - r, cy - r, cx + r, cy + r);
                g.DrawLine(pen, cx - r, cy + r, cx + r, cy - r);
            }
        }

        if (toastVisible)
        {
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(230, 238, 238, 238)))
            using (StringFormat fmt = new StringFormat())
            {
                fmt.Alignment = StringAlignment.Center;
                fmt.LineAlignment = StringAlignment.Center;
                fmt.Trimming = StringTrimming.EllipsisCharacter;
                fmt.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(toastText, toastFont, brush, new RectangleF(6f, 0f, Width - 12f, Height), fmt);
            }
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;

        if (IsInCloseButton(e.Location))
        {
            Close();
            return;
        }

        dragStartCursor = Cursor.Position;
        dragStartBounds = Bounds;

        int d = HitEdge(e.Location);
        if (d != 0)
        {
            resizing = true;
            moving = false;
            resizeDir = d;
            UpdateCursor(e.Location);
        }
        else
        {
            moving = true;
            resizing = false;
        }
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (moving)
        {
            Point p = Cursor.Position;
            Location = new Point(dragStartBounds.X + (p.X - dragStartCursor.X),
                                 dragStartBounds.Y + (p.Y - dragStartCursor.Y));
            return;
        }

        if (resizing)
        {
            ApplyResize();
            return;
        }

        UpdateCursor(e.Location);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button == MouseButtons.Right)
        {
            if (!moving && !resizing) menu.Show(this, e.Location);
            return;
        }

        if (e.Button == MouseButtons.Left)
        {
            bool wasInteracting = moving || resizing;
            moving = false;
            resizing = false;
            Capture = false;
            if (wasInteracting)
            {
                SaveSettings();
                UpdateCursor(e.Location);
            }
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_MOUSEWHEEL)
        {
            long wp = m.WParam.ToInt64() & 0xFFFFFFFFL;
            if ((wp & MK_CONTROL) != 0)
            {
                int delta = (short)((wp >> 16) & 0xFFFF);
                if (delta != 0)
                {
                    AdjustOpacity(delta > 0 ? 0.05 : -0.05);
                    return;
                }
            }
        }
        base.WndProc(ref m);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        hovering = true;
        EnsureTimer();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hovering = false;
        if (!moving && !resizing) Cursor = Cursors.Default;
        EnsureTimer();
    }

    private void ApplyResize()
    {
        Point p = Cursor.Position;
        int dx = p.X - dragStartCursor.X;
        int dy = p.Y - dragStartCursor.Y;
        Rectangle r = dragStartBounds;
        int left = r.Left, top = r.Top, right = r.Right, bottom = r.Bottom;

        if ((resizeDir & 1) != 0) left = Math.Min(r.Left + dx, right - MinWidth);
        if ((resizeDir & 2) != 0) right = Math.Max(r.Right + dx, left + MinWidth);
        if ((resizeDir & 4) != 0) top = Math.Min(r.Top + dy, bottom - MinHeight);
        if ((resizeDir & 8) != 0) bottom = Math.Max(r.Bottom + dy, top + MinHeight);

        Bounds = new Rectangle(left, top, right - left, bottom - top);
    }

    private int HitEdge(Point p)
    {
        int d = 0;
        if (p.X <= EdgeSize) d |= 1;
        else if (p.X >= ClientSize.Width - EdgeSize) d |= 2;
        if (p.Y <= EdgeSize) d |= 4;
        else if (p.Y >= ClientSize.Height - EdgeSize) d |= 8;
        return d;
    }

    private bool IsInCloseButton(Point p)
    {
        return Math.Abs(p.X - ClientSize.Width / 2) <= CloseRadius &&
               Math.Abs(p.Y - ClientSize.Height / 2) <= CloseRadius;
    }

    private void UpdateCursor(Point p)
    {
        if (IsInCloseButton(p))
        {
            Cursor = Cursors.Hand;
            return;
        }
        int d = HitEdge(p);
        if (d == 1 || d == 2) Cursor = Cursors.SizeWE;
        else if (d == 4 || d == 8) Cursor = Cursors.SizeNS;
        else if (d == 5 || d == 10) Cursor = Cursors.SizeNWSE;
        else if (d == 9 || d == 6) Cursor = Cursors.SizeNESW;
        else Cursor = Cursors.Default;
    }

    private void UiTimerTick(object sender, EventArgs e)
    {
        bool need = false;
        bool reveal = DateTime.Now < revealUntil;
        float target = (hovering || alwaysShowClose || reveal) ? 1f : 0f;

        if (Math.Abs(hoverAlpha - target) > 0.005f)
        {
            hoverAlpha += (target - hoverAlpha) * 0.28f;
            if (Math.Abs(hoverAlpha - target) <= 0.02f) hoverAlpha = target;
            need = true;
        }
        else if (hoverAlpha != target)
        {
            hoverAlpha = target;
            need = true;
        }

        if (toastText != null && DateTime.Now >= toastUntil)
        {
            toastText = null;
            need = true;
        }

        if (need) Invalidate();

        if (hoverAlpha == target && toastText == null && !(DateTime.Now < revealUntil))
            uiTimer.Stop();
    }

    private void EnsureTimer()
    {
        if (!uiTimer.Enabled) uiTimer.Start();
    }

    private void ShowToast(string text, int ms)
    {
        toastText = text;
        toastUntil = DateTime.Now.AddMilliseconds(ms);
        EnsureTimer();
        Invalidate();
    }

    private ContextMenuStrip BuildMenu()
    {
        ContextMenuStrip m = new ContextMenuStrip();
        m.ShowImageMargin = false;

        ToolStripMenuItem opItem = new ToolStripMenuItem("不透明度");
        int[] ops = new int[] { 100, 90, 80, 70, 60, 50, 40, 30 };
        for (int i = 0; i < ops.Length; i++)
        {
            int percent = ops[i];
            ToolStripMenuItem sub = new ToolStripMenuItem(percent + "%");
            sub.Click += delegate { SetOpacityPercent(percent); };
            opItem.DropDownItems.Add(sub);
        }
        m.Items.Add(opItem);

        ToolStripMenuItem hItem = new ToolStripMenuItem("高度");
        int[] heights = new int[] { 40, 56, 72, 90 };
        for (int i = 0; i < heights.Length; i++)
        {
            int h = heights[i];
            ToolStripMenuItem sub = new ToolStripMenuItem(h + " px");
            sub.Click += delegate { ApplyHeightPreset(h); };
            hItem.DropDownItems.Add(sub);
        }
        m.Items.Add(hItem);

        m.Items.Add(new ToolStripSeparator());

        ToolStripMenuItem always = new ToolStripMenuItem("始终显示关闭按钮 X");
        always.CheckOnClick = true;
        always.Checked = alwaysShowClose;
        always.CheckedChanged += delegate
        {
            alwaysShowClose = always.Checked;
            EnsureTimer();
            Invalidate();
            SaveSettings();
        };
        m.Items.Add(always);

        m.Items.Add("重置位置和大小", null, delegate { ResetPlacement(); });
        m.Items.Add("退出", null, delegate { Close(); });

        return m;
    }

    private void SetOpacityPercent(int percent)
    {
        opacity = Math.Max(0.25, Math.Min(1.0, percent / 100.0));
        Opacity = opacity;
        ShowToast("不透明度 " + (int)Math.Round(opacity * 100) + "%", 1200);
        SaveSettings();
    }

    private void ApplyHeightPreset(int h)
    {
        int bottom = Bottom;
        int newH = Math.Max(MinHeight, h);
        Bounds = new Rectangle(Left, bottom - newH, Width, newH);
        ShowToast("高度 " + newH + " px", 1200);
        SaveSettings();
    }

    private void ResetPlacement()
    {
        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
        int w = Math.Max(MinWidth, (int)(wa.Width * 0.66));
        int h = 64;
        Bounds = new Rectangle(wa.Left + (wa.Width - w) / 2, wa.Bottom - h - 26, w, h);
        revealUntil = DateTime.Now.AddMilliseconds(2200);
        ShowToast("已重置位置和大小", 1600);
        SaveSettings();
    }

    private static string[] GetSettingsCandidates()
    {
        // 优先放在 exe 旁边（便携式），不行再用用户配置目录
        string portable = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SubtitleMask.ini");
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData)) return new string[] { portable };
        return new string[]
        {
            portable,
            Path.Combine(Path.Combine(appData, "SubtitleMask"), "settings.ini")
        };
    }

    private static bool CanWrite(string file)
    {
        try
        {
            string dir = Path.GetDirectoryName(file);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string probe = Path.Combine(dir, Path.GetRandomFileName());
            File.WriteAllText(probe, "probe");
            File.Delete(probe);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string ChooseSettingsPath()
    {
        string[] candidates = GetSettingsCandidates();
        for (int i = 0; i < candidates.Length; i++)
        {
            if (CanWrite(candidates[i])) return candidates[i];
        }
        return null;
    }

    private static bool IsOnScreen(Rectangle r)
    {
        foreach (Screen s in Screen.AllScreens)
        {
            Rectangle inter = Rectangle.Intersect(r, s.WorkingArea);
            if (inter.Width >= 60 && inter.Height >= 10) return true;
        }
        return false;
    }

    private void LoadSettings()
    {
        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
        int w = Math.Max(MinWidth, (int)(wa.Width * 0.66));
        int h = 64;
        int x = wa.Left + (wa.Width - w) / 2;
        int y = wa.Bottom - h - 26;

        try
        {
            if (settingsPath != null && File.Exists(settingsPath))
            {
                foreach (string raw in File.ReadAllLines(settingsPath))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string val = line.Substring(eq + 1).Trim();
                    int n;
                    double d;
                    switch (key)
                    {
                        case "x":
                            if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) x = n;
                            break;
                        case "y":
                            if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) y = n;
                            break;
                        case "w":
                            if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) w = n;
                            break;
                        case "h":
                            if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) h = n;
                            break;
                        case "opacity":
                            if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) opacity = d;
                            break;
                        case "alwaysshowx":
                            alwaysShowClose = (val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase));
                            break;
                    }
                }
            }
        }
        catch (Exception)
        {
        }

        if (w < MinWidth) w = MinWidth;
        if (h < MinHeight) h = MinHeight;
        if (opacity < 0.25) opacity = 0.25;
        if (opacity > 1.0) opacity = 1.0;

        Rectangle r = new Rectangle(x, y, w, h);
        if (!IsOnScreen(r))
        {
            w = Math.Max(MinWidth, (int)(wa.Width * 0.66));
            h = 64;
            r = new Rectangle(wa.Left + (wa.Width - w) / 2, wa.Bottom - h - 26, w, h);
        }
        Bounds = r;
    }

    private void SaveSettings()
    {
        if (settingsPath == null) return;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("X=" + Left.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine("Y=" + Top.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine("W=" + Width.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine("H=" + Height.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine("Opacity=" + opacity.ToString("0.###", CultureInfo.InvariantCulture));
        sb.AppendLine("AlwaysShowX=" + (alwaysShowClose ? "1" : "0"));
        string data = sb.ToString();

        if (TryWriteSettings(settingsPath, data)) return;

        string[] candidates = GetSettingsCandidates();
        for (int i = 0; i < candidates.Length; i++)
        {
            if (string.Equals(candidates[i], settingsPath, StringComparison.OrdinalIgnoreCase)) continue;
            if (TryWriteSettings(candidates[i], data))
            {
                settingsPath = candidates[i];
                return;
            }
        }
    }

    private static bool TryWriteSettings(string path, string data)
    {
        try
        {
            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, data, Encoding.UTF8);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveSettings();
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(mouseHook);
            mouseHook = IntPtr.Zero;
        }
        if (hookForm == this) hookForm = null;

        uiTimer.Stop();
        uiTimer.Dispose();
        menu.Dispose();
        toastFont.Dispose();
        base.OnFormClosed(e);
    }
}
