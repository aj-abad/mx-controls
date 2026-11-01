# Native UI tests

The Windows UI suite runs the actual Tauri executable and its WebView2 renderer. Playwright connects to that instance through a loopback Chrome DevTools Protocol (CDP) endpoint. It does not use desktop automation, move the physical pointer, send OS keystrokes, or bring a window to the foreground.

## Run

Requires Windows, the WebView2 runtime, Rust with the Windows C++ toolchain, Node 24 or newer, and pnpm. There is no Playwright browser download and no .NET/NativeAOT build in this path.

```powershell
cd desktop
pnpm install
pnpm test:native
```

`test:native` generates the current frontend, compiles the isolated debug shell, type-checks the test harness, and runs the suite. After a test-only change, rerun without rebuilding:

```powershell
pnpm test:native:run
```

`pnpm test:native:typecheck` checks only the harness and its imported types. `pnpm typecheck` checks the application.

Rebuild after changing frontend or Rust code:

```powershell
pnpm test:native:build
pnpm test:native:run
```

The build lives in `desktop/src-tauri/target/native-tests`, separate from the development app, installed portable copy, and release artifacts. No dev server is required: the native executable serves the generated frontend at `http://tauri.localhost`.

## Isolation

- The `ui-tests` Cargo feature only compiles on Windows in debug mode and cannot be combined with `embedded-agent`.
- Before creating a window, Rust requires the test app identifier and a hidden, non-focusable window excluded from the taskbar. An incorrectly configured build refuses to start. Normal binaries also refuse a test-run token, so accidentally pointing the runner at one cannot start a real agent.
- The feature removes real-agent launching and requires a unique `mx-controls-ui-test-<run UUID>` pipe. There is no fallback to `mx-controls-agent`.
- Each test starts its own in-memory Node agent, WebView profile, app process, and loopback debugging port. Environment overrides apply only to that child process. Settings and device changes stay in the simulated service.
- The native `ui_test_environment` command confirms the run token, pipe, hardware isolation, and hidden/unfocused state. It is absent from normal builds.
- Recorder tests send synthetic key events through the mock pipe and the real Rust event bridge. They never install a keyboard hook. Clipboard tests replace the WebView clipboard with an in-memory implementation before the tested page loads.
- Cleanup terminates only the native host created by the fixture, disconnects its WebView, and removes only that test's checked WebView profile directory. It never searches for or stops a running user app/agent.

## Coverage and artifacts

The suite checks native startup and IPC, rejection of the real-agent pipe, and these interaction paths:

- Recording, native hotkeys, pause/resume, discard, capture timeout, startup cancellation, and helper/device connection loss with draft recovery.
- Inline shortcut capture, long chord containment, text/mouse step insertion, retained drafts and edit history, long lists, block dragging, and keyboard context menus.
- Gap-based delay editing, delay-budget trimming, zoom, precise preview seeking, pause/resume, and transport reset when switching macros.
- Save failure and retry while its alert remains visible, profile switching/renaming, device popovers, and gesture sequence editing.
- Button shortcuts stored inline through the real native IPC bridge, shortcut capture without macro creation, explicit copying of one-combination macros into an action, and gesture repeat settings with stable control geometry. Agent tests separately cover JSON settings reloads preserving inline shortcuts and existing shared macros.
- Light and dark appearance with standard motion, fixed editor controls, stable scrollbar space, and anchored gesture directions.
- Contained keyboard focus in both appearances, long preset-menu keyboard navigation, wheel scrolling, Reka thumb dragging, and horizontal timeline panning. The thumb test also intercepts native window-drag requests and verifies that scrollbar interaction does not request a window move.

Layout probes observe DOM changes, element resizes, and intermediate geometry rather than comparing only the end states. They check the editor's frames and transport controls while allowing intended changes to list contents. Assertions use accessible roles and labels. Unhandled renderer errors fail a test.

Each test writes a WebView screenshot, Playwright trace, native environment snapshot, pipe request log, and app/renderer error log beneath `desktop/test-results/native`. The HTML report is in `desktop/playwright-report/native`. Both directories are ignored by Git.

```powershell
pnpm exec playwright show-report playwright-report/native
pnpm exec playwright show-trace <path-to-trace.zip>
```

Screenshots capture only the owned WebView's contents. Hidden rendering does not qualify desktop compositor effects such as acrylic, real HID behavior, OS input injection, sign-in registration, or real-agent persistence. Those remain separate hardware/integration checks.

## Extend

Add specs under `desktop/tests/native` and import `test`/`expect` from `./fixture`. Use `native.page` for WebView interaction and `native.agent` for synthetic events, request inspection, and failure injection. The mock rejects unsupported methods explicitly; add a protocol implementation there when a new scenario needs one.

The approach follows [Playwright's WebView2 testing guidance](https://playwright.dev/docs/webview2), including per-test user-data folders and CDP attachment. The shell configuration uses [Tauri window options](https://v2.tauri.app/reference/config/#windowconfig).
