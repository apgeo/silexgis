// SPDX-License-Identifier: AGPL-3.0-or-later
import type { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';
import { formatBytes, insufficientFigures, limitKey } from '../storage/storageFigures.ts';

type Translate = ReturnType<typeof useTranslation>['t'];

/**
 * How a processing run can end badly, in the words of somebody who has to act on it. These codes
 * sit in the `error` member of an upload's status, or of one of its artefacts — the status call
 * itself answers 200 — so they are looked up from the status, not from a failed request.
 *
 * Exported so the wording can be checked against both locales: the keys are looked up through a
 * variable, which hides every one of them from the scan that catches a key nobody translated.
 */
export const SURVEY_UPLOAD_RUN_ERROR_KEYS: Record<string, string> = {
  'survey_pipeline.files_missing': 'surveyUploads.problems.filesMissing',
  'survey_pipeline.archive_invalid': 'surveyUploads.problems.archiveInvalid',
  'survey_pipeline.archive_too_large': 'surveyUploads.problems.archiveTooLarge',
  'survey_pipeline.unexpected': 'surveyUploads.problems.unexpected',
  'survey_pipeline.source_missing': 'surveyUploads.problems.sourceMissing',
  'survey_pipeline.source_invalid': 'surveyUploads.problems.sourceInvalid',
  'survey_pipeline.compiler_unavailable': 'surveyUploads.problems.compilerUnavailable',
  'survey_pipeline.compile_failed': 'surveyUploads.problems.compileFailed',
  'survey_pipeline.compile_timeout': 'surveyUploads.problems.compileTimeout',
  'survey_pipeline.output_missing': 'surveyUploads.problems.outputMissing',
  'survey_pipeline.crs_unknown': 'surveyUploads.problems.crsUnknown',
  'survey_pipeline.store_failed': 'surveyUploads.problems.storeFailed',
  'survey_pipeline.storage_insufficient': 'surveyUploads.problems.storageInsufficient',
  'survey_pipeline.surface_unplaced': 'surveyUploads.problems.surfaceUnplaced',
};

/**
 * What a failed *request* about an upload can answer with: running again, making or withdrawing a
 * public link. Anything not listed falls back to a general sentence rather than a paraphrase of a
 * code nobody wrote wording for.
 */
export const SURVEY_UPLOAD_REQUEST_ERROR_KEYS: Record<string, string> = {
  'survey_upload.not_found': 'surveyUploads.problems.notFound',
  'survey_upload.not_completed': 'surveyUploads.problems.notCompleted',
  'survey_upload.run_in_progress': 'surveyUploads.problems.runInProgress',
  'survey_upload.output_unknown': 'surveyUploads.problems.outputUnknown',
  'survey_upload.public_links_disabled': 'surveyUploads.problems.publicLinksDisabled',
  'survey_upload.public_link_forbidden': 'surveyUploads.problems.publicLinkForbidden',
  'survey_upload.public_link_refused_protected': 'surveyUploads.problems.publicLinkProtected',
  'survey_upload.not_awaiting_external': 'surveyUploads.problems.notAwaitingExternal',
  'survey_upload.external_output_unknown': 'surveyUploads.problems.externalOutputUnknown',
  'survey_upload.external_output_invalid': 'surveyUploads.problems.externalOutputInvalid',
  'survey_upload.external_output_too_large': 'surveyUploads.problems.externalOutputTooLarge',
  'survey_upload.external_step_invalid': 'surveyUploads.problems.externalStepInvalid',
  'survey_upload.session_needs_scan': 'surveyUploads.problems.sessionNeedsScan',
  'survey_upload.surface_needs_tiles': 'surveyUploads.problems.surfaceNeedsTiles',
  'survey_upload.surface_needs_placement': 'surveyUploads.problems.surfaceNeedsPlacement',
  'survey_upload.surface_profile_step': 'surveyUploads.problems.surfaceProfileStep',
  'survey_upload.profile_unknown': 'surveyUploads.problems.profileUnknown',
  'survey_upload.profile_values_invalid': 'surveyUploads.problems.profileValuesInvalid',
  'cave.not_found': 'surveyUploads.problems.caveNotFound',
  'acl.forbidden': 'surveyUploads.problems.forbidden',
};

/** The sentence for a run's recorded error, or `null` when this client has no wording for the code. */
export function surveyUploadRunErrorText(code: string, t: Translate): string | null {
  const key = SURVEY_UPLOAD_RUN_ERROR_KEYS[code];
  return key ? t(key) : null;
}

/** The sentence for a failed request about an upload. */
export function surveyUploadRequestErrorText(error: unknown, t: Translate): string {
  // Not enough room: said with the amount missing and the ceiling it is under, read from the
  // refusal's own members, so the person knows how much to free or to ask an administrator for.
  const room = error instanceof ApiError ? insufficientFigures(error.problem) : null;
  if (room) {
    return t('fileStorage.room.refused', { missing: formatBytes(room.missingBytes), limit: t(limitKey(room.limit)) });
  }
  const key = error instanceof ApiError && error.code ? SURVEY_UPLOAD_REQUEST_ERROR_KEYS[error.code] : undefined;
  return key ? t(key) : t('surveyUploads.problems.requestFailed');
}
