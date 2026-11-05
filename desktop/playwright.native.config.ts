import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: "./tests/native",
  testMatch: "**/*.spec.ts",
  outputDir: "./test-results/native",
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 45_000,
  expect: { timeout: 8_000 },
  reporter: [["list"], ["html", { outputFolder: "playwright-report/native", open: "never" }]],
  // The custom fixture attaches to the app's own WebView2, with no browser download or webServer.
});
