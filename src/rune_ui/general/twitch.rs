//! Twitch targeting in the Windows General settings.
use super::*;
use crate::windows::chat_target::{self, Selection};

pub(super) fn section(ui: &Ui, p: Paint) -> El {
    let enabled = ui.settings.twitch_chat_enabled;
    let mut rows = vec![icon_toggle_row(
        p,
        "twitch-enabled",
        &KEYBOARD,
        tr("Target Twitch chat"),
        tr(
            "Tap Ctrl twice to dictate, then once to finish. If chat is unavailable, Hex dictates in the active app.",
        ),
        enabled,
        |ui| {
            ui.update(|s| s.twitch_chat_enabled = !s.twitch_chat_enabled);
        },
    )];
    if enabled {
        let state = ui.platform.twitch_selection;
        let (title, description) = match state {
            Selection::Unavailable => (
                "Chat targeting unavailable",
                "Reopen Hex to reconnect the chat picker.",
            ),
            Selection::Selecting => (
                "Selecting chat…",
                "Keep the chat field focused for a moment.",
            ),
            Selection::Field => (
                "Chat field selected",
                "Hex will use this field for the rest of the session.",
            ),
            Selection::Window => (
                "Chat window selected",
                "Hex will focus this window for the rest of the session.",
            ),
            Selection::Failed | Selection::Unbound => (
                "No chat selected",
                "Focus the chat field and press Ctrl + Alt + F8.",
            ),
            Selection::HotkeyUnavailable => (
                "Automatic detection",
                "Ctrl + Alt + F8 is in use by another app.",
            ),
            Selection::Automatic => (
                "Automatic detection",
                "To select a specific chat, focus its field and press Ctrl + Alt + F8.",
            ),
        };
        let action = if matches!(
            state,
            Selection::Field | Selection::Window | Selection::Failed | Selection::Unbound
        ) {
            button(
                p,
                "twitch-reset",
                tr("Use automatic detection"),
                ButtonKind::Secondary,
                |_, _| chat_target::reset_selection(),
            )
        } else {
            row()
        };
        rows.push(icon_row(
            p,
            "twitch-target",
            &GLOBE,
            tr(title),
            Some(tr(description)),
            action,
        ));
        rows.push(icon_toggle_row(
            p,
            "twitch-window-only",
            &PANEL,
            tr("Select the window only"),
            tr("Applies the next time you press Ctrl + Alt + F8."),
            ui.settings.twitch_window_only,
            |ui| {
                ui.update(|s| s.twitch_window_only = !s.twitch_window_only);
            },
        ));
    }
    controls::section(p, "twitch", "Twitch", rows)
}
