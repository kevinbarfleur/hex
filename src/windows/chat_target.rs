//! The optional Windows chat picker knows the chosen UI Automation element.
//! Only the foreground HWND, a deadline, and an opaque binding ID cross the
//! window-message boundary. Hex owns the shortcut, capture, and transcript.

use std::ptr;
use std::sync::OnceLock;

use windows_sys::Win32::Foundation::HWND;
use windows_sys::Win32::System::SystemInformation::GetTickCount;
use windows_sys::Win32::UI::WindowsAndMessaging::{
    AllowSetForegroundWindow, FindWindowW, GetForegroundWindow, GetWindowThreadProcessId,
    SMTO_ABORTIFHUNG, SMTO_BLOCK, SendMessageTimeoutW, WM_APP,
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

impl Chat {
    fn activate() -> Option<Self> {
        // A hidden top-level window belonging to the companion beside this binary.
        let picker = unsafe { FindWindowW(ptr::null(), windows_sys::w!("HexChat.Picker.v1")) };
        if picker.is_null() {
            return None;
        }
        static PATH: OnceLock<Option<std::path::PathBuf>> = OnceLock::new();
        let expected = PATH
            .get_or_init(|| Some(std::env::current_exe().ok()?.with_file_name("HexChat.exe")))
            .as_ref()?;
        let actual = super::input::window_process_path(picker)?;
        if !actual
            .as_os_str()
            .eq_ignore_ascii_case(expected.as_os_str())
        {
            return None;
        }
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
