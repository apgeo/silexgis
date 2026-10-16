// SPDX-License-Identifier: AGPL-3.0-or-later
import { ApiError } from '../client.ts';
import { parseAccessActions, useCan, useEffectiveAccess, type AccessActionFlag } from './access.ts';

/** What the caller may do with one feature, as the controls that act on it need to know. */
export interface ObjectRights {
  canEdit: boolean;
  canDelete: boolean;
  canShare: boolean;
  canManagePermissions: boolean;
  /**
   * Whether the answer is the object's own yet, rather than the installation-wide stand-in. A
   * control drawn from the stand-in is drawn but not usable until it is: the stand-in can be
   * wrong both ways for one object.
   */
  settled: boolean;
}

/** The same four rights held installation-wide, which stand in until the object has answered. */
export interface InstallationWideRights {
  write: boolean;
  delete: boolean;
  share: boolean;
  managePermissions: boolean;
}

/**
 * The rights to act on what was asked for, from the actions this caller holds on THE OBJECT, or
 * — until that answer arrives — from the same rights held installation-wide, each right standing
 * in for itself.
 *
 * Exported so the rule is tested on its own; `useFeatureRights` is how a screen asks.
 */
export function objectRightsFrom(
  actions: string | null | undefined,
  installationWide: InstallationWideRights,
): ObjectRights {
  if (actions === undefined) {
    return {
      canEdit: installationWide.write,
      canDelete: installationWide.delete,
      canShare: installationWide.share,
      canManagePermissions: installationWide.managePermissions,
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
 * Whether a failed answer about an object is still the object's answer: refused, or not found,
 * for this caller — which holds no right on it. Anything else (the server unreachable, a session
 * that lapsed) is no answer yet.
 */
export function isSettledRefusal(error: unknown): boolean {
  return error instanceof ApiError && (error.status === 403 || error.status === 404);
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
  const installationWide: InstallationWideRights = {
    write: useCan('features', 'write'),
    delete: useCan('features', 'delete'),
    share: useCan('features', 'share'),
    managePermissions: useCan('features', 'managePermissions'),
  };
  const { data, error } = useEffectiveAccess('feature', featureId, { enabled: options.enabled ?? true });
  const actions = data ? (data.actions ?? '') : isSettledRefusal(error) ? '' : undefined;
  return objectRightsFrom(actions, installationWide);
}
