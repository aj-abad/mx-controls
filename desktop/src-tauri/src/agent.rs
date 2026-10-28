//! The connection to the MX Controls agent: newline-delimited JSON over a named pipe on Windows,
//! and over the Unix socket .NET makes for the same pipe name elsewhere.

use serde_json::{json, Value};
use std::collections::HashMap;
use std::path::PathBuf;
use std::process::{Command, Stdio};
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::Arc;
use std::time::Duration;
use tauri::{AppHandle, Emitter};
use tokio::io::{AsyncBufReadExt, AsyncRead, AsyncWrite, AsyncWriteExt, BufReader};
use tokio::sync::{mpsc, oneshot, Mutex};

const PIPE_NAME: &str = "mx-controls-agent";
const REQUEST_TIMEOUT: Duration = Duration::from_secs(10);
const RECONNECT_DELAY: Duration = Duration::from_millis(500);

type Reply = Result<Value, String>;

#[derive(Default)]
pub struct Agent {
    outbox: Mutex<Option<mpsc::UnboundedSender<String>>>,
    pending: Mutex<HashMap<u64, oneshot::Sender<Reply>>>,
    next_id: AtomicU64,
    connected: AtomicBool,
}

impl Agent {
    pub fn connected(&self) -> bool {
        self.connected.load(Ordering::SeqCst)
    }

    pub async fn call(&self, method: String, params: Value) -> Reply {
        let id = self.next_id.fetch_add(1, Ordering::SeqCst) + 1;
        let (reply, answer) = oneshot::channel();
        {
            let outbox = self.outbox.lock().await;
            let Some(outbox) = outbox.as_ref() else {
                return Err("The MX Controls agent isn't running.".into());
            };
            self.pending.lock().await.insert(id, reply);
            let line = json!({ "id": id, "method": method, "params": params }).to_string();
            if outbox.send(line).is_err() {
                self.pending.lock().await.remove(&id);
                return Err("The connection to the agent closed.".into());
            }
        }
        match tokio::time::timeout(REQUEST_TIMEOUT, answer).await {
            Ok(Ok(reply)) => reply,
            Ok(Err(_)) => Err("The connection to the agent closed.".into()),
            Err(_) => {
                self.pending.lock().await.remove(&id);
                Err("The agent didn't answer.".into())
            }
        }
    }
}

/// Keeps a connection to the agent open for the app's lifetime, starting the agent once if nothing answers.
pub async fn run(app: AppHandle, agent: Arc<Agent>) {
    let mut launched = false;
    loop {
        match connect().await {
            Ok(stream) => serve(&app, &agent, stream).await,
            Err(_) if !launched => {
                launched = true;
                launch_agent();
            }
            Err(_) => {}
        }
        tokio::time::sleep(RECONNECT_DELAY).await;
    }
}

async fn serve<S>(app: &AppHandle, agent: &Arc<Agent>, stream: S)
where
    S: AsyncRead + AsyncWrite + Send + 'static,
{
    let (read, mut write) = tokio::io::split(stream);
    let (outbox, mut lines_out) = mpsc::unbounded_channel::<String>();
    *agent.outbox.lock().await = Some(outbox);
    agent.connected.store(true, Ordering::SeqCst);
    let _ = app.emit("agent://connection", true);

    let writer = tokio::spawn(async move {
        while let Some(line) = lines_out.recv().await {
            let sent = async {
                write.write_all(line.as_bytes()).await?;
                write.write_all(b"\n").await?;
                write.flush().await
            };
            if sent.await.is_err() {
                break;
            }
        }
    });

    let mut lines = BufReader::new(read).lines();
    while let Ok(Some(line)) = lines.next_line().await {
        let Ok(message) = serde_json::from_str::<Value>(&line) else {
            continue;
        };
        if message.get("event").is_some() {
            let _ = app.emit("agent://event", message);
            continue;
        }
        let Some(id) = message.get("id").and_then(Value::as_u64) else {
            continue;
        };
        if let Some(reply) = agent.pending.lock().await.remove(&id) {
            let result = match message.get("error").and_then(Value::as_str) {
                Some(error) => Err(error.to_string()),
                None => Ok(message.get("result").cloned().unwrap_or(Value::Null)),
            };
            let _ = reply.send(result);
        }
    }

    *agent.outbox.lock().await = None;
    writer.abort();
    // Dropping the senders fails every call still waiting.
    agent.pending.lock().await.clear();
    agent.connected.store(false, Ordering::SeqCst);
    let _ = app.emit("agent://connection", false);
}

#[cfg(windows)]
async fn connect() -> std::io::Result<tokio::net::windows::named_pipe::NamedPipeClient> {
    tokio::net::windows::named_pipe::ClientOptions::new().open(format!(r"\\.\pipe\{PIPE_NAME}"))
}

#[cfg(unix)]
async fn connect() -> std::io::Result<tokio::net::UnixStream> {
    // .NET's NamedPipeServerStream listens on this socket in the temp folder.
    tokio::net::UnixStream::connect(std::env::temp_dir().join(format!("CoreFxPipe_{PIPE_NAME}"))).await
}

/// `MX_AGENT_PATH`, or the `mx-agent` sidecar, which Tauri places beside the app in a bundle and in development.
fn agent_path() -> Option<PathBuf> {
    if let Ok(path) = std::env::var("MX_AGENT_PATH") {
        return Some(path.into());
    }
    let exe = if cfg!(windows) { "mx-agent.exe" } else { "mx-agent" };
    let beside = std::env::current_exe().ok()?.parent()?.join(exe);
    beside.exists().then_some(beside)
}

/// Starts the agent detached, so it keeps the mappings working after the window closes.
fn launch_agent() {
    let Some(path) = agent_path() else {
        eprintln!("MX Controls: no mx-agent beside the app; run `pnpm agent` or set MX_AGENT_PATH");
        return;
    };
    let mut command = Command::new(&path);
    command.stdin(Stdio::null()).stdout(Stdio::null()).stderr(Stdio::null());
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        const CREATE_NO_WINDOW: u32 = 0x0800_0000;
        command.creation_flags(CREATE_NO_WINDOW);
    }
    if let Err(error) = command.spawn() {
        eprintln!("MX Controls: couldn't start {}: {error}", path.display());
    }
}
