// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * The handful of shapes both the registry and the anchor editors need. It sits below both
 * of them on purpose: the registry names the editor for each anchor kind, and every editor
 * is written against the props declared here, so putting these in either of those modules
 * would make the pair import each other.
 */

/** Props an anchor editor is driven with — the plain value/onChange pair a form field uses. */
export interface AnchorEditorProps {
  value: unknown;
  onChange: (anchor: unknown) => void;
}

/**
 * One numeric field of an anchor payload, or null when it is absent or not a number. The
 * payload travels as free-form JSON, so every read of it is a question, never an assumption.
 */
export function readAnchorNumber(anchor: unknown, key: string): number | null {
  if (typeof anchor !== 'object' || anchor === null) {
    return null;
  }
  const value = (anchor as Record<string, unknown>)[key];
  return typeof value === 'number' && Number.isFinite(value) ? value : null;
}

/** `m:ss`, growing to `h:mm:ss` past the hour — the shape a media scrubber reads in. */
export function formatMediaTime(seconds: number): string {
  const whole = Math.max(0, Math.floor(seconds));
  const hours = Math.floor(whole / 3600);
  const minutes = Math.floor((whole % 3600) / 60);
  const secs = String(whole % 60).padStart(2, '0');
  return hours > 0 ? `${hours}:${String(minutes).padStart(2, '0')}:${secs}` : `${minutes}:${secs}`;
}
