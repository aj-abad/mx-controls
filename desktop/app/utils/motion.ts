import type { Transition } from "motion-v";

/**
 * Timings for the whole app, after Emil Kowalski's guidance: UI motion stays under about 200ms and eases out
 * hard, so it starts moving the instant you act and spends its tail settling, which reads as fast. Feedback
 * seen many times a minute, like hover, isn't animated at all.
 */

/** A strong ease-out; the CSS animations in app.css use the same curve. */
export const EASE_OUT = [0.23, 1, 0.32, 1] as const;

/** Small state changes: toggles, the segmented control. */
export const FAST: Transition = { duration: 0.15, ease: EASE_OUT };

/** Panels and popovers, and the drawing making room for a control's settings. */
export const BASE: Transition = { duration: 0.2, ease: EASE_OUT };

/** Leaving is quicker than arriving, so what's going never holds up what's next. */
export const EXIT: Transition = { duration: 0.1, ease: EASE_OUT };

/** Feedback for a press on the mouse; it never blocks anything, so it can linger a little. */
export const PULSE: Transition = { duration: 0.4, ease: EASE_OUT };
