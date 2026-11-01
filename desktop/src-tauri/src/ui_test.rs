//! Compiled only into the hidden UI-test binary. No device agent is linked or launched.
use serde_json::{json, Value};

pub fn pipe_name() -> String {
    let run = std::env::var("MX_CONTROLS_TEST_RUN").expect("UI tests require a run token");
    assert!(run.len() == 36 && run.bytes().all(|b| b.is_ascii_hexdigit() || b == b'-'), "Invalid UI-test run token");
    let pipe = std::env::var("MX_CONTROLS_TEST_PIPE").expect("UI tests require an isolated pipe");
    assert_eq!(pipe, format!("mx-controls-ui-test-{run}"), "Refusing a non-test agent pipe");
    pipe
}

pub fn validate(config: &tauri::Config) {
    pipe_name();
    assert_eq!(config.identifier, "app.mxcontrols.desktop.tests");
    assert_eq!(config.app.windows.len(), 1);
    let window = &config.app.windows[0];
    assert!(!window.visible && !window.focus && !window.focusable && window.skip_taskbar,
        "UI tests require a hidden, non-focusable window excluded from the taskbar");
    assert!(std::env::var_os("WEBVIEW2_USER_DATA_FOLDER").is_some(), "UI tests require a private WebView profile");
}

#[tauri::command]
pub fn ui_test_environment(window: tauri::WebviewWindow) -> Result<Value, String> {
    let size = window.inner_size().map_err(|e| e.to_string())?;
    Ok(json!({
        "run": std::env::var("MX_CONTROLS_TEST_RUN").unwrap(),
        "pipe": pipe_name(),
        "hardwareAgentEnabled": false,
        "visible": window.is_visible().map_err(|e| e.to_string())?,
        "focused": window.is_focused().map_err(|e| e.to_string())?,
        "width": size.width,
        "height": size.height,
    }))
}
