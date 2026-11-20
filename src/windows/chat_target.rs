//! The optional Windows chat picker knows the chosen UI Automation element.
//! Only the foreground HWND, a deadline, and an opaque binding ID cross the
//! window-message boundary. Hex owns the shortcut, capture, and transcript.

use std::ptr;
use std::sync::OnceLock;
use std::sync::atomic::{AtomicU32, Ordering};

use windows_sys::Win32::Foundation::HWND;
use windows_sys::Win32::System::SystemInformation::GetTickCount;
use windows_sys::Win32::UI::WindowsAndMessaging::{
    AllowSetForegroundWindow, FindWindowW, GetForegroundWindow, GetPropW, GetWindowThreadProcessId,
    PostMessageW, SMTO_ABORTIFHUNG, SMTO_BLOCK, SendMessageTimeoutW, SetPropW, WM_APP,
};

use super::paste::CaptureWindow;

#[derive(Clone, Copy, Default)]
pub struct Destination {
    pub return_to: Option<CaptureWindow>,
    pub chat: Option<Chat>,
}

#[derive(Clone, Copy)]
pub struct Chat {
    picker: isize,
    binding: usize,
}

impl Destination {
    pub fn begin(chat_gesture: bool) -> Self {
        let return_to = CaptureWindow::read();
        let chat = chat_gesture.then(Chat::activate).flatten();
        Self { return_to, chat }
    }
}

fn picker_path() -> Option<&'static std::path::Path> {
    static PATH: OnceLock<Option<std::path::PathBuf>> = OnceLock::new();
    PATH.get_or_init(|| Some(std::env::current_exe().ok()?.with_file_name("HexChat.exe")))
        .as_deref()
}

static PICKER_PID: AtomicU32 = AtomicU32::new(0);

pub(crate) fn installed() -> bool {
    picker_path().is_some_and(std::path::Path::is_file)
}

pub(super) fn start_picker() {
    let Some(path) = picker_path().filter(|path| path.is_file()) else {
        return;
    };
    match std::process::Command::new(path)
        .arg("--host")
        .arg(std::process::id().to_string())
        .spawn()
    {
        Ok(child) => PICKER_PID.store(child.id(), Ordering::Release),
        Err(error) => tracing::warn!(%error, "could not start Twitch targeting"),
    }
}

fn picker_window() -> Option<HWND> {
    let expected = PICKER_PID.load(Ordering::Acquire);
    if expected == 0 {
        return None;
    }
    unsafe {
        let hwnd = FindWindowW(ptr::null(), windows_sys::w!("HexChat.Picker.v1"));
        let mut pid = 0;
        GetWindowThreadProcessId(hwnd, &mut pid);
        (pid == expected).then_some(hwnd)
    }
}

#[derive(Clone, Copy, Debug, Default, Eq, PartialEq)]
pub(crate) enum Selection {
    #[default]
    Unavailable,
    Automatic,
    Selecting,
    Field,
    Window,
    Failed,
    HotkeyUnavailable,
    Unbound,
}

// Window properties carry small integers only. Reading them does not block
// the runtime or the settings window while UI Automation focuses a browser.
pub(super) fn sync_settings(settings: &crate::app_settings::AppSettings) -> Selection {
    let Some(hwnd) = picker_window() else {
        return Selection::Unavailable;
    };
    let flags = 1
        | ((settings.twitch_chat_enabled as usize) << 1)
        | ((settings.twitch_window_only as usize) << 2);
    unsafe {
        let options = windows_sys::w!("HexChat.Options");
        if GetPropW(hwnd, options) as usize != flags {
            SetPropW(hwnd, options, flags as _);
        }
        match GetPropW(hwnd, windows_sys::w!("HexChat.Selection")) as usize {
            1 => Selection::Automatic,
            2 => Selection::Selecting,
            3 => Selection::Field,
            4 => Selection::Window,
            5 => Selection::Failed,
            6 => Selection::HotkeyUnavailable,
            7 => Selection::Unbound,
            _ => Selection::Unavailable,
        }
    }
}

pub(crate) fn reset_selection() {
    if let Some(hwnd) = picker_window() {
        unsafe {
            PostMessageW(hwnd, WM_APP + 55, 0, 0);
        }
    }
}

impl Chat {
    fn activate() -> Option<Self> {
        let picker = picker_window()?;
        let (origin, expires) = unsafe {
            let mut pid = 0;
            GetWindowThreadProcessId(picker, &mut pid);
            AllowSetForegroundWindow(pid);
            (GetForegroundWindow(), GetTickCount().wrapping_add(900))
        };
        let binding = ask(picker, WM_APP + 51, origin as usize, expires as isize, 1050)?;
        (binding != 0).then_some(Self {
            picker: picker as isize,
            binding,
        })
    }

    pub fn is_focused(self) -> bool {
        ask(self.picker as HWND, WM_APP + 52, self.binding, 0, 400) == Some(1)
    }
}

fn ask(window: HWND, message: u32, first: usize, second: isize, timeout: u32) -> Option<usize> {
    let mut result = 0;
    // All parameters are values; neither process shares an address or a buffer.
    let answered = unsafe {
        SendMessageTimeoutW(
            window,
            message,
            first,
            second,
            SMTO_BLOCK | SMTO_ABORTIFHUNG,
            timeout,
            &mut result,
        )
    };
    (answered != 0).then_some(result)
}
