// Publishes the agent with NativeAOT to src-tauri/binaries, where Tauri bundles it as the `ezpz-agent` sidecar.
// Skips the publish when the binary is newer than every agent source file; `--force` publishes anyway.
// On macOS it also wraps the agent in `ezPz Agent.app`, which Tauri copies to Contents/Helpers; see
// buildMacHelper.
import { execFileSync } from "node:child_process";
import {
  closeSync, copyFileSync, existsSync, mkdirSync, openSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync,
} from "node:fs";
import { homedir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const RUNTIMES = {
  "x86_64-pc-windows-msvc": "win-x64",
  "aarch64-pc-windows-msvc": "win-arm64",
  "x86_64-apple-darwin": "osx-x64",
  "aarch64-apple-darwin": "osx-arm64",
  "x86_64-unknown-linux-gnu": "linux-x64",
  "aarch64-unknown-linux-gnu": "linux-arm64",
};

const desktop = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const agentRoot = resolve(desktop, "../agent");

const triple = process.env.TAURI_ENV_TARGET_TRIPLE || hostTriple();
const runtime = RUNTIMES[triple];
if (!runtime) throw new Error(`No .NET runtime identifier for ${triple}`);
const extension = triple.includes("windows") ? ".exe" : "";
const target = join(desktop, "src-tauri", "binaries", `ezpz-agent-${triple}${extension}`);

// Tauri copies the sidecar beside the app on every build, which fails on Windows while an agent runs from that
// copy, so that agent is stopped; the app starts it again when it opens. An agent running from anywhere else, such
// as the one deploy.cmd unpacks, keeps the buttons working through the build.
stopAgentRunningFromBuild();

if (!process.argv.includes("--force") && existsSync(target) && newestSource(agentRoot) < statSync(target).mtimeMs) {
  console.log(`ezpz-agent is up to date: ${target}`);
} else {
  publish();
}
if (triple.includes("apple-darwin")) buildMacHelper();

function publish() {
  const output = join(desktop, "src-tauri", "target", "agent", runtime);
  execFileSync(
    "dotnet",
    ["publish", join(agentRoot, "EzPz.Agent"), "-c", "Release", "-r", runtime, "-o", output, "-p:DebugType=none"],
    { stdio: "inherit", env: publishEnvironment() },
  );
  mkdirSync(dirname(target), { recursive: true });
  copyFileSync(join(output, `ezpz-agent${extension}`), target);
  console.log(`Published ezpz-agent for ${triple}: ${target}`);
}

// The agent runs all the time. Left in the app's Contents/MacOS, macOS takes it for ezPz itself and keeps a Dock
// icon up for it once it uses AppKit. Its own bundle, marked LSUIElement, gives it no Dock icon and its own identity.
// Signed ad hoc so the app around it can be signed; install-app signs it again with a real identity.
function buildMacHelper() {
  const config = JSON.parse(readFileSync(join(desktop, "src-tauri", "tauri.conf.json"), "utf8"));
  const app = join(desktop, "src-tauri", "target", "agent-app", "ezPz Agent.app");
  rmSync(app, { recursive: true, force: true });
  mkdirSync(join(app, "Contents", "MacOS"), { recursive: true });
  mkdirSync(join(app, "Contents", "Resources"), { recursive: true });
  copyFileSync(target, join(app, "Contents", "MacOS", "ezpz-agent"));
  copyFileSync(join(desktop, "src-tauri", "icons", "icon.icns"), join(app, "Contents", "Resources", "icon.icns"));
  writeFileSync(join(app, "Contents", "Info.plist"), `<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleIdentifier</key>
  <string>${config.identifier.replace(/\.desktop$/, "")}.agent</string>
  <key>CFBundleName</key>
  <string>ezPz Agent</string>
  <key>CFBundleExecutable</key>
  <string>ezpz-agent</string>
  <key>CFBundleIconFile</key>
  <string>icon</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleInfoDictionaryVersion</key>
  <string>6.0</string>
  <key>CFBundleShortVersionString</key>
  <string>${config.version}</string>
  <key>CFBundleVersion</key>
  <string>${config.version}</string>
  <key>LSMinimumSystemVersion</key>
  <string>11.0</string>
  <key>LSUIElement</key>
  <true/>
</dict>
</plist>
`);
  execFileSync("codesign", ["--force", "--sign", "-", app], { stdio: "inherit" });
  console.log(`Wrapped ezpz-agent in ${app}`);
}

function stopAgentRunningFromBuild() {
  if (process.platform !== "win32" || !existsSync(target)) return;
  const copies = ["debug", "release"].map((profile) => join(desktop, "src-tauri", "target", profile, "ezpz-agent.exe"));
  if (!copies.some(isRunning)) return;
  try {
    execFileSync(target, ["--stop"], { stdio: "ignore", timeout: 10_000 });
  } catch {
    console.warn("Couldn't stop the running agent; the build may fail to replace it.");
  }
}

// Windows won't open a running program's file for writing.
function isRunning(executable) {
  if (!existsSync(executable)) return false;
  try {
    closeSync(openSync(executable, "r+"));
    return false;
  } catch (error) {
    return error.code === "EBUSY" || error.code === "EPERM";
  }
}

function hostTriple() {
  const info = execFileSync("rustc", ["-vV"], { encoding: "utf8" });
  return /^host: (.+)$/m.exec(info)[1].trim();
}

function newestSource(folder) {
  let newest = 0;
  for (const entry of readdirSync(folder, { withFileTypes: true })) {
    if (entry.name === "bin" || entry.name === "obj") continue;
    const path = join(folder, entry.name);
    newest = Math.max(newest, entry.isDirectory() ? newestSource(path) : statSync(path).mtimeMs);
  }
  return newest;
}

function publishEnvironment() {
  const env = { ...process.env };
  if (process.platform !== "win32") {
    // dotnet-install's default folder, which it leaves off PATH.
    env.PATH = `${join(homedir(), ".dotnet")}:${env.PATH ?? ""}`;
    return env;
  }
  // NativeAOT finds the MSVC linker by running vswhere, which the Visual Studio installer doesn't put on PATH.
  const pathKey = Object.keys(env).find((key) => key.toUpperCase() === "PATH") ?? "Path";
  const installer = join(env["ProgramFiles(x86)"] ?? "C:\\Program Files (x86)", "Microsoft Visual Studio", "Installer");
  env[pathKey] = `${installer};${env[pathKey] ?? ""}`;
  return env;
}
