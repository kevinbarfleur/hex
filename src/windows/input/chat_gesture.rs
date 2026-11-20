//! Two short, unchorded presses of the same Control key toggle chat dictation.

use windows_sys::Win32::UI::Input::KeyboardAndMouse::{VK_CONTROL, VK_LCONTROL, VK_RCONTROL};

#[derive(Default)]
pub(super) struct DoubleControl {
    down: Option<(u32, u32)>,
    first: Option<(u32, u32)>,
}

impl DoubleControl {
    pub(super) fn event(&mut self, key: u32, down: bool, time: u32, other_modifier: bool) -> bool {
        const MAX_MS: u32 = 350;
        if other_modifier || !matches!(key as u16, VK_CONTROL | VK_LCONTROL | VK_RCONTROL) {
            *self = Self::default();
            return false;
        }
        if down {
            self.down.get_or_insert((key, time));
            return false;
        }
        let press = self.down.take();
        if !press.is_some_and(|(vk, at)| vk == key && time.wrapping_sub(at) <= MAX_MS) {
            self.first = None;
            return false;
        }
        if self
            .first
            .take()
            .is_some_and(|(vk, at)| vk == key && time.wrapping_sub(at) <= MAX_MS)
        {
            return true;
        }
        self.first = Some((key, time));
        false
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    const CTRL: u32 = VK_LCONTROL as u32;

    #[test]
    fn fires_only_on_second_release_and_ignores_repeats() {
        let mut gesture = DoubleControl::default();
        for (down, time) in [(true, 10), (true, 30), (false, 50), (true, 110)] {
            assert!(!gesture.event(CTRL, down, time, false));
        }
        assert!(gesture.event(CTRL, false, 140, false));
        assert!(!gesture.event(CTRL, false, 145, false));
    }

    #[test]
    fn ordinary_dictation_and_other_control_chords_never_count_as_taps() {
        for key in [0x20, 0x43, 0x56, 0x01] {
            let mut gesture = DoubleControl::default();
            for (vk, down, time) in [
                (CTRL, true, 10),
                (key, true, 30),
                (key, false, 40),
                (CTRL, false, 50),
                (CTRL, true, 100),
                (CTRL, false, 120),
            ] {
                assert!(!gesture.event(vk, down, time, false));
            }
        }
    }

    #[test]
    fn rejects_slow_presses_and_other_modifiers() {
        let mut gesture = DoubleControl::default();
        for (down, at, other) in [
            (true, 0, false),
            (false, 500, false),
            (true, 520, false),
            (false, 530, false),
            (true, 600, true),
            (false, 625, false),
            (true, 1200, false),
            (false, 1210, false),
            (true, 1700, false),
            (false, 1710, false),
        ] {
            assert!(!gesture.event(CTRL, down, at, other));
        }
    }
}
