import {
  PhAirplane,
  PhBriefcase,
  PhCamera,
  PhChartLine,
  PhCode,
  PhCoffee,
  PhCube,
  PhFilmStrip,
  PhGameController,
  PhGlobe,
  PhGraduationCap,
  PhHeart,
  PhHouse,
  PhLightning,
  PhMusicNotes,
  PhPaintBrush,
  PhPenNib,
  PhPresentationChart,
  PhStar,
  PhTerminalWindow,
} from "@phosphor-icons/vue";
import type { Component } from "vue";

/** The icons a profile can wear, by the kebab-case name the agent stores. */
export const PROFILE_ICONS: Record<string, Component> = {
  briefcase: PhBriefcase,
  house: PhHouse,
  code: PhCode,
  "terminal-window": PhTerminalWindow,
  "game-controller": PhGameController,
  "paint-brush": PhPaintBrush,
  "pen-nib": PhPenNib,
  cube: PhCube,
  "film-strip": PhFilmStrip,
  "music-notes": PhMusicNotes,
  camera: PhCamera,
  "chart-line": PhChartLine,
  "presentation-chart": PhPresentationChart,
  globe: PhGlobe,
  "graduation-cap": PhGraduationCap,
  coffee: PhCoffee,
  airplane: PhAirplane,
  heart: PhHeart,
  star: PhStar,
  lightning: PhLightning,
};

/** The first icon no profile wears yet, so a new profile looks different from the rest. */
export function unusedProfileIcon(profiles: { icon: string | null }[]) {
  const worn = new Set(profiles.map((p) => p.icon));
  return Object.keys(PROFILE_ICONS).find((name) => !worn.has(name)) ?? null;
}
