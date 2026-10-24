// SPDX-License-Identifier: AGPL-3.0-or-later
import { INSTALLATION_PRESET_KEY } from '../../components/map/workspace/workspacePresets.ts';

/** The stored starting arrangement as an object, or null when it is not one this page can edit. */
export function readPanelDefaults(raw: string): Record<string, unknown> | null {
  if (raw.trim() === '') {
    return {};
  }
  try {
    const parsed: unknown = JSON.parse(raw);
    return typeof parsed === 'object' && parsed !== null && !Array.isArray(parsed)
      ? (parsed as Record<string, unknown>)
      : null;
  } catch {
    return null;
  }
}

/**
 * The starting arrangement with the starting preset set — or removed, for the arrangement as
 * shipped — and every other member kept exactly as it was: the same document holds the panels'
 * default sections, which this page does not edit and must not lose.
 */
export function withStartingPreset(defaults: Record<string, unknown>, preset: string | null): string {
  const next: Record<string, unknown> = { ...defaults };
  if (preset === null) {
    delete next[INSTALLATION_PRESET_KEY];
  } else {
    next[INSTALLATION_PRESET_KEY] = preset;
  }
  return JSON.stringify(next);
}
