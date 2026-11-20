"""Windows integration check. Requires Python with Tk, a Chromium browser,
a prepared disposable Hex profile with a model, and a WAV of spoken words.
Close Hex before running. The test briefly takes foreground keyboard focus.
"""
import argparse
from concurrent.futures import ThreadPoolExecutor
import ctypes
from ctypes import wintypes
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import subprocess
import tempfile
import threading
import time
import tkinter as tk
import uuid


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for option in ("app", "browser", "profile", "wav"):
        parser.add_argument("--" + option, required=True, type=Path)
    args = parser.parse_args()
    title = "HexChatFixture-" + uuid.uuid4().hex
    state = {}
    page = ("""<!doctype html><meta charset=utf-8><title>TITLE</title>
        <div id=d contenteditable=true role=textbox aria-label="Message">Draft: </div>
        <input id=q aria-label="Search">
        <script>
        let enters=0;
        document.onkeydown=e=>{if(e.key==='Enter') enters++};
        d.focus();
        async function tick(){
          let r=await fetch('/state',{method:'POST',body:JSON.stringify({
            draft:d.textContent, other:q.value, focus:document.activeElement.id, enters
          })});
          if(await r.text()==='other') q.focus();
          setTimeout(tick,100);
        }tick();
        </script>""").replace("TITLE", title).encode()
    commands = []

    class Fixture(BaseHTTPRequestHandler):
        def log_message(self, *args):
            pass

        def reply(self, body, mime):
            self.send_response(200)
            self.send_header("Content-Type", mime)
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def do_GET(self):
            self.reply(page, "text/html; charset=utf-8")

        def do_POST(self):
            state.update(json.loads(self.rfile.read(int(self.headers["Content-Length"]))))
            self.reply(commands.pop(0) if commands else b"", "text/plain")

    user = ctypes.windll.user32
    user.GetForegroundWindow.restype = wintypes.HWND
    user.FindWindowW.argtypes = (wintypes.LPCWSTR, wintypes.LPCWSTR)
    user.FindWindowW.restype = wintypes.HWND
    user.SetForegroundWindow.argtypes = (wintypes.HWND,)
    user.SendMessageW.argtypes = (wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM)
    user.SendMessageW.restype = wintypes.LPARAM
    enum_callback = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    user.GetAncestor.argtypes = (wintypes.HWND, wintypes.UINT)
    user.GetAncestor.restype = wintypes.HWND

    def foreground():
        return user.GetAncestor(user.GetForegroundWindow(), 2)

    game = tk.Tk()
    game.title(title + "-Source")
    field = tk.Entry(game)
    field.pack()
    game.update()
    source = user.FindWindowW(None, title + "-Source")

    def wait(check, why, seconds=8):
        end = time.monotonic() + seconds
        while time.monotonic() < end:
            game.update()
            value = check()
            if value:
                return value
            time.sleep(.04)
        raise AssertionError(why)

    def browser_window():
        matches = []

        def enum(hwnd, unused):
            caption = ctypes.create_unicode_buffer(512)
            user.GetWindowTextW(hwnd, caption, len(caption))
            if title in caption.value and hwnd != source and user.IsWindowVisible(hwnd):
                matches.append(hwnd)
            return True

        user.EnumWindows(enum_callback(enum), 0)
        return next(iter(matches), None)

    def activate(hwnd):
        if foreground() != hwnd:
            # Give the test driver foreground permission before moving focus
            # between its own two windows.
            user.keybd_event(0x12, 0, 0, 0)
            user.keybd_event(0x12, 0, 2, 0)
            user.SetForegroundWindow(hwnd)
        if foreground() != hwnd:
            # The shell can own foreground permission during browser first-run.
            # Use only this test window's hit-tested caption to activate it.
            rect = wintypes.RECT()
            user.GetWindowRect(hwnd, ctypes.byref(rect))
            user.WindowFromPoint.argtypes = (wintypes.POINT,)
            user.WindowFromPoint.restype = wintypes.HWND
            previous = wintypes.POINT()
            user.GetCursorPos(ctypes.byref(previous))
            point = wintypes.POINT((rect.left + rect.right) // 2, rect.top + 12)
            if user.GetAncestor(user.WindowFromPoint(point), 2) == hwnd:
                user.SetCursorPos(point.x, point.y)
                user.mouse_event(2, 0, 0, 0, 0)
                user.mouse_event(4, 0, 0, 0, 0)
                time.sleep(.10)
                user.SetCursorPos(previous.x, previous.y)
        wait(lambda: foreground() == hwnd, "The test window could not be activated")

    def keys(sequence, down):
        for key in sequence:
            user.keybd_event(key, 0, 0 if down else 2, 0)

    def focus_query():
        picker = user.FindWindowW(None, "HexChat.Picker.v1")
        assert picker, "The picker must be running"
        pid = wintypes.DWORD()
        user.GetWindowThreadProcessId(picker, ctypes.byref(pid))
        user.AllowSetForegroundWindow(pid)
        expiry = (ctypes.windll.kernel32.GetTickCount() + 900) & 0xffffffff
        with ThreadPoolExecutor(max_workers=1) as worker:
            result = worker.submit(user.SendMessageW, picker, 0x8000 + 51, source, expiry)
            wait(result.done, "Picker timed out", 2)
            return result.result()

    def dictate(expected):
        assert foreground() == source
        keys((0x11, 0x20), True)
        try:
            wait(lambda: foreground() == expected, "Wrong recording destination", 2)
            end = time.monotonic() + 3
            while time.monotonic() < end:
                game.update()
                time.sleep(.03)
        finally:
            keys((0x20, 0x11), False)

    def double_control():
        for unused in range(2):
            keys((0xa2,), True)
            time.sleep(.055)
            keys((0xa2,), False)
            time.sleep(.075)

    def dictate_to_chat(expected):
        assert foreground() == source
        double_control()
        wait(lambda: foreground() == expected, "Wrong double-Control destination", 2)
        end = time.monotonic() + 3
        while time.monotonic() < end:
            game.update()
            time.sleep(.03)
        double_control()

    env = dict(os.environ, HEX_APPLICATION_SUPPORT_DIR=str(args.profile.resolve()),
               HEX_AUDIO_FIXTURE=str(args.wav.resolve()))
    assert args.wav.is_file() and (args.profile / "models").is_dir()
    settings = json.loads((args.profile / "settings.json").read_text(encoding="utf8"))
    assert settings.get("shortcut_mode", "hold") == "hold", "Use Hold mode in the test profile"
    assert not user.FindWindowW(None, "HexChat.Picker.v1"), "Close the running picker first"
    server = ThreadingHTTPServer(("127.0.0.1", 0), Fixture)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    engine = subprocess.Popen([args.app.with_name("hex.exe"), "app", "--hidden"], env=env)
    helper = browser = chat = None
    try:
        wait(lambda: (args.profile / "local-api.json").exists(), "Hex API unavailable", 30)
        helper = subprocess.Popen([args.app, "--pin-only"], env=env)
        wait(lambda: user.FindWindowW(None, "HexChat.Picker.v1"), "Picker unavailable")
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as browser_profile:
            browser = subprocess.Popen([args.browser, "--user-data-dir=" + browser_profile,
                                       "--no-first-run", "--no-default-browser-check", "--force-renderer-accessibility",
                                       "--disable-background-timer-throttling", "--disable-backgrounding-occluded-windows",
                                       "--disable-features=msEdgeFirstRunExperience",
                                       "--app=http://127.0.0.1:" + str(server.server_port)])
            chat = wait(browser_window, "Test browser unavailable", 20)
            activate(chat)
            wait(lambda: state.get("focus") == "d", "Test editor unavailable")
            keys((0x11, 0x12, 0x77), True)
            keys((0x77, 0x12, 0x11), False)
            time.sleep(1)
            commands.append(b"other")
            wait(lambda: state["focus"] == "q", "Could not focus second field")
            activate(source)
            # Pin and verify our own fixture before injecting any speech.
            assert focus_query() and foreground() == chat
            wait(lambda: state["focus"] == "d", "Wrong editor bound")
            commands.append(b"other")
            wait(lambda: state["focus"] == "q", "Could not move the caret")
            activate(source)
            field.focus_set()
            dictate(source)
            wait(lambda: field.get().strip(), "Ctrl+Space failed while the chat was available", 40)
            assert state["draft"] == "Draft: " and foreground() == source
            field.delete(0, tk.END)
            time.sleep(.8)
            dictate_to_chat(chat)
            wait(lambda: len(state["draft"]) > len("Draft: "), "No chat transcript", 40)
            assert state["draft"].startswith("Draft:") and state["focus"] == "d"
            assert state["enters"] == 0 and not state["other"] and not field.get()
            assert foreground() == chat
            user.PostMessageW(chat, 0x10, 0, 0)
            wait(lambda: not user.IsWindow(chat), "Test chat did not close")
            chat = None
            activate(source)
            field.focus_force()
            assert focus_query() == 0 and foreground() == source
            dictate_to_chat(source)
            wait(lambda: field.get().strip(), "No double-Control fallback after closing chat", 40)
            assert foreground() == source
            time.sleep(.8)  # Let Hex restore the previous clipboard before shutdown.
            print("Passed: ordinary Ctrl+Space, double-Control chat draft, retained focus, no Enter, double-Control fallback.")
    finally:
        if chat:
            user.PostMessageW(chat, 0x10, 0, 0)
        if browser and browser.poll() is None:
            browser.terminate()
        if helper:
            helper.terminate()
        if engine.poll() is None:
            subprocess.run([args.app.with_name("hex.exe"), "quit"], env=env, timeout=10, capture_output=True)
            try:
                engine.wait(3)
            except subprocess.TimeoutExpired:
                engine.terminate()
        game.destroy()
        server.shutdown()


if __name__ == "__main__":
    main()
