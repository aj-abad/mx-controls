// Compiles the Icon Composer icon, src-tauri/icons/AppIcon.icon, into the Assets.car that macOS 26 draws the app's
// layered icon from; Info.plist names it with CFBundleIconName. Earlier macOS versions use icons/icon.icns.
// Tauri runs this before bundling. It needs Xcode 26's actool, so it does nothing when not building for macOS.
import { execFileSync } from "node:child_process";
import { existsSync, mkdirSync, rmSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const target = process.env.TAURI_ENV_PLATFORM ?? process.platform;
if (target !== "darwin" && target !== "macos") process.exit(0);

const tauri = resolve(dirname(fileURLToPath(import.meta.url)), "..", "src-tauri");
const output = join(tauri, "target", "mac-icon");
rmSync(output, { recursive: true, force: true });
mkdirSync(output, { recursive: true });

try {
  execFileSync(
    "xcrun",
    [
      "actool",
      join(tauri, "icons", "AppIcon.icon"),
      "--compile", output,
      "--app-icon", "AppIcon",
      "--include-all-app-icons",
      "--platform", "macosx",
      "--target-device", "mac",
      "--minimum-deployment-target", "11.0",
      "--output-partial-info-plist", join(output, "partial-info.plist"),
      "--output-format", "human-readable-text",
      "--errors", "--warnings",
    ],
    { stdio: "inherit" },
  );
} catch (error) {
  console.error("actool couldn't compile AppIcon.icon. Icon Composer icons need Xcode 26 or later.");
  throw error;
}
if (!existsSync(join(output, "Assets.car"))) throw new Error(`actool wrote no Assets.car to ${output}`);
console.log(`Compiled the app icon: ${join(output, "Assets.car")}`);
