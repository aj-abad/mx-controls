// Drives the window of an agent `pnpm shell` started, through its pipe:
//   pnpm ui show                  opens or focuses the window
//   pnpm ui eval '<script>'       runs the body of an async function in the page; prints what it returns, as JSON
//   pnpm ui snapshot <file.png>   saves the page as it's drawn now (without the window's glass backdrop)
import { spawnSync } from "node:child_process";
import { homedir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const desktop = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const agent = join(desktop, "..", "agent", "EzPz.Agent", "bin", "Debug", "net10.0",
  process.platform === "win32" ? "ezpz-agent.exe" : "ezpz-agent");
const env = { ...process.env };
if (process.platform !== "win32") env.DOTNET_ROOT ??= join(homedir(), ".dotnet");
// Piped rather than inherited: the agent is a GUI program on Windows, which shows nothing on a console it inherits.
const { status, stdout, stderr } = spawnSync(agent, ["--ui", ...process.argv.slice(2)], { env, encoding: "utf8" });
process.stdout.write(stdout ?? "");
process.stderr.write(stderr ?? "");
process.exit(status ?? 1);
