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
    offered.find((profile) => profile.builtIn && groups !== undefined && changedProfileKeys(profile.values, groups).length === 0)?.id ??
    null
  );
}
