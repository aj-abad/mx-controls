import { test as base, expect, chromium, type Page, type Browser } from "@playwright/test";
import { spawn, type ChildProcess } from "node:child_process";
import { once } from "node:events";
import { mkdir, rm, writeFile, appendFile, access } from "node:fs/promises";
import net from "node:net";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { randomUUID } from "node:crypto";
import { MockAgent } from "./mock-agent";

const root = fileURLToPath(new URL("../..", import.meta.url));
export const nativeExecutable = path.join(root, "src-tauri/target/native-tests/debug/mx-controls.exe");
interface NativeEnvironment {
  run: string; pipe: string; hardwareAgentEnabled: boolean; visible: boolean; focused: boolean;
  width: number; height: number;
}
type NativeApp = { page: Page; agent: MockAgent; environment: () => Promise<NativeEnvironment> };

async function freePort() {
  const server = net.createServer();
  server.listen(0, "127.0.0.1");
  await once(server, "listening");
  const port = (server.address() as net.AddressInfo).port;
  await new Promise<void>(resolve => server.close(() => resolve()));
  return port;
}

async function debuggerReady(endpoint: string, child: ChildProcess) {
  const deadline = Date.now() + 20_000;
  while (Date.now() < deadline) {
    if (child.exitCode !== null) throw new Error(`Hidden native app exited (${child.exitCode})`);
    try {
      const response = await fetch(`${endpoint}/json/version`, { signal: AbortSignal.timeout(500) });
      if (response.ok && (await response.json()).webSocketDebuggerUrl) return;
    } catch { /* WebView2 is still starting. */ }
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  throw new Error("The test app's private WebView2 debugger did not start");
}

async function stopOwnedProcess(child: ChildProcess) {
  if (!child.pid || child.exitCode !== null || child.signalCode !== null) return;
  // Only the process handle returned by our spawn; no process-name lookup or desktop control.
  const exited = once(child, "exit").catch(() => {});
  if (!child.kill()) throw new Error(`Could not stop test process ${child.pid}`);
  await Promise.race([exited, new Promise<never>((_, reject) => {
    const timer = setTimeout(() => reject(new Error(`Test process ${child.pid} did not exit`)), 5_000);
    timer.unref();
  })]);
}

export const test = base.extend<{ native: NativeApp }>({
  native: async ({}, use, testInfo) => {
    if (process.platform !== "win32") throw new Error("Native UI tests currently require Windows/WebView2");
    await access(nativeExecutable).catch(() => { throw new Error("Run pnpm test:native:build before pnpm test:native:run"); });
    const run = randomUUID();
    const pipe = `mx-controls-ui-test-${run}`;
    const dataDir = testInfo.outputPath("webview-profile");
    await mkdir(dataDir, { recursive: true });
    const agent = new MockAgent();
    const log: string[] = [];
    const pageErrors: string[] = [];
    let child: ChildProcess | undefined;
    let browser: Browser | undefined;
    let page: Page | undefined;
    let tracing = false;
    let environment: (() => Promise<NativeEnvironment>) | undefined;
    try {
      await agent.listen(pipe);
      const port = await freePort();
      child = spawn(nativeExecutable, [], {
        cwd: root, windowsHide: true, stdio: ["ignore", "pipe", "pipe"],
        env: {
          ...process.env,
          MX_CONTROLS_TEST_RUN: run, MX_CONTROLS_TEST_PIPE: pipe,
          WEBVIEW2_USER_DATA_FOLDER: dataDir,
          WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS: `--remote-debugging-address=127.0.0.1 --remote-debugging-port=${port} --disable-backgrounding-occluded-windows --disable-background-timer-throttling --disable-renderer-backgrounding`,
        },
      });
      child.stdout?.on("data", data => log.push(String(data)));
      child.stderr?.on("data", data => log.push(String(data)));
      await once(child, "spawn");
      await writeFile(testInfo.outputPath("owned-process.json"), JSON.stringify({ pid: child.pid, run, pipe, executable: nativeExecutable }, null, 2));
      const endpoint = `http://127.0.0.1:${port}`;
      await debuggerReady(endpoint, child);
      browser = await chromium.connectOverCDP(endpoint);
      // WebView2 can expose a temporary blank target before the application target appears.
      await expect.poll(() => {
        page = browser!.contexts().flatMap(context => context.pages()).find(candidate => /^http:\/\/tauri\.localhost\//.test(candidate.url()));
        return !!page;
      }, { timeout: 20_000, message: "Waiting for the packaged native app target" }).toBe(true);
      if (!page) throw new Error("No packaged native app target");
      const context = page!.context();
      page.on("pageerror", error => { pageErrors.push(error.message); log.push(`pageerror: ${error.stack}`); });
      page.on("console", message => { if (message.type() === "error") log.push(`console: ${message.text()}`); });
      await expect(page).toHaveURL(/^http:\/\/tauri\.localhost\//);
      await page.waitForFunction(() => "__TAURI__" in window);
      environment = () => page!.evaluate(() => (window as unknown as {
        __TAURI__: { core: { invoke: (command: string) => Promise<NativeEnvironment> } };
      }).__TAURI__.core.invoke("ui_test_environment"));
      // Prove ownership before reloading or interacting with the attached WebView.
      const info = await environment();
      expect(info).toMatchObject({ run, pipe, hardwareAgentEnabled: false, visible: false, focused: false });
      await writeFile(testInfo.outputPath("native-environment.json"), JSON.stringify(info, null, 2));
      // This is a browser-level clipboard stub in the owned WebView, never the Windows clipboard.
      await context.addInitScript(() => {
        let text = "";
        Object.defineProperty(navigator, "clipboard", { configurable: true, value: {
          writeText: async (value: string) => { text = value; }, readText: async () => text,
        } });
      });
      await page.emulateMedia({ reducedMotion: "reduce" });
      await page.reload();
      await expect(page).toHaveURL(/^http:\/\/tauri\.localhost\//);
      await context.tracing.start({ screenshots: true, snapshots: true, sources: true });
      tracing = true;
      await expect(page.getByRole("button", { name: /^Macros/ })).toBeVisible();
      await expect.poll(() => agent.requests.some(r => r.method === "getState")).toBe(true);
      await use({ page, agent, environment });
      expect(await environment()).toMatchObject({ visible: false, focused: false });
      expect(pageErrors, "Unhandled renderer errors").toEqual([]);
    } finally {
      try {
        if (page && !page.isClosed()) {
          await page.screenshot({ path: testInfo.outputPath("native-window.png"), timeout: 5_000 });
          await testInfo.attach("Native WebView", { path: testInfo.outputPath("native-window.png"), contentType: "image/png" });
        }
        if (tracing) {
          await browser!.contexts()[0]!.tracing.stop({ path: testInfo.outputPath("trace.zip") });
          await testInfo.attach("trace", { path: testInfo.outputPath("trace.zip"), contentType: "application/zip" });
        }
      } finally {
        const lifecycle = (message: string) => appendFile(testInfo.outputPath("cleanup.log"), `${message}\n`);
        await lifecycle("Stopping owned native host");
        // Browser.close can wait indefinitely for WebView2's native host. Stop the owned host first.
        if (child) await stopOwnedProcess(child);
        await lifecycle("Disconnecting CDP");
        await browser?.close().catch(() => {});
        await lifecycle("Closing simulated agent");
        await agent.close();
        await writeFile(testInfo.outputPath("agent-requests.json"), JSON.stringify(agent.requests, null, 2));
        await writeFile(testInfo.outputPath("native.log"), log.join("\n"));
        await lifecycle("Removing private WebView profile");
        // The only recursively removed directory is this test's private, checked WebView profile.
        const resolved = path.resolve(dataDir);
        if (path.dirname(resolved) !== path.resolve(testInfo.outputDir) || path.basename(resolved) !== "webview-profile") {
          throw new Error("Refusing to remove a WebView profile outside this test's output directory");
        }
        await rm(resolved, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 });
        await lifecycle("Complete");
      }
    }
  },
});
export { expect };
