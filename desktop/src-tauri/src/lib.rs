mod agent;

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
    let agent = Arc::new(Agent::default());
    tauri::Builder::default()
        .manage(agent.clone())
        .setup(move |app| {
            tauri::async_runtime::spawn(agent::run(app.handle().clone(), agent));
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![agent_call, agent_connected])
        .run(tauri::generate_context!())
        .expect("error while running MX Controls");
}
