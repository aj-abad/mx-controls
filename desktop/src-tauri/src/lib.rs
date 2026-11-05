mod agent;
#[cfg(all(feature = "ui-tests", any(not(debug_assertions), feature = "embedded-agent", not(windows))))]
compile_error!("ui-tests requires a Windows debug build without embedded-agent");
#[cfg(feature = "ui-tests")]
mod ui_test;
#[cfg(feature = "embedded-agent")]
mod embedded_agent;

use agent::Agent;
use serde_json::Value;
use std::sync::Arc;

#[tauri::command]
async fn agent_call(
    agent: tauri::State<'_, Arc<Agent>>,
    method: String,
    params: Option<Value>,
) -> Result<Value, String> {
    agent.call(method, params.unwrap_or_else(|| Value::Object(Default::default()))).await
}

#[tauri::command]
fn agent_connected(agent: tauri::State<'_, Arc<Agent>>) -> bool {
    agent.connected()
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    // A runner pointed at the wrong binary must fail before connecting to a real agent.
    #[cfg(not(feature = "ui-tests"))]
    assert!(std::env::var_os("MX_CONTROLS_TEST_RUN").is_none(), "This binary was not built with ui-tests");
    let context = tauri::generate_context!();
    // Validate before Tauri creates any windows or starts the pipe bridge.
    #[cfg(feature = "ui-tests")]
    ui_test::validate(context.config());
    let agent = Arc::new(Agent::default());
    let builder = tauri::Builder::default();
    // Must be the first plugin, so a second launch hands over before it starts anything of its own.
    // UI test runs stay separate, since each drives its own hidden window.
    #[cfg(not(feature = "ui-tests"))]
    let builder = builder.plugin(tauri_plugin_single_instance::init(|app, _, _| {
        use tauri::Manager;
        if let Some(window) = app.get_webview_window("main") {
            let _ = window.unminimize();
            let _ = window.show();
            let _ = window.set_focus();
        }
    }));
    let builder = builder
        .manage(agent.clone())
        .setup(move |app| {
            tauri::async_runtime::spawn(agent::run(app.handle().clone(), agent));
            Ok(())
        });
    #[cfg(not(feature = "ui-tests"))]
    let builder = builder.invoke_handler(tauri::generate_handler![agent_call, agent_connected]);
    #[cfg(feature = "ui-tests")]
    let builder = builder.invoke_handler(tauri::generate_handler![agent_call, agent_connected, ui_test::ui_test_environment]);
    builder.run(context)
        .expect("error while running MX Studio");
}
