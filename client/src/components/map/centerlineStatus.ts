// SPDX-License-Identifier: AGPL-3.0-or-later

/** The layer panel's survey-line block: what the map-corner notice opens the panel at. */
export const CENTERLINE_STATUS_ID = 'map-centerline-status';

/**
 * Moves focus to the layer panel's survey-line block once it can take it, and scrolls it into
 * view. The panel is opened by the same press that calls this, and neither host shows it at
 * once: the desktop pane grows from zero width, and the phone drawer mounts its contents when it
 * first opens. A zero-width block still accepts focus, but nothing the reader can see moves to it,
 * so this waits a frame at a time until the block has width — and gives up after a bound rather
 * than chase a panel that was closed again in the meantime. Returns a cancel function.
 */
export function focusCenterlineStatus(maxFrames = 60): () => void {
  let frames = 0;
  let handle = 0;
  let cancelled = false;
  const attempt = () => {
    if (cancelled) {
      return;
    }
    const block = document.getElementById(CENTERLINE_STATUS_ID);
    if (block && block.getBoundingClientRect().width > 0) {
      block.scrollIntoView?.({ block: 'nearest' });
      block.focus();
      return;
    }
    frames += 1;
    if (frames < maxFrames) {
      handle = window.requestAnimationFrame(attempt);
    }
  };
  handle = window.requestAnimationFrame(attempt);
  return () => {
    cancelled = true;
    window.cancelAnimationFrame(handle);
  };
}
