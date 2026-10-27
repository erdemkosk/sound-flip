// SoundFlip - kulaklik <-> hoparlor arasinda tek tusla gecis.
// Gorev cubugu ikonu: sol tik = gecis, sag tik = menu. Kisayol: Ctrl+Alt+S (degistirilebilir).
// Cihazlar ayni klasordeki devices.txt'den okunur:  <ad parcasi>=headphones|speakers
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SoundFlip {

  // Arayuz dili: L.T("Turkce", "English")
  static class L {
    public const string AppName = "SoundFlip";
    public static bool English;
    public static string T(string tr, string en) { return English ? en : tr; }
  }

  // ---- Windows Core Audio COM arayuzleri ----

  [StructLayout(LayoutKind.Sequential)]
  struct PROPERTYKEY { public Guid fmtid; public int pid; }

  [StructLayout(LayoutKind.Explicit)]
  struct PROPVARIANT {
    [FieldOffset(0)] public short vt;
    [FieldOffset(8)] public IntPtr pwszVal;
    [FieldOffset(16)] public long pad;
  }

  [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface IPropertyStore {
    void GetCount(out int c);
    void GetAt(int i, out PROPERTYKEY k);
    void GetValue(ref PROPERTYKEY k, out PROPVARIANT v);
  }

  [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface IMMDevice {
    void Activate(IntPtr iid, int ctx, IntPtr p, out IntPtr o);
    void OpenPropertyStore(int access, out IPropertyStore ps);
    void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
  }

  [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface IMMDeviceCollection {
    void GetCount(out int c);
    void Item(int i, out IMMDevice d);
  }

  [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface IMMDeviceEnumerator {
    void EnumAudioEndpoints(int flow, int mask, out IMMDeviceCollection c);
    [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice d);
  }

  [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
  class MMDeviceEnumerator {}

  [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface IPolicyConfig {
    void GetMixFormat(IntPtr a, IntPtr b);
    void GetDeviceFormat(IntPtr a, int b, IntPtr c);
    void ResetDeviceFormat(IntPtr a);
    void SetDeviceFormat(IntPtr a, IntPtr b, IntPtr c);
    void GetProcessingPeriod(IntPtr a, int b, IntPtr c, IntPtr d);
    void SetProcessingPeriod(IntPtr a, IntPtr b);
    void GetShareMode(IntPtr a, IntPtr b);
    void SetShareMode(IntPtr a, IntPtr b);
    void GetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
    void SetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
    void SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
  }

  [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
  class CPolicyConfigClient {}

  class Device {
    public string Id, Name, Short;
    public bool Headphone;
    public string ConfigKey; // cihazlar.txt'de eslesen satir (yoksa null)
  }

  static class Audio {
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT v);

    static readonly string[] HeadphoneWords = { "kulakl", "headphone", "headset", "earphone", "buds" };

    static string ConfigPath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "devices.txt"); } }

    // Aktif cikis cihazlari; cihazlar.txt varsa sadece oradakiler, o sirayla
    public static List<Device> Devices() {
      var config = LoadConfig();
      var all = AllDevices();
      if (config.Count == 0) return all;
      var result = new List<Device>();
      foreach (var entry in config) {
        foreach (var dev in all) {
          if (dev.Name.IndexOf(entry.Key, StringComparison.OrdinalIgnoreCase) >= 0 && !result.Contains(dev)) {
            dev.Headphone = entry.Value;
            dev.ConfigKey = entry.Key;
            result.Add(dev);
          }
        }
      }
      return result;
    }

    // Gecis listesinde olmayan aktif cihazlar (yeni kulaklik vb.)
    public static List<Device> Unlisted() {
      var listed = Devices();
      if (LoadConfig().Count == 0) return new List<Device>();
      return AllDevices().FindAll(a => !listed.Exists(l => l.Id == a.Id));
    }

    public static void AddToList(Device d, bool headphone) {
      var lines = new List<string>(File.Exists(ConfigPath) ? File.ReadAllLines(ConfigPath) : new string[0]);
      lines.Add(d.Short + "=" + (headphone ? "headphones" : "speakers"));
      File.WriteAllLines(ConfigPath, lines.ToArray());
    }

    public static void RemoveFromList(Device d) {
      if (d.ConfigKey == null || !File.Exists(ConfigPath)) return;
      var lines = new List<string>(File.ReadAllLines(ConfigPath));
      lines.RemoveAll(l => l.Split('=')[0].Trim().Equals(d.ConfigKey, StringComparison.OrdinalIgnoreCase));
      File.WriteAllLines(ConfigPath, lines.ToArray());
    }

    static List<Device> AllDevices() {
      var en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
      IMMDeviceCollection col;
      en.EnumAudioEndpoints(0, 1, out col);
      int n; col.GetCount(out n);
      var all = new List<Device>();
      var key = new PROPERTYKEY { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
      for (int i = 0; i < n; i++) {
        IMMDevice d; col.Item(i, out d);
        string id; d.GetId(out id);
        IPropertyStore ps; d.OpenPropertyStore(0, out ps);
        PROPVARIANT v; ps.GetValue(ref key, out v);
        string name = Marshal.PtrToStringUni(v.pwszVal) ?? id;
        PropVariantClear(ref v);
        var dev = new Device { Id = id, Name = name, Short = ShortName(name) };
        string lower = name.ToLowerInvariant();
        foreach (var w in HeadphoneWords) if (lower.Contains(w)) dev.Headphone = true;
        all.Add(dev);
      }
      return all;
    }

    static List<KeyValuePair<string, bool>> LoadConfig() {
      var list = new List<KeyValuePair<string, bool>>();
      if (!File.Exists(ConfigPath)) return list;
      foreach (var raw in File.ReadAllLines(ConfigPath)) {
        var parts = raw.Split('=');
        string name = parts[0].Trim();
        if (name.Length == 0 || name.StartsWith("#")) continue;
        string kind = parts.Length > 1 ? parts[1].Trim().ToLowerInvariant() : "";
        list.Add(new KeyValuePair<string, bool>(name, kind.StartsWith("kul") || kind.StartsWith("head")));
      }
      return list;
    }

    // "Hoparlor (SteelSeries Arena 7)" -> "SteelSeries Arena 7"
    static string ShortName(string name) {
      int a = name.IndexOf('('), b = name.LastIndexOf(')');
      return (a >= 0 && b > a + 1) ? name.Substring(a + 1, b - a - 1) : name;
    }

    public static string DefaultId() {
      var en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
      IMMDevice d;
      if (en.GetDefaultAudioEndpoint(0, 1, out d) != 0) return null;
      string id; d.GetId(out id);
      return id;
    }

    public static void SetDefault(string id) {
      var pc = (IPolicyConfig)new CPolicyConfigClient();
      for (int role = 0; role < 3; role++) pc.SetDefaultEndpoint(id, role);
    }
  }

  // ---- Gorunum ----

  static class Theme {
    public const string HeadphoneGlyph = "";
    public const string SpeakerGlyph = "";
    public static readonly Color Accent = Color.FromArgb(76, 194, 255);
    public static readonly Color Back = Color.FromArgb(32, 32, 32);
    public static readonly Color Hover = Color.FromArgb(55, 55, 55);
    public static readonly Color Border = Color.FromArgb(64, 64, 64);
    public static string IconFont;
    public static float Scale = 1f;

    public static void Init() {
      IconFont = "Segoe MDL2 Assets";
      using (var fc = new InstalledFontCollection())
        foreach (var f in fc.Families) if (f.Name == "Segoe Fluent Icons") IconFont = f.Name;
      using (var g = Graphics.FromHwnd(IntPtr.Zero)) Scale = g.DpiX / 96f;
    }

    public static bool TaskbarIsLight() {
      using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) {
        object v = k == null ? null : k.GetValue("SystemUsesLightTheme");
        return v is int && (int)v == 1;
      }
    }

    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

    public static Icon TrayIcon(bool headphone) {
      int s = SystemInformation.SmallIconSize.Width;
      using (var bmp = new Bitmap(s, s))
      using (var g = Graphics.FromImage(bmp))
      using (var f = new Font(IconFont, s * 0.85f, GraphicsUnit.Pixel))
      using (var b = new SolidBrush(TaskbarIsLight() ? Color.Black : Color.White))
      using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) {
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        g.DrawString(headphone ? HeadphoneGlyph : SpeakerGlyph, f, b, new RectangleF(0, 0, s, s), sf);
        IntPtr h = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(h).Clone();
        DestroyIcon(h);
        return icon;
      }
    }
  }

  class DarkColors : ProfessionalColorTable {
    public override Color ToolStripDropDownBackground { get { return Theme.Back; } }
    public override Color ImageMarginGradientBegin { get { return Theme.Back; } }
    public override Color ImageMarginGradientMiddle { get { return Theme.Back; } }
    public override Color ImageMarginGradientEnd { get { return Theme.Back; } }
    public override Color MenuBorder { get { return Theme.Border; } }
    public override Color MenuItemBorder { get { return Theme.Hover; } }
    public override Color MenuItemSelected { get { return Theme.Hover; } }
    public override Color SeparatorDark { get { return Theme.Border; } }
    public override Color SeparatorLight { get { return Theme.Back; } }
    public override Color CheckBackground { get { return Theme.Back; } }
    public override Color CheckSelectedBackground { get { return Theme.Hover; } }
    public override Color CheckPressedBackground { get { return Theme.Hover; } }
  }

  class DarkRenderer : ToolStripProfessionalRenderer {
    public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) {
      e.TextColor = e.Item.Enabled ? Color.White : Color.Gray;
      base.OnRenderItemText(e);
    }
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) {
      var r = e.ImageRectangle;
      using (var f = new Font(Theme.IconFont, r.Height * 0.7f, GraphicsUnit.Pixel))
      using (var b = new SolidBrush(Theme.Accent))
      using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) {
        e.Graphics.TextRenderingHint = TextRenderingHint.AntiAlias;
        e.Graphics.DrawString("", f, b, r, sf);
      }
    }
  }

  // Ekranin altinda beliren, kendiliginden kaybolan bildirim
  class Osd : Form {
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

    string title = "", sub = "";
    bool headphone;
    int phase, held;
    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 15 };

    public Osd() {
      FormBorderStyle = FormBorderStyle.None;
      ShowInTaskbar = false;
      TopMost = true;
      StartPosition = FormStartPosition.Manual;
      BackColor = Theme.Back;
      DoubleBuffered = true;
      Size = new Size((int)(300 * Theme.Scale), (int)(72 * Theme.Scale));
      Opacity = 0;
      timer.Tick += Step;
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams {
      get {
        var cp = base.CreateParams;
        cp.ExStyle |= 0x08000000 | 0x80 | 0x8; // NOACTIVATE | TOOLWINDOW | TOPMOST
        return cp;
      }
    }

    protected override void OnHandleCreated(EventArgs e) {
      base.OnHandleCreated(e);
      int round = 2;      // Windows 11 yuvarlak koseler
      DwmSetWindowAttribute(Handle, 33, ref round, 4);
      int border = Theme.Border.R | (Theme.Border.G << 8) | (Theme.Border.B << 16);
      DwmSetWindowAttribute(Handle, 34, ref border, 4);
    }

    public void ShowDevice(Device d) {
      headphone = d.Headphone;
      title = d.Headphone ? L.T("Kulaklık", "Headphones") : L.T("Hoparlör", "Speakers");
      sub = d.Short;
      var wa = Screen.PrimaryScreen.WorkingArea;
      Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Bottom - Height - (int)(28 * Theme.Scale));
      if (!Visible) { Opacity = 0; Show(); }
      phase = 0; held = 0;
      Invalidate();
      timer.Start();
    }

    void Step(object s, EventArgs e) {
      if (phase == 0) {
        Opacity = Math.Min(1.0, Opacity + 0.15);
        if (Opacity >= 1.0) phase = 1;
      } else if (phase == 1) {
        held += timer.Interval;
        if (held >= 1400) phase = 2;
      } else {
        Opacity = Math.Max(0.0, Opacity - 0.06);
        if (Opacity <= 0) { timer.Stop(); Hide(); }
      }
    }

    protected override void OnPaint(PaintEventArgs e) {
      float s = Theme.Scale;
      var g = e.Graphics;
      g.SmoothingMode = SmoothingMode.AntiAlias;

      var circle = new RectangleF(16 * s, (Height - 42 * s) / 2, 42 * s, 42 * s);
      using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, circle);

      g.TextRenderingHint = TextRenderingHint.AntiAlias;
      using (var f = new Font(Theme.IconFont, 19 * s, GraphicsUnit.Pixel))
      using (var b = new SolidBrush(Color.FromArgb(20, 20, 20)))
      using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        g.DrawString(headphone ? Theme.HeadphoneGlyph : Theme.SpeakerGlyph, f, b, circle, sf);

      g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
      float x = 72 * s, w = Width - x - 14 * s;
      using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap }) {
        using (var f = new Font("Segoe UI Semibold", 15 * s, GraphicsUnit.Pixel))
          g.DrawString(title, f, Brushes.White, new RectangleF(x, 13 * s, w, 22 * s), sf);
        using (var f = new Font("Segoe UI", 12 * s, GraphicsUnit.Pixel))
        using (var b = new SolidBrush(Color.FromArgb(175, 175, 175)))
          g.DrawString(sub, f, b, new RectangleF(x, 37 * s, w, 20 * s), sf);
      }
    }
  }

  // ---- Global kisayol ----

  class HotkeyWindow : NativeWindow, IDisposable {
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);

    public event Action Pressed;
    bool registered;

    public HotkeyWindow() {
      CreateHandle(new CreateParams());
    }

    public bool Register(Keys combo) {
      Unregister();
      uint mods = 0x4000; // NoRepeat
      if ((combo & Keys.Alt) != 0) mods |= 0x1;
      if ((combo & Keys.Control) != 0) mods |= 0x2;
      if ((combo & Keys.Shift) != 0) mods |= 0x4;
      registered = RegisterHotKey(Handle, 1, mods, (uint)(combo & Keys.KeyCode));
      return registered;
    }

    public void Unregister() {
      if (registered) UnregisterHotKey(Handle, 1);
      registered = false;
    }

    protected override void WndProc(ref Message m) {
      if (m.Msg == 0x0312 && Pressed != null) Pressed();
      base.WndProc(ref m);
    }

    public void Dispose() {
      Unregister();
      DestroyHandle();
    }
  }

  // Ayarlar settings.txt'de saklanir:  hotkey=Ctrl+Alt+S  /  language=tr
  static class Settings {
    static readonly string Path_ = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.txt");
    static readonly KeysConverter Converter = new KeysConverter();
    public static readonly Keys DefaultHotkey = Keys.Control | Keys.Alt | Keys.S;

    static Dictionary<string, string> Load() {
      var d = new Dictionary<string, string>();
      if (File.Exists(Path_))
        foreach (var line in File.ReadAllLines(Path_)) {
          int i = line.IndexOf('=');
          if (i > 0) d[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
        }
      return d;
    }

    static string Get(string key) {
      string v;
      return Load().TryGetValue(key, out v) ? v : null;
    }

    static void Set(string key, string value) {
      var d = Load();
      d[key] = value;
      var lines = new List<string>();
      foreach (var kv in d) lines.Add(kv.Key + "=" + kv.Value);
      File.WriteAllLines(Path_, lines.ToArray());
    }

    public static Keys LoadHotkey() {
      try {
        string v = Get("hotkey");
        if (v != null) return (Keys)Converter.ConvertFromInvariantString(v);
      } catch { }
      return DefaultHotkey;
    }

    public static void SaveHotkey(Keys k) { Set("hotkey", Text(k)); }

    // Ayar yoksa Windows dili Turkce ise Turkce, degilse Ingilizce
    public static bool LoadEnglish() {
      string v = Get("language");
      if (v == null) return System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "tr";
      return v == "en";
    }

    public static void SaveEnglish(bool en) { Set("language", en ? "en" : "tr"); }

    public static string Text(Keys k) {
      return Converter.ConvertToInvariantString(k);
    }
  }

  // "Yeni kisayola bas" penceresi
  class HotkeyDialog : Form {
    public Keys Result;
    readonly Label hint = new Label();

    public HotkeyDialog(Keys current) {
      float s = Theme.Scale;
      Text = L.T("Kısayolu değiştir", "Change shortcut");
      FormBorderStyle = FormBorderStyle.FixedToolWindow;
      StartPosition = FormStartPosition.CenterScreen;
      TopMost = true;
      KeyPreview = true;
      BackColor = Theme.Back;
      ClientSize = new Size((int)(320 * s), (int)(110 * s));

      var title = new Label {
        Text = L.T("Yeni kısayola bas", "Press the new shortcut"),
        ForeColor = Color.White,
        Font = new Font("Segoe UI Semibold", 15 * s, GraphicsUnit.Pixel),
        AutoSize = false, TextAlign = ContentAlignment.MiddleCenter,
        Bounds = new Rectangle(0, (int)(14 * s), ClientSize.Width, (int)(24 * s))
      };
      hint.Text = Settings.Text(current);
      hint.ForeColor = Theme.Accent;
      hint.Font = new Font("Segoe UI", 18 * s, GraphicsUnit.Pixel);
      hint.AutoSize = false;
      hint.TextAlign = ContentAlignment.MiddleCenter;
      hint.Bounds = new Rectangle(0, (int)(42 * s), ClientSize.Width, (int)(30 * s));
      var foot = new Label {
        Text = L.T("Ctrl / Alt / Shift + bir tuş  ·  Esc: vazgeç", "Ctrl / Alt / Shift + a key  ·  Esc: cancel"),
        ForeColor = Color.FromArgb(150, 150, 150),
        Font = new Font("Segoe UI", 11 * s, GraphicsUnit.Pixel),
        AutoSize = false, TextAlign = ContentAlignment.MiddleCenter,
        Bounds = new Rectangle(0, (int)(80 * s), ClientSize.Width, (int)(20 * s))
      };
      Controls.Add(title);
      Controls.Add(hint);
      Controls.Add(foot);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
      Keys code = keyData & Keys.KeyCode;
      if (code == Keys.Escape) { DialogResult = DialogResult.Cancel; return true; }
      bool modOnly = code == Keys.ControlKey || code == Keys.Menu || code == Keys.ShiftKey || code == Keys.None;
      if (modOnly || (keyData & (Keys.Control | Keys.Alt)) == 0) {
        hint.Text = Settings.Text(keyData & Keys.Modifiers) + (modOnly ? "+…" : L.T("  (Ctrl veya Alt gerekli)", "  (Ctrl or Alt required)"));
        return true;
      }
      Result = keyData;
      DialogResult = DialogResult.OK;
      return true;
    }
  }

  // ---- Uygulama ----

  class TrayApp : ApplicationContext {
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    readonly NotifyIcon tray = new NotifyIcon();
    readonly ContextMenuStrip menu = new ContextMenuStrip();
    readonly Osd osd = new Osd();
    readonly HotkeyWindow hotkey = new HotkeyWindow();
    readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer { Interval = 2000 };
    string shownId = "";
    Keys hotkeyCombo = Settings.LoadHotkey();

    public TrayApp() {
      ToolStripManager.Renderer = new DarkRenderer(); // alt menuler de koyu olsun
      menu.Renderer = ToolStripManager.Renderer;
      menu.ShowImageMargin = true;
      menu.Opening += (s, e) => { BuildMenu(); e.Cancel = false; };
      tray.ContextMenuStrip = menu;
      tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) Toggle(); };
      hotkey.Pressed += Toggle;
      poll.Tick += (s, e) => RefreshTray();
      poll.Start();
      RefreshTray();
      tray.Visible = true;
      if (!hotkey.Register(hotkeyCombo)) WarnHotkeyTaken(hotkeyCombo);
    }

    void WarnHotkeyTaken(Keys k) {
      tray.ShowBalloonTip(4000, L.AppName, Settings.Text(k) + L.T(" başka bir program tarafından kullanılıyor. Sağ tık → Kısayolu değiştir.",
        " is used by another program. Right-click → Change shortcut."), ToolTipIcon.Warning);
    }

    void ChangeHotkey() {
      hotkey.Unregister();
      using (var dlg = new HotkeyDialog(hotkeyCombo)) {
        if (dlg.ShowDialog() == DialogResult.OK) {
          if (hotkey.Register(dlg.Result)) {
            hotkeyCombo = dlg.Result;
            Settings.SaveHotkey(hotkeyCombo);
            tray.ShowBalloonTip(2000, L.AppName, L.T("Yeni kısayol: ", "New shortcut: ") + Settings.Text(hotkeyCombo), ToolTipIcon.Info);
            return;
          }
          WarnHotkeyTaken(dlg.Result);
        }
      }
      hotkey.Register(hotkeyCombo);
    }

    void Toggle() {
      try {
        var devs = Audio.Devices();
        if (devs.Count == 0) return;
        string cur = Audio.DefaultId();
        int idx = devs.FindIndex(d => d.Id == cur);
        var next = devs[(idx + 1) % devs.Count];
        Select(next);
      } catch (Exception ex) {
        tray.ShowBalloonTip(3000, L.AppName, ex.Message, ToolTipIcon.Error);
      }
    }

    void Select(Device d) {
      Audio.SetDefault(d.Id);
      RefreshTray();
      osd.ShowDevice(d);
    }

    // Varsayilan cihaz Windows'tan elle degistirilirse ikon da guncellensin
    void RefreshTray() {
      string cur;
      try { cur = Audio.DefaultId() ?? ""; } catch { return; }
      if (cur == shownId) return;
      shownId = cur;
      Device d = Audio.Devices().Find(x => x.Id == cur);
      var old = tray.Icon;
      tray.Icon = Theme.TrayIcon(d != null && d.Headphone);
      if (old != null) old.Dispose();
      string text = d == null ? L.AppName
        : L.AppName + " · " + (d.Headphone ? L.T("Kulaklık: ", "Headphones: ") : L.T("Hoparlör: ", "Speakers: ")) + d.Short;
      tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;
    }

    void BuildMenu() {
      menu.Items.Clear();
      string cur = Audio.DefaultId();
      var listed = Audio.Devices();
      foreach (var d in listed) {
        var dev = d;
        var item = new ToolStripMenuItem((d.Headphone ? L.T("Kulaklık", "Headphones") : L.T("Hoparlör", "Speakers")) + "  ·  " + d.Short);
        item.Checked = d.Id == cur;
        item.Click += (s, e) => Select(dev);
        menu.Items.Add(item);
      }
      menu.Items.Add(new ToolStripSeparator());

      // Yeni takilan cihazi listeye ekle
      var unlisted = Audio.Unlisted();
      var add = new ToolStripMenuItem(L.T("Cihaz ekle", "Add device")) { Enabled = unlisted.Count > 0 };
      foreach (var d in unlisted) {
        var dev = d;
        var sub = new ToolStripMenuItem(d.Short);
        var asHp = new ToolStripMenuItem(L.T("Kulaklık olarak", "As headphones"));
        asHp.Click += (s, e) => { Audio.AddToList(dev, true); shownId = ""; RefreshTray(); };
        var asSp = new ToolStripMenuItem(L.T("Hoparlör olarak", "As speakers"));
        asSp.Click += (s, e) => { Audio.AddToList(dev, false); shownId = ""; RefreshTray(); };
        sub.DropDownItems.Add(asHp);
        sub.DropDownItems.Add(asSp);
        add.DropDownItems.Add(sub);
      }
      menu.Items.Add(add);

      var remove = new ToolStripMenuItem(L.T("Cihaz çıkar", "Remove device")) { Enabled = listed.Exists(d => d.ConfigKey != null) };
      foreach (var d in listed) {
        if (d.ConfigKey == null) continue;
        var dev = d;
        var sub = new ToolStripMenuItem(d.Short);
        sub.Click += (s, e) => { Audio.RemoveFromList(dev); shownId = ""; RefreshTray(); };
        remove.DropDownItems.Add(sub);
      }
      menu.Items.Add(remove);
      menu.Items.Add(new ToolStripSeparator());
      var toggle = new ToolStripMenuItem(L.T("Geçiş yap", "Switch")) { ShortcutKeyDisplayString = Settings.Text(hotkeyCombo) };
      toggle.Click += (s, e) => Toggle();
      menu.Items.Add(toggle);
      var change = new ToolStripMenuItem(L.T("Kısayolu değiştir…", "Change shortcut…"));
      change.Click += (s, e) => ChangeHotkey();
      menu.Items.Add(change);
      var edit = new ToolStripMenuItem(L.T("Cihaz listesini düzenle…", "Edit device list…"));
      edit.Click += (s, e) => System.Diagnostics.Process.Start("notepad.exe",
        "\"" + Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "devices.txt") + "\"");
      menu.Items.Add(edit);
      var lang = new ToolStripMenuItem(L.T("Dil", "Language"));
      var tr = new ToolStripMenuItem("Türkçe") { Checked = !L.English };
      tr.Click += (s, e) => SetLanguage(false);
      var en = new ToolStripMenuItem("English") { Checked = L.English };
      en.Click += (s, e) => SetLanguage(true);
      lang.DropDownItems.Add(tr);
      lang.DropDownItems.Add(en);
      menu.Items.Add(lang);
      var startup = new ToolStripMenuItem(L.T("Windows ile başlat", "Start with Windows")) { Checked = StartsWithWindows() };
      startup.Click += (s, e) => SetStartsWithWindows(!StartsWithWindows());
      menu.Items.Add(startup);
      menu.Items.Add(new ToolStripSeparator());
      var exit = new ToolStripMenuItem(L.T("Çıkış", "Exit"));
      exit.Click += (s, e) => ExitThread();
      menu.Items.Add(exit);
    }

    static bool StartsWithWindows() {
      using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("SoundFlip") != null;
    }

    static void SetStartsWithWindows(bool on) {
      using (var k = Registry.CurrentUser.CreateSubKey(RunKey)) {
        if (on) k.SetValue("SoundFlip", "\"" + Application.ExecutablePath + "\"");
        else k.DeleteValue("SoundFlip", false);
      }
    }

    void SetLanguage(bool english) {
      L.English = english;
      Settings.SaveEnglish(english);
      shownId = "";
      RefreshTray();
    }

    protected override void ExitThreadCore() {
      tray.Visible = false;
      tray.Dispose();
      hotkey.Dispose();
      osd.Dispose();
      base.ExitThreadCore();
    }
  }

  static class Program {
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main() {
      bool first;
      using (new Mutex(true, "SoundFlip_SingleInstance", out first)) {
        if (!first) return;
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Theme.Init();
        L.English = Settings.LoadEnglish();
        Application.Run(new TrayApp());
      }
    }
  }
}
