import { spawn } from "node:child_process";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import path from "node:path";

if (process.platform !== "win32") throw new Error("Native UI tests currently require Windows/WebView2.");
const root = fileURLToPath(new URL("..", import.meta.url));
const require = createRequire(import.meta.url);
const cli = path.join(path.dirname(require.resolve("@tauri-apps/cli/package.json")), "tauri.js");
const child = spawn(process.execPath, [cli, "build", "--debug", "--no-bundle", "--features", "ui-tests", "--config", "src-tauri/tauri.ui-tests.conf.json"], {
  cwd: root,
  env: { ...process.env, CARGO_TARGET_DIR: path.join(root, "src-tauri/target/native-tests") },
  stdio: "inherit",
  windowsHide: true,
});
child.on("error", error => { console.error(error); process.exitCode = 1; });
child.on("exit", code => { process.exitCode = code ?? 1; });
