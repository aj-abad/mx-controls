//! The portable build carries the agent inside the app, so the app is one file to copy anywhere. It unpacks the
//! agent to the user's local app data, where the agent's sign-in task then points, and replaces an older agent
//! there when the app is updated.

use std::path::PathBuf;
use std::process::Command;
use std::time::Duration;

const AGENT: &[u8] = include_bytes!(env!("MX_EMBEDDED_AGENT"));

/// `%LOCALAPPDATA%\MxControls\mx-agent.exe`. Not the installer's `MX Studio` folder, so the two don't mix.
pub fn path() -> PathBuf {
    let base = std::env::var_os("LOCALAPPDATA").map(PathBuf::from).unwrap_or_else(std::env::temp_dir);
    base.join("MxControls").join(if cfg!(windows) { "mx-agent.exe" } else { "mx-agent" })
}

/// Puts this build's agent in place unless it's there already. Blocks, and runs before the app first connects, so
/// the app never talks to the agent it's replacing.
pub fn unpack() -> std::io::Result<()> {
    let path = path();
    if std::fs::read(&path).is_ok_and(|current| current == AGENT) {
        return Ok(());
    }
    if path.exists() {
        // The old agent's file stays locked while it runs. `--stop` hands the buttons back and waits for it to exit.
        let mut stop = Command::new(&path);
        stop.arg("--stop");
        crate::agent::hide_window(&mut stop);
        let _ = stop.status();
    }
    std::fs::create_dir_all(path.parent().unwrap())?;
    let staged = path.with_extension("new");
    std::fs::write(&staged, AGENT)?;
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        std::fs::set_permissions(&staged, std::fs::Permissions::from_mode(0o755))?;
    }
    for attempt in 1.. {
        // Antivirus scanning can hold the old file for a moment after the agent exits.
        match std::fs::rename(&staged, &path) {
            Ok(()) => return Ok(()),
            Err(_) if attempt < 10 => std::thread::sleep(Duration::from_millis(300)),
            Err(error) => {
                let _ = std::fs::remove_file(&staged);
                return Err(error);
            }
        }
    }
    unreachable!()
}
