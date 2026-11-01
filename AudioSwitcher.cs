// AudioSwitcher: tecla Mute -> toque corto = mutear, mantener = cambiar salida de audio.
// Compilar: build.cmd (usa el csc.exe de .NET Framework que viene con Windows).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AudioSwitcher
{
    // ---------------- Core Audio COM ----------------
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorCom { }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int stgmAccess, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PropertyKey { public Guid fmtid; public int pid; }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    struct PropVariant { [FieldOffset(0)] public ushort vt; [FieldOffset(8)] public IntPtr p; }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }

    // Interfaz no documentada (la que usan todos los switchers de audio) para cambiar el dispositivo por defecto.
    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(IntPtr a, IntPtr b);
        [PreserveSig] int GetDeviceFormat(IntPtr a, int b, IntPtr c);
        [PreserveSig] int ResetDeviceFormat(IntPtr a);
        [PreserveSig] int SetDeviceFormat(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int GetProcessingPeriod(IntPtr a, int b, IntPtr c, IntPtr d);
        [PreserveSig] int SetProcessingPeriod(IntPtr a, IntPtr b);
        [PreserveSig] int GetShareMode(IntPtr a, IntPtr b);
        [PreserveSig] int SetShareMode(IntPtr a, IntPtr b);
        [PreserveSig] int GetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int SetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int visible);
    }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    class PolicyConfigClientCom { }

    class AudioDevice { public string Id; public string Name; }

    static class Audio
    {
        const int eRender = 0, eMultimedia = 1, DEVICE_STATE_ACTIVE = 1;
        static PropertyKey PKEY_FriendlyName = new PropertyKey { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };

        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant pv);

        public static List<AudioDevice> GetOutputs()
        {
            var list = new List<AudioDevice>();
            var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            IMMDeviceCollection col;
            Marshal.ThrowExceptionForHR(en.EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE, out col));
            uint n; col.GetCount(out n);
            for (uint i = 0; i < n; i++)
            {
                IMMDevice dev; col.Item(i, out dev);
                string id; dev.GetId(out id);
                string name = id;
                IPropertyStore ps;
                if (dev.OpenPropertyStore(0, out ps) == 0)
                {
                    PropVariant pv;
                    if (ps.GetValue(ref PKEY_FriendlyName, out pv) == 0)
                    {
                        if (pv.vt == 31) name = Marshal.PtrToStringUni(pv.p);
                        PropVariantClear(ref pv);
                    }
                }
                list.Add(new AudioDevice { Id = id, Name = name });
            }
            return list;
        }

        public static string GetDefaultId()
        {
            var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            IMMDevice dev;
            if (en.GetDefaultAudioEndpoint(eRender, eMultimedia, out dev) != 0) return null;
            string id; dev.GetId(out id);
            return id;
        }

        public static void SetDefault(string id)
        {
            var pc = (IPolicyConfig)new PolicyConfigClientCom();
            for (int role = 0; role <= 2; role++) // consola, multimedia, comunicaciones
                Marshal.ThrowExceptionForHR(pc.SetDefaultEndpoint(id, role));
        }
    }

    // ---------------- Config ----------------
    class Config
    {
        public int HoldMs = 500;
        public bool Debug;
        public List<string> Exclude = new List<string>();

        public static Config Load(string path)
        {
            var c = new Config();
            if (!File.Exists(path)) return c;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                var k = line.Substring(0, eq).Trim().ToLowerInvariant();
                var v = line.Substring(eq + 1).Trim();
                if (k == "holdms") int.TryParse(v, out c.HoldMs);
                else if (k == "exclude" && v.Length > 0) c.Exclude.Add(v);
                else if (k == "debug") c.Debug = v == "1" || v.ToLowerInvariant() == "true";
            }
            return c;
        }

        public bool IsExcluded(AudioDevice d)
        {
            return Exclude.Any(x => d.Name.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }

    // ---------------- OSD ----------------
    class Osd : Form
    {
        readonly System.Windows.Forms.Timer hide = new System.Windows.Forms.Timer { Interval = 1600 };
        string text = "";

        public Osd()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(32, 32, 32);
            DoubleBuffered = true;
            Opacity = 0.92;
            hide.Tick += (s, e) => { hide.Stop(); Hide(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80 /*TOOLWINDOW*/ | 0x08000000 /*NOACTIVATE*/ | 0x8 /*TOPMOST*/ | 0x20 /*TRANSPARENT*/;
                return cp;
            }
        }

        public void ShowText(string t)
        {
            text = t;
            using (var g = CreateGraphics())
            using (var f = new Font("Segoe UI", 14f))
            {
                var sz = g.MeasureString(text, f);
                Width = (int)sz.Width + 48;
                Height = (int)sz.Height + 28;
            }
            var wa = Screen.PrimaryScreen.WorkingArea;
            Left = wa.Left + (wa.Width - Width) / 2;
            Top = wa.Bottom - Height - 80;
            Invalidate();
            Show();
            hide.Stop(); hide.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var f = new Font("Segoe UI", 14f))
                TextRenderer.DrawText(e.Graphics, text, f, ClientRectangle, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // ---------------- App ----------------
    class App : ApplicationContext
    {
        const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;
        const int VK_VOLUME_MUTE = 0xAD;
        static readonly IntPtr MY_MARK = (IntPtr)0x41535731; // marca para reconocer nuestras propias pulsaciones

        [StructLayout(LayoutKind.Sequential)]
        struct KBDLLHOOKSTRUCT { public int vkCode, scanCode, flags, time; public IntPtr extra; }
        delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc fn, IntPtr hMod, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int n, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, int flags, IntPtr extra);
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);

        readonly HookProc proc;   // referencia viva para que el GC no se lo lleve
        readonly IntPtr hook;
        readonly System.Windows.Forms.Timer holdTimer = new System.Windows.Forms.Timer();
        readonly Osd osd = new Osd();
        readonly Config cfg;
        readonly string logPath;
        bool keyDown, switched, busy;

        public App(Config cfg, string logPath)
        {
            this.cfg = cfg; this.logPath = logPath;
            holdTimer.Interval = Math.Max(150, cfg.HoldMs);
            holdTimer.Tick += (s, e) => { holdTimer.Stop(); if (keyDown) { switched = true; SwitchNext(); } };

            osd.CreateControl();
            var _ = osd.Handle;

            proc = HookCallback;
            hook = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) Log("SetWindowsHookEx falló: " + Marshal.GetLastWin32Error());
        }

        public static string AppDir { get { return Path.GetDirectoryName(Application.ExecutablePath); } }

        IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var k = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                // Diagnóstico: solo teclas "especiales" (no letras, números ni numpad)
                if (cfg.Debug && !(k.vkCode >= 0x30 && k.vkCode <= 0x5A) && !(k.vkCode >= 0x60 && k.vkCode <= 0x6F))
                    Log(string.Format("key msg=0x{0:X} vk=0x{1:X2} scan=0x{2:X} flags=0x{3:X} t={4}", wParam.ToInt32(), k.vkCode, k.scanCode, k.flags, k.time));
                if (k.vkCode == VK_VOLUME_MUTE && k.extra != MY_MARK)
                {
                    int msg = wParam.ToInt32();
                    if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                    {
                        if (!keyDown) // ignorar auto-repeat
                        {
                            keyDown = true; switched = false;
                            holdTimer.Stop(); holdTimer.Start();
                        }
                        return (IntPtr)1;
                    }
                    if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
                    {
                        holdTimer.Stop();
                        bool wasSwitch = switched;
                        keyDown = false; switched = false;
                        if (!wasSwitch) SendMute();
                        return (IntPtr)1;
                    }
                }
            }
            return CallNextHookEx(hook, nCode, wParam, lParam);
        }

        void SendMute()
        {
            // Reinyectamos la tecla: Windows hace el mute normal (con su OSD). El hook la deja pasar por MY_MARK.
            keybd_event(VK_VOLUME_MUTE, 0, 1 /*EXTENDED*/, MY_MARK);
            keybd_event(VK_VOLUME_MUTE, 0, 1 | 2 /*KEYUP*/, MY_MARK);
        }

        void SwitchNext()
        {
            if (busy) return;
            busy = true;
            Task.Run(() =>
            {
                try
                {
                    var all = Audio.GetOutputs().Where(d => !cfg.IsExcluded(d)).ToList();
                    if (all.Count < 2) { Ui(() => osd.ShowText(all.Count == 0 ? "No hay salidas de audio" : "Solo hay una salida: " + all[0].Name)); return; }
                    var cur = Audio.GetDefaultId();
                    int idx = all.FindIndex(d => d.Id == cur);
                    var next = all[(idx + 1) % all.Count];
                    Audio.SetDefault(next.Id);
                    Ui(() => osd.ShowText("🔊  " + next.Name));
                }
                catch (Exception ex) { Log("Switch: " + ex); Ui(() => osd.ShowText("Error cambiando salida (ver log)")); }
                finally { busy = false; }
            });
        }

        void Ui(Action a) { try { osd.BeginInvoke(a); } catch { } }

        void Log(string s)
        {
            try { File.AppendAllText(logPath, DateTime.Now.ToString("s") + " " + s + Environment.NewLine); } catch { }
        }

        protected override void ExitThreadCore()
        {
            if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
            base.ExitThreadCore();
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            var dir = App.AppDir;
            var cfg = Config.Load(Path.Combine(dir, "config.ini"));

            if (args.Length > 0 && args[0] == "--list")
            {
                var cur = Audio.GetDefaultId();
                foreach (var d in Audio.GetOutputs())
                    Console.WriteLine((d.Id == cur ? "* " : "  ") + d.Name + (cfg.IsExcluded(d) ? "  [excluido]" : ""));
                return;
            }

            if (args.Length > 0 && args[0] == "--exit")
            {
                int me = Process.GetCurrentProcess().Id;
                foreach (var p in Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName))
                    if (p.Id != me) try { p.Kill(); } catch { }
                return;
            }

            bool created;
            using (var mutex = new Mutex(true, "AudioSwitcher.SingleInstance", out created))
            {
                if (!created) return; // ya está corriendo
                Application.EnableVisualStyles();
                Application.Run(new App(cfg, Path.Combine(dir, "audioswitcher.log")));
            }
        }
    }
}
