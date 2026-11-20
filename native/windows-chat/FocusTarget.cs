using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace HexChat
{
    // Keep the actual control for this session. A similar title or a control at
    // the same screen position does not identify the user's chosen draft.
    internal sealed class FocusTarget
    {
        readonly IntPtr window;
        readonly uint process;
        readonly AutomationElement editor, document;
        readonly string page;

        FocusTarget(IntPtr window, uint process, AutomationElement editor, AutomationElement document)
        {
            this.window = window;
            this.process = process;
            this.editor = editor;
            this.document = document;
            page = DocumentAddress(document);
        }

        internal static FocusTarget Bind(IntPtr window, bool windowOnly)
        {
            uint pid;
            GetWindowThreadProcessId(window, out pid);
            if (window == IntPtr.Zero || pid == 0 || pid == Process.GetCurrentProcess().Id)
                throw new InvalidOperationException("Clique dans la fenêtre à cibler.");
            if (windowOnly) return new FocusTarget(window, pid, null, null);

            var root = AutomationElement.FromHandle(window);
            // Chromium can enable accessibility lazily when the first client arrives.
            for (int i = 0; i < 12 && Foreground() == window; i++)
            {
                var field = AutomationElement.FocusedElement;
                if (field != null && field.Current.ControlType == ControlType.Edit &&
                    field.Current.IsEnabled && !field.Current.IsPassword)
                {
                    AutomationElement doc = null;
                    var walker = TreeWalker.RawViewWalker;
                    for (var node = field; node != null; node = walker.GetParent(node))
                    {
                        if (doc == null && node.Current.ControlType == ControlType.Document) doc = node;
                        if (Automation.Compare(node, root))
                        {
                            if (Foreground() == window) return new FocusTarget(window, pid, field, doc);
                            break;
                        }
                    }
                }
                Thread.Sleep(65);
            }
            throw new InvalidOperationException("Clique dans la zone de saisie du chat et refais Ctrl + Alt + F8.");
        }

        static string DocumentAddress(AutomationElement element)
        {
            object pattern;
            if (element == null) return null;
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out pattern))
                return ((ValuePattern)pattern).Current.Value;
            return null;
        }

        internal static FocusTarget FindTwitch(int deadline)
        {
            // First filter cheap window titles. Only inspect visible browser
            // documents, and confirm the URL before considering an editor.
            var windows = new List<IntPtr>();
            EnumWindows(delegate(IntPtr hwnd, IntPtr ignored) {
                if (!IsWindowVisible(hwnd) || IsIconic(hwnd)) return true;
                var title = new StringBuilder(512);
                GetWindowText(hwnd, title, title.Capacity);
                if (title.ToString().IndexOf("Twitch", StringComparison.OrdinalIgnoreCase) >= 0)
                    windows.Add(hwnd);
                return true;
            }, IntPtr.Zero);
            FocusTarget match = null;
            foreach (var hwnd in windows)
            {
                if (!Before(deadline)) return null;
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                try
                {
                    using (var app = Process.GetProcessById((int)pid))
                    {
                        var name = app.ProcessName.ToLowerInvariant();
                        if (name != "chrome" && name != "helium" && name != "msedge") continue;
                    }
                    var root = AutomationElement.FromHandle(hwnd);
                    var documentType = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document);
                    var documents = root.FindAll(TreeScope.Descendants, documentType);
                    // A fresh Chromium window first creates its accessibility
                    // tree in response to this client. Give it time to arrive.
                    for (int retry = 0; documents.Count == 0 && retry < 3 && Before(deadline); retry++)
                    {
                        Thread.Sleep(50);
                        documents = root.FindAll(TreeScope.Descendants, documentType);
                    }
                    foreach (AutomationElement doc in documents)
                    {
                        Uri page;
                        if (!Before(deadline)) return null;
                        if (!Uri.TryCreate(DocumentAddress(doc), UriKind.Absolute, out page) ||
                            page.Scheme != "https" || !(page.Host == "twitch.tv" ||
                            page.Host.EndsWith(".twitch.tv", StringComparison.OrdinalIgnoreCase))) continue;
                        var fields = doc.FindAll(TreeScope.Descendants, new PropertyCondition(
                            AutomationElement.ControlTypeProperty, ControlType.Edit));
                        foreach (AutomationElement field in fields)
                        {
                            string label = field.Current.Name.Trim().TrimEnd('.', '…').ToLowerInvariant();
                            if (!field.Current.IsEnabled || field.Current.IsOffscreen || field.Current.IsPassword ||
                                !(field.Current.AutomationId == "chat-input" || label == "send a message" ||
                                label == "envoyer un message" || label == "envoyez un message" ||
                                label == "chat message input" || label == "message du chat")) continue;
                            // Descendant searches include embedded frames. Only
                            // the Twitch document itself may supply its editor.
                            var ancestor = TreeWalker.RawViewWalker.GetParent(field);
                            while (ancestor != null && ancestor.Current.ControlType != ControlType.Document)
                                ancestor = TreeWalker.RawViewWalker.GetParent(ancestor);
                            if (ancestor == null || !Automation.Compare(ancestor, doc)) continue;
                            // A second distinct editor is ambiguous until the user pins one.
                            if (match != null && !Automation.Compare(match.editor, field)) return null;
                            match = new FocusTarget(hwnd, pid, field, doc);
                        }
                    }
                }
                catch (ElementNotAvailableException) { continue; }
                catch (ArgumentException) { continue; } // Window closed during enumeration.
            }
            return match;
        }

        bool SameWindow()
        {
            uint pid;
            return GetWindowThreadProcessId(window, out pid) != 0 && pid == process;
        }

        static bool Before(int deadline) { return unchecked(deadline - Environment.TickCount) > 0; }

        internal bool TryFocus(IntPtr origin, int deadline)
        {
            if (!SameWindow() || !Before(deadline) || Foreground() != origin) return false;
            bool moved = false;
            try
            {
                if (editor != null && (!editor.Current.IsEnabled || !SamePage())) return false;
                if (!Before(deadline) || Foreground() != origin) return false;
                if (IsIconic(window)) ShowWindowAsync(window, 9);
                if (Foreground() != window)
                {
                    ActivateFrom(origin);
                    moved = true;
                    for (int i = 0; i < 12 && Before(deadline) && Foreground() != window; i++)
                        Thread.Sleep(10);
                }
                if (Foreground() != window || !Before(deadline)) return false;
                if (IsFocused())
                {
                    if (!Before(deadline)) return false;
                    moved = false;
                    return true;
                }
                if (editor == null || !SamePage() || editor.Current.IsOffscreen) return false;
                editor.SetFocus();
                for (int i = 0; i < 12 && Before(deadline); i++)
                {
                    if (IsFocused())
                    {
                        // Focusing a different Chromium control resets its caret to the start.
                        object pattern;
                        if (editor.TryGetCurrentPattern(TextPattern.Pattern, out pattern))
                        {
                            var range = ((TextPattern)pattern).DocumentRange;
                            range.MoveEndpointByRange(TextPatternRangeEndpoint.Start, range, TextPatternRangeEndpoint.End);
                            try { range.Select(); }
                            catch (InvalidOperationException) { /* The provider controls its caret. */ }
                        }
                        if (!Before(deadline)) return false;
                        moved = false;
                        return true;
                    }
                    Thread.Sleep(10);
                }
                return false;
            }
            finally
            {
                if (moved && Foreground() == window && origin != IntPtr.Zero) SetForegroundWindow(origin);
            }
        }

        bool SamePage() { return page == null || String.Equals(page, DocumentAddress(document), StringComparison.Ordinal); }

        void ActivateFrom(IntPtr origin)
        {
            if (SetForegroundWindow(window) || Foreground() != origin) return;
            // A user invoked the picker, but a game's foreground lock can
            // still reject the request. Windows unlocks activation on Alt.
            // Mask that Alt like Hex's own Windows hook so it does not open
            // a menu. Never release a modifier the user is holding.
            foreach (var key in new[] { 0x10, 0x11, 0x12, 0x5b, 0x5c })
                if ((GetAsyncKeyState(key) & 0x8000) != 0) return;
            if (Foreground() != origin) return;
            var keys = new[] { ActivationKey(0x12, false), ActivationKey(0xe8, false),
                ActivationKey(0xe8, true), ActivationKey(0x12, true) };
            var count = SendInput((uint)keys.Length, keys, Marshal.SizeOf(typeof(Input)));
            if (count != keys.Length)
            {
                if (count > 0) SendInput(2, new[] { keys[2], keys[3] }, Marshal.SizeOf(typeof(Input)));
                return;
            }
            if (Foreground() == origin) SetForegroundWindow(window);
        }

        internal bool IsFocused()
        {
            if (Foreground() != window || !SameWindow()) return false;
            if (editor == null) return true;
            var current = AutomationElement.FocusedElement;
            return current != null && Automation.Compare(editor, current) && SamePage() && Foreground() == window;
        }

        static Input ActivationKey(ushort key, bool up)
        {
            return new Input { type = 1, data = new InputData { keyboard = new Keyboard {
                key = key, flags = up ? 2u : 0u, extra = new UIntPtr(0x48455821u) } } };
        }

        [StructLayout(LayoutKind.Sequential)] struct Input { public uint type; public InputData data; }
        [StructLayout(LayoutKind.Explicit)] struct InputData
        {
            [FieldOffset(0)] public Keyboard keyboard;
            [FieldOffset(0)] public Mouse mouse;
        }
        [StructLayout(LayoutKind.Sequential)] struct Keyboard
        { public ushort key, scan; public uint flags, time; public UIntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] struct Mouse
        { public int x, y; public uint data, flags, time; public UIntPtr extra; }
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] static extern uint SendInput(uint count, Input[] keys, int size);
        [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")] internal static extern IntPtr Foreground();
        delegate bool EnumWindow(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindow callback, IntPtr parameter);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr window, StringBuilder title, int length);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr window, int command);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    }
}
