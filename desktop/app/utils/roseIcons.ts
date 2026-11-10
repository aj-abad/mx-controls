import {
  PhArrowClockwise,
  PhArrowCounterClockwise,
  PhArrowLeft,
  PhArrowRight,
  PhArrowsLeftRight,
  PhBrowser,
  PhCamera,
  PhChatCircle,
  PhClipboardText,
  PhCode,
  PhCopy,
  PhCrosshair,
  PhDesktop,
  PhEnvelope,
  PhFloppyDisk,
  PhFolder,
  PhGlobe,
  PhHand,
  PhHeart,
  PhHouse,
  PhImage,
  PhLightning,
  PhLock,
  PhMagnifyingGlass,
  PhMagnifyingGlassMinus,
  PhMagnifyingGlassPlus,
  PhMicrophone,
  PhPaintBrush,
  PhPlay,
  PhPlus,
  PhQuestion,
  PhScissors,
  PhShield,
  PhSkipBack,
  PhSkipForward,
  PhSpeakerHigh,
  PhSpeakerSlash,
  PhSquaresFour,
  PhStack,
  PhStar,
  PhTerminalWindow,
  PhTrash,
  PhWarning,
  PhX,
} from "@phosphor-icons/vue";
import { h, render, type Component } from "vue";

/** The icons a rose option can wear, by the kebab-case name the agent stores. */
export const ROSE_ICONS: Record<string, Component> = {
  copy: PhCopy,
  "clipboard-text": PhClipboardText,
  scissors: PhScissors,
  "arrow-counter-clockwise": PhArrowCounterClockwise,
  "arrow-clockwise": PhArrowClockwise,
  "floppy-disk": PhFloppyDisk,
  trash: PhTrash,
  plus: PhPlus,
  x: PhX,
  "magnifying-glass": PhMagnifyingGlass,
  "magnifying-glass-plus": PhMagnifyingGlassPlus,
  "magnifying-glass-minus": PhMagnifyingGlassMinus,
  "arrow-left": PhArrowLeft,
  "arrow-right": PhArrowRight,
  "arrows-left-right": PhArrowsLeftRight,
  browser: PhBrowser,
  "squares-four": PhSquaresFour,
  desktop: PhDesktop,
  house: PhHouse,
  folder: PhFolder,
  play: PhPlay,
  "skip-back": PhSkipBack,
  "skip-forward": PhSkipForward,
  "speaker-high": PhSpeakerHigh,
  "speaker-slash": PhSpeakerSlash,
  microphone: PhMicrophone,
  camera: PhCamera,
  image: PhImage,
  "paint-brush": PhPaintBrush,
  "terminal-window": PhTerminalWindow,
  code: PhCode,
  "chat-circle": PhChatCircle,
  envelope: PhEnvelope,
  globe: PhGlobe,
  lock: PhLock,
  stack: PhStack,
  lightning: PhLightning,
  star: PhStar,
  heart: PhHeart,
  hand: PhHand,
  shield: PhShield,
  warning: PhWarning,
  crosshair: PhCrosshair,
  question: PhQuestion,
};

/**
 * The icon's outline as SVG path data on Phosphor's 256-unit square, in the bold weight, which stays legible at the
 * overlay's size. The agent draws icons from this, having no icon font of its own.
 */
export function roseIconPath(name: string): string | null {
  const icon = ROSE_ICONS[name];
  if (!icon) return null;
  const host = document.createElement("div");
  render(h(icon, { weight: "bold" }), host);
  const data = [...host.querySelectorAll("path")].map((path) => path.getAttribute("d") ?? "").join(" ").trim();
  render(null, host);
  return data || null;
}
