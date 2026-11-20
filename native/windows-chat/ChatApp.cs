using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace HexChat
{
    // Hex owns both shortcuts and its paste pipeline. The picker
    // remembers the user's selection and answers two bounded focus queries.
    internal sealed class ChatApp : Form
    {
        const int ActivateTarget = 0x8000 + 51, CheckTarget = 0x8000 + 52;
        const int HostExited = 0x8000 + 54, ResetSelection = 0x8000 + 55;
        // Shared with windows/chat_target.rs. These window properties contain integers only.
        enum Selection { Automatic = 1, Selecting, Field, Window, Failed, HotkeyUnavailable, Unbound }
        readonly bool pinOnly, canBind;
        int revision;
        FocusTarget target;
        readonly Dictionary<int, FocusTarget> captures = new Dictionary<int, FocusTarget>();
        readonly Queue<int> recent = new Queue<int>();
        bool selecting, pinned;
        Task probe;
        ChatApp(bool pinOnly)
        {
            this.pinOnly = pinOnly;
            Text = "HexChat.Picker.v1";
            ShowInTaskbar = false;
            canBind = RegisterHotKey(Handle, 1, 0x4003, (uint)Keys.F8);
            Reset();
        }

        void Report(Selection selection) { SetProp(Handle, "HexChat.Selection", new IntPtr((int)selection)); }
        int Options { get { return GetProp(Handle, "HexChat.Options").ToInt32(); } }

        void Reset()
        {
            revision++;
            Forget();
            pinned = pinOnly;
            Report(!canBind ? Selection.HotkeyUnavailable : pinned ? Selection.Unbound : Selection.Automatic);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == HostExited) { Application.ExitThread(); return; }
            if (m.Msg == ResetSelection) { Reset(); return; }
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == 1) { Bind(); return; }
            if (m.Msg == ActivateTarget || m.Msg == CheckTarget)
            {
                var chosen = target;
                m.Result = IntPtr.Zero;
                if ((Options & 2) == 0 || selecting || (probe != null && !probe.IsCompleted)) return;
                try
                {
                    if (m.Msg == ActivateTarget)
                    {
                        var origin = m.WParam;
                        int deadline = unchecked((int)m.LParam.ToInt64());
                        int remaining = unchecked(deadline - Environment.TickCount);
                        if (remaining < 1 || remaining > 1000) return;
                        bool manual = pinned;
                        var activate = Task.Run(() => {
                            var candidate = manual ? chosen : FocusTarget.FindTwitch(deadline);
                            return candidate != null && candidate.TryFocus(origin, deadline) ? candidate : null;
                        });
                        probe = activate;
                        if (activate.Wait(remaining) && activate.Result != null)
                        {
                            int binding;
                            do { binding = NewBinding(); } while (captures.ContainsKey(binding));
                            captures.Add(binding, activate.Result);
                            recent.Enqueue(binding);
                            if (recent.Count > 32) captures.Remove(recent.Dequeue());
                            m.Result = new IntPtr(binding);
                        }
                    }
                    else if (captures.TryGetValue(unchecked((int)m.WParam.ToInt64()), out chosen))
                    {
                        var check = Task.Run(() => chosen.IsFocused());
                        probe = check;
                        if (check.Wait(300) && check.Result) m.Result = new IntPtr(1);
                    }
                }
                catch (AggregateException) { /* Closed windows and stale fields fall back in Hex. */ }
                catch (System.Windows.Automation.ElementNotAvailableException) { target = null; }
                return;
            }
            base.WndProc(ref m);
        }

        async void Bind()
        {
            int options = Options;
            if (selecting || (options & 2) == 0) return;
            selecting = true;
            int attempt = ++revision;
            Forget();
            pinned = true;
            Report(Selection.Selecting);
            var window = FocusTarget.Foreground();
            bool onlyWindow = (options & 4) != 0;
            try
            {
                var found = await Task.Run(() => FocusTarget.Bind(window, onlyWindow));
                if (attempt != revision) return;
                target = found;
                Report(onlyWindow ? Selection.Window : Selection.Field);
            }
            catch (Exception) { if (attempt == revision) Report(Selection.Failed); }
            finally { selecting = false; }
        }

        void Forget() { target = null; captures.Clear(); recent.Clear(); }
        static int NewBinding() { return (Guid.NewGuid().GetHashCode() & Int32.MaxValue) | 1; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (IsHandleCreated) UnregisterHotKey(Handle, 1);
                if (IsHandleCreated)
                {
                    RemoveProp(Handle, "HexChat.Selection");
                    RemoveProp(Handle, "HexChat.Options");
                }
            }
            base.Dispose(disposing);
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetProp(IntPtr window, string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SetProp(IntPtr window, string name, IntPtr value);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr RemoveProp(IntPtr window, string name);
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, int message, IntPtr first, IntPtr second);

        [STAThread]
        static void Main(string[] args)
        {
            string engine = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hex.exe");
            if (args.Length == 0 || (args.Length == 1 && args[0] == "--pin-only"))
            {
                try
                {
                    var launch = new ProcessStartInfo(engine, "app") { UseShellExecute = false };
                    if (args.Length != 0) launch.EnvironmentVariables["HEX_CHAT_PIN_ONLY"] = "1";
                    using (Process.Start(launch)) { }
                }
                catch (Exception e) { MessageBox.Show(e.Message, "Hex Chat", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                return;
            }
            int pid;
            if (args.Length != 2 || args[0] != "--host" || !Int32.TryParse(args[1], out pid)) return;
            try
            {
                using (var host = Process.GetProcessById(pid))
                {
                    if (!String.Equals(host.MainModule.FileName, engine, StringComparison.OrdinalIgnoreCase)) return;
                    Application.EnableVisualStyles();
                    using (var app = new ChatApp(Environment.GetEnvironmentVariable("HEX_CHAT_PIN_ONLY") == "1"))
                    {
                        var handle = app.Handle;
                        host.Exited += delegate { PostMessage(handle, HostExited, IntPtr.Zero, IntPtr.Zero); };
                        host.EnableRaisingEvents = true;
                        if (!host.HasExited) Application.Run();
                    }
                }
            }
            catch (ArgumentException) { /* Hex exited before its helper opened. */ }
            catch (InvalidOperationException) { /* The host has already closed. */ }
        }
    }
}
