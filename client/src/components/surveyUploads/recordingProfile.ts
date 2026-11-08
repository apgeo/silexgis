// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ProcessingProfile, ProcessingProfileChoice } from '../../api/hooks.ts';

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function same(a: unknown, b: unknown): boolean {
  return Array.isArray(a) || Array.isArray(b) ? JSON.stringify(a) === JSON.stringify(b) : a === b;
}

/**
 * The keys a profile's values change from the step's defaults, as dotted paths
 * (`values.filter_points.decimation`), in the order the profile holds them.
 *
 * A profile holds only the keys it sets and is laid over the defaults when it runs, so a key it
 * does not name is the default, and a key it names with the default's own value changes nothing.
 * A key the defaults do not have counts as changed: it is not what runs unasked.
 */
export function changedProfileKeys(values: unknown, defaults: unknown, prefix = ''): string[] {
  if (!isPlainObject(values)) {
    return values === undefined || same(values, defaults) ? [] : [prefix];
  }
  const base = isPlainObject(defaults) ? defaults : {};
  return Object.entries(values).flatMap(([key, value]) => {
    const path = prefix ? `${prefix}.${key}` : key;
    return isPlainObject(value) ? changedProfileKeys(value, base[key], path) : same(value, base[key]) ? [] : [path];
  });
}

/**
 * The profile an upload pre-selects for a step: the one the caller chose last, while it is still
 * offered; else a built-in that changes nothing (the program's defaults, under a name); else none,
 * which runs the step with its defaults all the same.
 */
export function preferredProfileId(
  stepId: string,
  profiles: readonly ProcessingProfile[],
  remembered: readonly ProcessingProfileChoice[] | undefined,
  defaults: unknown,
): string | null {
  const offered = profiles.filter((profile) => profile.stepId === stepId);
  const last = remembered?.find((choice) => choice.stepId === stepId)?.profileId;
  if (last && offered.some((profile) => profile.id === last)) {
    return last;
  }
  return (
    offered.find((profile) => profile.builtIn && changedProfileKeys(profile.values, defaults).length === 0)?.id ??
    null
  );
}
