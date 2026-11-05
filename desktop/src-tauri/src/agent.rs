//! The connection to the MX Studio agent: newline-delimited JSON over a named pipe on Windows,
//! and over the Unix socket .NET makes for the same pipe name elsewhere.

use serde_json::{json, Value};
use std::collections::HashMap;
#[cfg(not(feature = "ui-tests"))]
use std::path::PathBuf;
#[cfg(not(feature = "ui-tests"))]
use std::process::{Command, Stdio};
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::Arc;
use std::time::Duration;
#[cfg(not(feature = "ui-tests"))]
use std::time::Instant;
use tauri::{AppHandle, Emitter};
use tokio::io::{AsyncBufReadExt, AsyncRead, AsyncWrite, AsyncWriteExt, BufReader};
use tokio::sync::{mpsc, oneshot, Mutex};

#[cfg(not(feature = "ui-tests"))]
fn pipe_name() -> String { "mx-controls-agent".into() }
#[cfg(feature = "ui-tests")]
fn pipe_name() -> String { crate::ui_test::pipe_name() }
const REQUEST_TIMEOUT: Duration = Duration::from_secs(10);
const RECONNECT_DELAY: Duration = Duration::from_millis(500);
/// Time for a starting agent to open its pipe before another launch.
#[cfg(not(feature = "ui-tests"))]
const RELAUNCH_DELAY: Duration = Duration::from_secs(5);

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
                return Err("The MX Studio agent isn't running.".into());
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

/// Keeps a connection to the agent open for the app's lifetime, starting the agent whenever nothing answers,
/// including after it crashed or was stopped.
pub async fn run(app: AppHandle, agent: Arc<Agent>) {
    #[cfg(feature = "embedded-agent")]
    if let Err(error) = tokio::task::spawn_blocking(crate::embedded_agent::unpack)
        .await
        .map_err(std::io::Error::other)
        .and_then(|unpacked| unpacked)
    {
        eprintln!("MX Studio: couldn't unpack the agent: {error}");
    }
    #[cfg(not(feature = "ui-tests"))]
    let mut launched: Option<Instant> = None;
    loop {
        match connect().await {
            Ok(stream) => serve(&app, &agent, stream).await,
            #[cfg(not(feature = "ui-tests"))]
            Err(_) if launched.is_none_or(|at| at.elapsed() >= RELAUNCH_DELAY) => {
                launched = Some(Instant::now());
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
    tokio::net::windows::named_pipe::ClientOptions::new().open(format!(r"\\.\pipe\{}", pipe_name()))
}

#[cfg(unix)]
async fn connect() -> std::io::Result<tokio::net::UnixStream> {
    // .NET's NamedPipeServerStream listens on this socket in the temp folder.
    tokio::net::UnixStream::connect(std::env::temp_dir().join(format!("CoreFxPipe_{}", pipe_name()))).await
}

/// `MX_AGENT_PATH`, the agent a portable build unpacked, or the `mx-agent` sidecar, which Tauri places beside the
/// app in a bundle and in development.
#[cfg(not(feature = "ui-tests"))]
fn agent_path() -> Option<PathBuf> {
    if let Ok(path) = std::env::var("MX_AGENT_PATH") {
        return Some(path.into());
    }
    #[cfg(feature = "embedded-agent")]
    {
        let unpacked = crate::embedded_agent::path();
        if unpacked.exists() {
            return Some(unpacked);
        }
    }
    let exe = if cfg!(windows) { "mx-agent.exe" } else { "mx-agent" };
    let beside = std::env::current_exe().ok()?.parent()?.join(exe);
    beside.exists().then_some(beside)
}

/// Starts the agent detached, so it keeps the mappings working after the window closes.
#[cfg(not(feature = "ui-tests"))]
fn launch_agent() {
    let Some(path) = agent_path() else {
        eprintln!("MX Studio: no mx-agent beside the app; run `pnpm agent` or set MX_AGENT_PATH");
        return;
    };
    let mut command = Command::new(&path);
    command.stdin(Stdio::null()).stdout(Stdio::null()).stderr(Stdio::null());
    hide_window(&mut command);
    match command.spawn() {
        // Waited for off to the side: on a Unix system a child that exits stays a zombie until its parent does this.
        Ok(mut child) => {
            std::thread::spawn(move || child.wait());
        }
        Err(error) => eprintln!("MX Studio: couldn't start {}: {error}", path.display()),
    }
}

/// Keeps the agent, a console program, from opening a console window.
#[cfg(not(feature = "ui-tests"))]
pub fn hide_window(command: &mut Command) {
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        const CREATE_NO_WINDOW: u32 = 0x0800_0000;
        command.creation_flags(CREATE_NO_WINDOW);
    }
    #[cfg(not(windows))]
    let _ = command;
}
