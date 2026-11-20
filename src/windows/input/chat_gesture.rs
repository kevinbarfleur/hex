//! A pair of bare Ctrl taps starts a chat capture. Its next bare tap finishes it.

use windows_sys::Win32::UI::Input::KeyboardAndMouse::{VK_CONTROL, VK_LCONTROL, VK_RCONTROL};

#[derive(Default)]
pub(super) struct ControlTaps {
    down: Option<(u32, u32)>,
    previous: Option<(u32, u32)>,
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub struct ControlTap {
    consecutive: bool,
}

impl ControlTap {
    // The listener consumes taps in order, even while the chat lookup is busy.
    // A tap that finishes one capture cannot start the next pair.
    pub(crate) fn triggers(self, armed: &mut bool, recording: bool, chat_owned: bool) -> bool {
        let starts = !recording && *armed && self.consecutive;
        *armed = !recording && !starts;
        starts || (recording && chat_owned)
    }
}

impl ControlTaps {
    pub(super) fn event(
        &mut self,
        key: u32,
        down: bool,
        time: u32,
        other_modifier: bool,
    ) -> Option<ControlTap> {
        const MAX_MS: u32 = 350;
        if other_modifier || !matches!(key as u16, VK_CONTROL | VK_LCONTROL | VK_RCONTROL) {
            *self = Self::default();
            return None;
        }
        if down {
            self.down.get_or_insert((key, time));
            return None;
        }
        if !self
            .down
            .take()
            .is_some_and(|(vk, at)| vk == key && time.wrapping_sub(at) <= MAX_MS)
        {
            self.previous = None;
            return None;
        }
        let consecutive = self
            .previous
            .replace((key, time))
            .is_some_and(|(vk, at)| vk == key && time.wrapping_sub(at) <= MAX_MS);
        Some(ControlTap { consecutive })
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    const CTRL: u32 = VK_LCONTROL as u32;

    fn tap(keys: &mut ControlTaps, key: u32, at: u32) -> ControlTap {
        assert!(keys.event(key, true, at, false).is_none());
        keys.event(key, false, at + 25, false).unwrap()
    }

    #[test]
    fn double_to_start_single_to_finish_and_a_fresh_pair_to_restart() {
        let mut keys = ControlTaps::default();
        let mut armed = false;
        assert!(!tap(&mut keys, CTRL, 0).triggers(&mut armed, false, false));
        assert!(tap(&mut keys, CTRL, 100).triggers(&mut armed, false, false));
        assert!(tap(&mut keys, CTRL, 180).triggers(&mut armed, true, true));
        assert!(!tap(&mut keys, CTRL, 240).triggers(&mut armed, false, true));
        assert!(tap(&mut keys, CTRL, 320).triggers(&mut armed, false, false));
        assert!(tap(&mut keys, VK_RCONTROL.into(), 5000).triggers(&mut armed, true, true));
    }

    #[test]
    fn other_captures_ignore_ctrl_and_slow_or_mixed_pairs_stay_idle() {
        let mut keys = ControlTaps::default();
        let mut armed = false;
        for at in [0, 90] {
            assert!(!tap(&mut keys, CTRL, at).triggers(&mut armed, true, false));
        }
        for (key, at) in [(CTRL, 150), (CTRL, 900), (VK_RCONTROL.into(), 970)] {
            assert!(!tap(&mut keys, key, at).triggers(&mut armed, false, false));
        }
    }

    #[test]
    fn chords_repeats_long_holds_and_unmatched_releases_do_not_finish() {
        for chord in [0x20, 0x43, 0x56, 0x01] {
            let mut keys = ControlTaps::default();
            for (key, down, at, modified) in [
                (CTRL, true, 0, false),
                (chord, true, 30, false),
                (chord, false, 40, false),
                (CTRL, false, 50, false),
                (CTRL, true, 100, true),
                (CTRL, false, 130, false),
                (CTRL, true, 200, false),
                (CTRL, true, 370, false),
                (CTRL, false, 750, false),
            ] {
                assert!(keys.event(key, down, at, modified).is_none());
            }
            assert!(!tap(&mut keys, CTRL, 800).consecutive);
            assert!(keys.event(CTRL, false, 845, false).is_none());
        }
    }
}
