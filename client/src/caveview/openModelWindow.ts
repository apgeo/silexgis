// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * Opens one survey model in the pop-out viewer window.
 *
 * The window NAME is deliberately per model rather than the single `silexgis-viewer3d` the map's
 * own pop-out button uses. A browser reuses a window with the same name, so a shared name means
 * asking for a second model replaces the first — which is right for the map's button, whose window
 * shows "whatever is selected", and wrong here, where the whole point of choosing a model is that
 * it stays the one being looked at. Two models opened from the list therefore get two windows, and
 * asking for the same one twice raises the window already showing it rather than making another.
 *
 * The size matches the map's pop-out so the two feel like one feature.
 */
export function openModelWindow(surveyModelId: string): void {
  window.open(
    `/panel/viewer3d?model=${encodeURIComponent(surveyModelId)}`,
    `silexgis-viewer3d-${surveyModelId}`,
    'popup,width=1000,height=750',
  );
}
