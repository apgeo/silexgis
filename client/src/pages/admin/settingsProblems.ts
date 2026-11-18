// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';
import { ApiError } from '../../api/client.ts';

/**
 * Why saving an installation settings section failed, in words the administrator can act on.
 *
 * The settings forms call the API directly, so a refusal reaches them as the problem body the
 * server wrote rather than as a thrown error; both shapes are read here, and a failure that never
 * reached the server (no body at all) is said as the general failure. A value the server refused
 * must not read like a dropped connection: one is fixed on the form, the other by trying again.
 * The keys are literal on purpose: the check that every key used exists in both languages can
 * only see literal keys.
 */
export function settingsSaveProblem(error: unknown, t: TFunction): string {
  const code = error instanceof ApiError
    ? error.code
    : typeof error === 'object' && error !== null && 'code' in error && typeof error.code === 'string'
      ? error.code
      : undefined;

  switch (code) {
    case 'validation.failed':
    case 'validation.invalid_enum':
      return t('admin.settingsProblems.invalid');
    case 'access.forbidden':
      return t('admin.settingsProblems.forbidden');
    case 'settings.sms_gateway_internal':
      return t('admin.settingsProblems.smsGatewayInternal');
    default:
      return t('common.saveFailed');
  }
}

/**
 * A note the server attached to a save it accepted, in words the administrator can act on — today
 * only that a stored secret was dropped because the server it belonged to changed, so it has to
 * be typed again.
 *
 * A note this page has no words for is still said, generically, rather than dropped: the server
 * only attaches one when something about the save needs the administrator's attention.
 */
export function settingsNotice(code: string, t: TFunction): string {
  switch (code) {
    case 'settings.mail_password_cleared':
      return t('admin.settingsProblems.mailPasswordCleared');
    case 'settings.sms_auth_header_cleared':
      return t('admin.settingsProblems.smsAuthHeaderCleared');
    default:
      return t('admin.settingsProblems.noticeUnworded');
  }
}

/**
 * Why a test message could not be asked for — as opposed to one that was tried and failed, which
 * the server answers with the transport's own error. A channel not yet configured, a test text
 * asked for too soon after the last, and a request that never reached the server are different
 * things for the administrator to do, so each is said in its own words.
 */
export function testSendProblem(error: unknown, t: TFunction): string {
  const code = error instanceof ApiError
    ? error.code
    : typeof error === 'object' && error !== null && 'code' in error && typeof error.code === 'string'
      ? error.code
      : undefined;

  switch (code) {
    case 'admin.mail_not_configured':
    case 'admin.sms_not_configured':
      return t('admin.messaging.testNotConfigured');
    case 'admin.sms_test_too_soon':
      return t('admin.messaging.testTooSoon');
    case 'validation.failed':
      return t('admin.messaging.testRecipientInvalid');
    case 'access.forbidden':
      return t('admin.settingsProblems.forbidden');
    default:
      return t('admin.messaging.testRequestFailed');
  }
}
