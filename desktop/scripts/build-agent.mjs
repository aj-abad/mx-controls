// Publishes the agent with NativeAOT to src-tauri/binaries, where Tauri bundles it as the `mx-agent` sidecar.
// Skips the publish when the binary is newer than every agent source file; `--force` publishes anyway.
import { execFileSync } from "node:child_process";
import { closeSync, copyFileSync, existsSync, mkdirSync, openSync, readdirSync, statSync } from "node:fs";
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
const target = join(desktop, "src-tauri", "binaries", `mx-agent-${triple}${extension}`);

// Tauri copies the sidecar beside the app on every build, which fails on Windows while an agent runs from that
// copy, so that agent is stopped; the app starts it again when it opens. An agent running from anywhere else, such
// as the one deploy.cmd unpacks, keeps the buttons working through the build.
stopAgentRunningFromBuild();

if (!process.argv.includes("--force") && existsSync(target) && newestSource(agentRoot) < statSync(target).mtimeMs) {
  console.log(`mx-agent is up to date: ${target}`);
  process.exit(0);
}

const output = join(desktop, "src-tauri", "target", "agent", runtime);
execFileSync(
  "dotnet",
  ["publish", join(agentRoot, "MxControls.Agent"), "-c", "Release", "-r", runtime, "-o", output, "-p:DebugType=none"],
  { stdio: "inherit", env: publishEnvironment() },
);
mkdirSync(dirname(target), { recursive: true });
copyFileSync(join(output, `mx-agent${extension}`), target);
console.log(`Published mx-agent for ${triple}: ${target}`);

function stopAgentRunningFromBuild() {
  if (process.platform !== "win32" || !existsSync(target)) return;
  const copies = ["debug", "release"].map((profile) => join(desktop, "src-tauri", "target", profile, "mx-agent.exe"));
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
  if (process.platform !== "win32") return env;
  // NativeAOT finds the MSVC linker by running vswhere, which the Visual Studio installer doesn't put on PATH.
  const pathKey = Object.keys(env).find((key) => key.toUpperCase() === "PATH") ?? "Path";
  const installer = join(env["ProgramFiles(x86)"] ?? "C:\\Program Files (x86)", "Microsoft Visual Studio", "Installer");
  env[pathKey] = `${installer};${env[pathKey] ?? ""}`;
  return env;
}
