// Builds ezPz and replaces the installed copy on macOS, signed with a stable identity so that the agent keeps
// its Accessibility permission. Signing identity: EZPZ_SIGNING_IDENTITY, or the keychain's only one.
// `--to <folder>` installs elsewhere, and `--no-open` leaves the app closed.
import { execFileSync, spawnSync } from "node:child_process";
import { cpSync, existsSync, renameSync, rmSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { setTimeout as sleep } from "node:timers/promises";

if (process.platform !== "darwin") throw new Error("install-app only supports macOS.");

const APP_NAME = "ezPz.app";
// The agent runs from its own helper app, so it has no Dock icon; see build-agent.mjs. macOS ties its permissions to
// the signing team and the helper's bundle identifier.
const AGENT_APP = "Contents/Helpers/ezPz Agent.app";
const AGENT = `${AGENT_APP}/Contents/MacOS/ezpz-agent`;

const desktop = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const folderFlag = process.argv.indexOf("--to");
const folder = folderFlag === -1 ? "/Applications" : resolve(process.argv[folderFlag + 1]);
const installed = join(folder, APP_NAME);
const built = join(desktop, "src-tauri/target/release/bundle/macos", APP_NAME);

// Before the build, so a missing identity fails fast.
const identity = signingIdentity();

execFileSync("pnpm", ["tauri", "build", "--bundles", "app"], { cwd: desktop, stdio: "inherit" });
// Inside out: a bundle's signature covers the code nested in it.
sign(join(built, AGENT_APP));
sign(built);
execFileSync("codesign", ["--verify", "--deep", "--strict", built], { stdio: "inherit" });

// Nothing below runs unless the new build is ready. A running copy would take over the new one's launch.
await stopApp(join(installed, "Contents/MacOS/ezpz"));
stopAgent(join(built, AGENT));
await removeFormer();
replace(built, installed);
console.log(`Installed ${installed}`);

if (!process.argv.includes("--no-open")) execFileSync("open", [installed]);

function signingIdentity() {
  if (process.env.EZPZ_SIGNING_IDENTITY) return process.env.EZPZ_SIGNING_IDENTITY;
  const listing = execFileSync("security", ["find-identity", "-v", "-p", "codesigning"], { encoding: "utf8" });
  const hashes = [...listing.matchAll(/^\s*\d+\) ([0-9A-F]{40}) /gm)].map((match) => match[1]);
  if (hashes.length === 1) return hashes[0];
  throw new Error(`Found ${hashes.length} signing identities; set EZPZ_SIGNING_IDENTITY to the one to use.`);
}

function sign(path, extra = []) {
  execFileSync("codesign", ["--force", "--options", "runtime", "--sign", identity, ...extra, path], { stdio: "inherit" });
}

async function stopApp(executable) {
  const pattern = `^${executable.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}`;
  spawnSync("pkill", ["-f", pattern]);
  for (let i = 0; i < 50; i++) {
    if (spawnSync("pgrep", ["-f", pattern]).status !== 0) return;
    await sleep(100);
  }
  throw new Error(`${executable} is still running; quit it and try again.`);
}

// Clean, so the buttons go back to the mouse. Not fatal: the new app replaces an agent from another build.
function stopAgent(agent) {
  const { status } = spawnSync(agent, ["--stop"], { stdio: "ignore", timeout: 15_000 });
  if (status !== 0) console.warn("Couldn't stop the running agent; the new app will replace it when it opens.");
}

// The app was called MX Studio. Its agent listens on another pipe, so the new one can't stop it; it's stopped here,
// and the old copy removed, so two agents don't fight over the mouse.
async function removeFormer() {
  const former = join(folder, "MX Studio.app");
  if (!existsSync(former)) return;
  await stopApp(join(former, "Contents/MacOS/mx-controls"));
  const agent = join(former, "Contents/Helpers/MX Studio Agent.app/Contents/MacOS/mx-agent");
  if (existsSync(agent)) stopAgent(agent);
  rmSync(former, { recursive: true, force: true });
  console.log(`Removed ${former}`);
}

// Moved into place rather than copied over, since macOS can kill a process whose signed file changes under it. The
// old copy is kept until the new one is in place.
function replace(source, target) {
  const staged = `${target}.new`;
  const previous = `${target}.old`;
  rmSync(staged, { recursive: true, force: true });
  rmSync(previous, { recursive: true, force: true });
  execFileSync("ditto", [source, staged]);
  const hadPrevious = existsSync(target);
  if (hadPrevious) renameSync(target, previous);
  try {
    renameSync(staged, target);
  } catch (error) {
    if (hadPrevious) renameSync(previous, target);
    throw error;
  }
  rmSync(previous, { recursive: true, force: true });
}
