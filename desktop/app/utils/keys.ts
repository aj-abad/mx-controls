const NAMED: Record<string, string> = {
  ShiftLeft: "Left Shift",
  ShiftRight: "Right Shift",
  ControlLeft: "Left Ctrl",
  ControlRight: "Right Ctrl",
  AltLeft: "Left Alt",
  AltRight: "Right Alt",
  MetaLeft: "Left Win",
  MetaRight: "Right Win",
  ArrowLeft: "←",
  ArrowUp: "↑",
  ArrowRight: "→",
  ArrowDown: "↓",
  Escape: "Esc",
  Backquote: "`",
  Minus: "-",
  Equal: "=",
  BracketLeft: "[",
  BracketRight: "]",
  Backslash: "\\",
  Semicolon: ";",
  Quote: "'",
  Comma: ",",
  Period: ".",
  Slash: "/",
  AudioVolumeUp: "Volume up",
  AudioVolumeDown: "Volume down",
  AudioVolumeMute: "Mute",
  MediaPlayPause: "Play/Pause",
  MediaTrackNext: "Next track",
  MediaTrackPrevious: "Previous track",
};

const MAC: Record<string, string> = {
  MetaLeft: "Left ⌘",
  MetaRight: "Right ⌘",
  AltLeft: "Left ⌥",
  AltRight: "Right ⌥",
};

/** A readable name for a `KeyboardEvent.code`, such as "Left Shift" or "K". */
export function keyLabel(code: string) {
  if (navigator.userAgent.includes("Mac") && MAC[code]) return MAC[code];
  if (NAMED[code]) return NAMED[code];
  if (/^Key[A-Z]$/.test(code)) return code.slice(3);
  if (/^Digit\d$/.test(code)) return code.slice(5);
  if (code.startsWith("Numpad")) return `Num ${code.slice(6)}`;
  return code;
}
