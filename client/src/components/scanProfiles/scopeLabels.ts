// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ProcessingProfileScope } from '../../api/hooks.ts';

/** The words for each scope, written out so every key is a literal the string checks can find. */
export const SCOPE_LABEL_KEYS: Record<ProcessingProfileScope, string> = {
  installation: 'settings.scanProfiles.scope.installation',
  cavingGroup: 'settings.scanProfiles.scope.cavingGroup',
  personal: 'settings.scanProfiles.scope.personal',
};

/** The words for each step a profile is kept for; a step the client does not know shows its id. */
export const STEP_LABEL_KEYS: Record<string, string> = {
  'hdmapping.odometry': 'settings.scanProfiles.step.odometry',
  'pointcloud.downsample': 'settings.scanProfiles.step.downsample',
  'py3dtiles.tile': 'settings.scanProfiles.step.tile',
  'pointcloud.estimate': 'settings.scanProfiles.step.estimate',
  'mesh.surface': 'settings.scanProfiles.step.surface',
};
