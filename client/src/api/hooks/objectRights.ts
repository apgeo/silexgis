// SPDX-License-Identifier: AGPL-3.0-or-later
import { parseAccessActions, useCan, useEffectiveAccess, type AccessActionFlag } from './access.ts';

/** What the caller may do with one feature, as the controls that act on it need to know. */
export interface ObjectRights {
  canEdit: boolean;
  canDelete: boolean;
  canShare: boolean;
  canManagePermissions: boolean;
  /** Whether the answer is the object's own yet, rather than the installation-wide stand-in. */
  settled: boolean;
}

/**
 * The rights to act on what was asked for, from the actions this caller holds on THE OBJECT, or
 * — until that answer arrives, or where none is asked for — from the installation-wide rights.
 *
 * Exported so the rule is tested on its own; `useFeatureRights` is how a screen asks.
 */
export function objectRightsFrom(actions: string | null | undefined, installationWide: boolean): ObjectRights {
  if (actions === undefined) {
    return {
      canEdit: installationWide,
      canDelete: installationWide,
      canShare: installationWide,
      canManagePermissions: installationWide,
      settled: false,
    };
  }
  const held = parseAccessActions(actions);
  const has = (flag: AccessActionFlag) => held.has(flag);
  return {
    canEdit: has('write'),
    canDelete: has('delete'),
    canShare: has('share'),
    canManagePermissions: has('managePermissions'),
    settled: true,
  };
}

/**
 * A feature's own rights for the controls that act on it — the same answer its page acts on.
 *
 * A right can be granted or withheld on one object, so the installation-wide `features: write`
 * is the wrong question for a selected feature: a member may write one cave they were given and
 * nothing else, or write everything except one cave held back from them. The per-object answer
 * decides as soon as it arrives; the installation-wide one only bridges the first moment, and the
 * server re-decides every write either way.
 */
export function useFeatureRights(featureId: string | undefined, options: { enabled?: boolean } = {}): ObjectRights {
  const installationWide = useCan('features', 'write');
  const { data } = useEffectiveAccess('feature', featureId, { enabled: options.enabled ?? true });
  return objectRightsFrom(data ? (data.actions ?? '') : undefined, installationWide);
}
