// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ProcessingProfile, ProcessingProfileChoice } from '../../api/hooks.ts';
import { changedFields, type ParameterGroup } from '../scanProfiles/parameterSchema.ts';

/**
 * The keys a profile's values change from the step's defaults, as dotted paths
 * (`values.filter_points.decimation`), in form order.
 *
 * The rule itself lives in the profile editor's helpers (a field counts when the profile names it
 * and the value is not the default), so the upload dialog and the editor always agree. A key the
 * schema does not describe is not a setting of the step and is not counted.
 */
export function changedProfileKeys(values: unknown, groups: readonly ParameterGroup[]): string[] {
  return changedFields(groups, values).map((field) => field.path.join('.'));
}

/** Every setting a profile's values name, as dotted paths of the leaves (objects are walked into). */
function namedKeys(values: unknown, prefix: readonly string[] = []): string[] {
  if (typeof values !== 'object' || values === null || Array.isArray(values)) {
    return prefix.length > 0 ? [prefix.join('.')] : [];
  }
  return Object.entries(values).flatMap(([key, value]) => namedKeys(value, [...prefix, key]));
}

/**
 * A profile stands in for the defaults only when nothing it names differs from them and the schema
 * knows everything it names. A key the installed version does not have is not counted as a change,
 * so without the second test a built-in written for another version — every key unknown here —
 * would look like "changes nothing", be pre-selected, and then be refused by the server.
 */
function changesNothing(profile: ProcessingProfile, groups: readonly ParameterGroup[]): boolean {
  const known = new Set(groups.flatMap((group) => group.fields.map((field) => field.path.join('.'))));
  return changedProfileKeys(profile.values, groups).length === 0 && namedKeys(profile.values).every((key) => known.has(key));
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
  groups: readonly ParameterGroup[] | undefined,
): string | null {
  const offered = profiles.filter((profile) => profile.stepId === stepId);
  const last = remembered?.find((choice) => choice.stepId === stepId)?.profileId;
  if (last && offered.some((profile) => profile.id === last)) {
    return last;
  }
  return (
    offered.find((profile) => profile.builtIn && groups !== undefined && changesNothing(profile, groups))?.id ??
    null
  );
}
