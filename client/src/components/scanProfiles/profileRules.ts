// SPDX-License-Identifier: AGPL-3.0-or-later
import { ApiError } from '../../api/client.ts';
import type { ProcessingProfile, ProcessingProfileScope } from '../../api/hooks.ts';

export interface ProfileScopeChoice {
  scope: ProcessingProfileScope;
  cavingGroupId: string | null;
}

/** Whether a scope choice names everything its scope needs: a group profile names its group. */
export function scopeChoiceComplete(choice: ProfileScopeChoice): boolean {
  return choice.scope !== 'cavingGroup' || !!choice.cavingGroupId;
}

/** The profiles of one step in the order the page lists them: by scope, built-ins first, then by name. */
export function profilesByScope(
  profiles: readonly ProcessingProfile[],
  stepId: string,
): Record<ProcessingProfileScope, ProcessingProfile[]> {
  const sorted = profiles
    .filter((profile) => profile.stepId === stepId)
    .sort((a, b) => Number(b.builtIn) - Number(a.builtIn) || a.name.localeCompare(b.name));
  return {
    installation: sorted.filter((p) => p.scope === 'installation'),
    cavingGroup: sorted.filter((p) => p.scope === 'cavingGroup'),
    personal: sorted.filter((p) => p.scope === 'personal'),
  };
}

/** The words for a refused save: the server's own list of refused keys, which no client wording can name. */
export function profileSaveErrorText(error: unknown, t: (key: string) => string): string {
  if (error instanceof ApiError) {
    if (error.code === 'processing_profile.values_invalid' && error.detail) {
      return `${t('settings.scanProfiles.errors.valuesInvalid')} ${error.detail}`;
    }
    if (error.code === 'processing_profile.built_in') return t('settings.scanProfiles.errors.builtIn');
    if (error.status === 403) return t('settings.scanProfiles.errors.forbidden');
  }
  return t('common.saveFailed');
}

/** The words for a file the server could not take at all, as opposed to one it reported on. */
export function profileImportErrorText(error: unknown, t: (key: string) => string): string {
  if (error instanceof ApiError) {
    switch (error.code) {
      case 'processing_profile.import_unreadable':
        return t('settings.scanProfiles.import.errors.unreadable');
      case 'processing_profile.import_not_a_profile':
        return t('settings.scanProfiles.import.errors.notAProfile');
      case 'processing_profile.defaults_unknown':
        return t('settings.scanProfiles.import.errors.defaultsUnknown');
      case 'processing_profile.step_unknown':
      case 'processing_profile.step_without_parameters':
        return t('settings.scanProfiles.import.errors.stepUnknown');
      case 'processing_profile.import_refused':
        return t('settings.scanProfiles.import.errors.refused');
    }
    if (error.status === 403) return t('settings.scanProfiles.errors.forbidden');
  }
  return t('common.saveFailed');
}
