import { test, expect } from "@playwright/test";
import { spawn } from "node:child_process";
import { once } from "node:events";
import { randomUUID } from "node:crypto";
import { nativeExecutable } from "./fixture";

test("test shell rejects the real agent pipe before creating a window", async ({}, testInfo) => {
  const child = spawn(nativeExecutable, [], {
    windowsHide: true,
    stdio: ["ignore", "pipe", "pipe"],
    env: {
      ...process.env,
      MX_CONTROLS_TEST_RUN: randomUUID(),
      MX_CONTROLS_TEST_PIPE: "mx-controls-agent",
      WEBVIEW2_USER_DATA_FOLDER: testInfo.outputPath("unused-profile"),
    },
  });
  let stderr = "";
  child.stderr.on("data", data => { stderr += data; });
  try {
    const [code] = await once(child, "exit");
    expect(code).not.toBe(0);
    expect(stderr).toContain("Refusing a non-test agent pipe");
    await testInfo.attach("Startup guard", { body: stderr, contentType: "text/plain" });
  } finally {
    if (child.pid && child.exitCode === null && child.signalCode === null) child.kill();
  }
});
