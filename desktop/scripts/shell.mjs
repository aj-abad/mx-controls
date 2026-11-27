// Builds the agent and the page, and opens the agent's own Photino window with UI automation on, so tools can drive
// it through `pnpm ui` instead of the screen. macOS and Windows.
//   pnpm shell          serves the generated page
//   pnpm shell --dev    loads the dev server (`pnpm dev`) instead, with hot reload
// Stops a running ezPz agent first, which hands its buttons back to the mouse; the new one takes them over.
import { execFileSync, spawn, spawnSync } from "node:child_process";
import { mkdirSync, openSync } from "node:fs";
import { homedir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const desktop = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const agentProject = join(desktop, "..", "agent", "EzPz.Agent");
const agent = join(agentProject, "bin", "Debug", "net10.0", process.platform === "win32" ? "ezpz-agent.exe" : "ezpz-agent");
const dev = process.argv.includes("--dev");

const env = { ...process.env };
// dotnet-install's default folder, which it leaves off PATH; the Windows installer puts dotnet on PATH itself.
if (process.platform !== "win32") {
  env.DOTNET_ROOT ??= join(homedir(), ".dotnet");
  env.PATH = `${env.DOTNET_ROOT}:${env.PATH ?? ""}`;
}

execFileSync("dotnet", ["build", agentProject, "-c", "Debug", "-v", "quiet", "-nologo"], { env, stdio: "inherit" });
if (!dev) execFileSync("pnpm", ["generate"], { cwd: desktop, stdio: "inherit", shell: process.platform === "win32" });

// Another build's agent listens on another pipe, so --stop can't reach it, and the two would fight over the mouse.
if (process.platform === "darwin" && spawnSync("pgrep", ["-f", "Contents/(MacOS|Helpers/.*)/mx-agent"]).status === 0)
  console.warn("An MX Studio agent is running too; quit MX Studio first, or both will drive the mouse.");
spawnSync(agent, ["--stop"], { env, stdio: "ignore", timeout: 15_000 });

const logs = join(desktop, "src-tauri", "target", "shell");
mkdirSync(logs, { recursive: true });
const log = join(logs, "agent.log");
const output = openSync(log, "w");
const child = spawn(agent, ["--worker", "--window"], {
  env: {
    ...env,
    EZPZ_AUTOMATION: "1",
    ...(dev ? { EZPZ_UI_URL: "http://127.0.0.1:3000" } : { EZPZ_UI_DIR: join(desktop, ".output", "public") }),
  },
  detached: true,
  windowsHide: true,
  stdio: ["ignore", output, output],
});
child.unref();
console.log(`Agent ${child.pid} started with its window; output in ${log}`);
console.log("Drive it with: pnpm ui show | eval '<script>' | snapshot <file.png>");
