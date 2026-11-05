// Renders the Icon Composer icon, src-tauri/icons/AppIcon.icon, into the flat icons that aren't drawn from it: icon.ico
// and the PNGs for Windows, and icon.icns for macOS before 26. The results are committed, so Windows builds need no Mac.
// Run it with `pnpm icons` after changing the icon. It needs Xcode 26's Icon Composer, so it only runs on a Mac.
import { execFileSync } from "node:child_process";
import { copyFileSync, existsSync, mkdtempSync, readdirSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

if (process.platform !== "darwin") throw new Error("Rendering AppIcon.icon needs Icon Composer, which only runs on a Mac.");

const desktop = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const icons = join(desktop, "src-tauri", "icons");
const developer = execFileSync("xcode-select", ["-p"], { encoding: "utf8" }).trim();
const ictool = resolve(developer, "..", "Applications", "Icon Composer.app", "Contents", "Executables", "ictool");
if (!existsSync(ictool)) throw new Error(`No ictool at ${ictool}. Icon Composer comes with Xcode 26 or later.`);

const srgb = "/System/Library/ColorSync/Profiles/sRGB Profile.icc";
const work = mkdtempSync(join(tmpdir(), "mx-icons-"));

// ICO and ICNS can't follow the system appearance, so they carry the light rendition. Icon Composer renders in
// Display P3, and the icon tools drop the profile, so convert it or the colors shift.
function render(size, file) {
  const output = join(work, file);
  execFileSync(ictool, [
    join(icons, "AppIcon.icon"),
    "--export-image",
    "--output-file", output,
    "--platform", "macOS",
    "--rendition", "Default",
    "--width", String(size),
    "--height", String(size),
    "--scale", "1",
  ], { stdio: "ignore" });
  execFileSync("sips", ["--matchTo", srgb, output], { stdio: "ignore" });
  return output;
}

function tauriIcon(source, output) {
  execFileSync("pnpm", ["tauri", "icon", source, "--output", output], { cwd: desktop, stdio: "ignore" });
}

try {
  // Windows draws its icons as they are, so the artwork fills the whole image.
  const full = render(1024, "app-icon.png");
  copyFileSync(full, join(icons, "app-icon.png"));
  const flat = join(work, "flat");
  tauriIcon(full, flat);
  for (const file of readdirSync(icons)) {
    if (file !== "icon.icns" && existsSync(join(flat, file))) copyFileSync(join(flat, file), join(icons, file));
  }

  // macOS before 26 draws icon.icns as it is too, but expects 824 px of artwork on a 1024 px canvas.
  const inset = render(824, "inset.png");
  execFileSync("sips", ["--padToHeightWidth", "1024", "1024", inset], { stdio: "ignore" });
  const mac = join(work, "mac");
  tauriIcon(inset, mac);
  copyFileSync(join(mac, "icon.icns"), join(icons, "icon.icns"));

  console.log(`Rendered the flat icons into ${icons}`);
} finally {
  rmSync(work, { recursive: true, force: true });
}
