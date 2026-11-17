// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * The words for each action the server records. The column is open-ended by design, so an action
 * this client has no words for is shown as its stored name rather than hidden.
 */
export const AUDIT_ACTION_KEYS: Record<string, string> = {
  created: 'audit.actions.created',
  updated: 'audit.actions.updated',
  deleted: 'audit.actions.deleted',
  permission_changed: 'audit.actions.permission_changed',
  notification_retried: 'audit.actions.notification_retried',
  login: 'audit.actions.login',
  two_factor_reset: 'audit.actions.two_factor_reset',
  upload_review_marked: 'audit.actions.upload_review_marked',
  external_account_linked: 'audit.actions.external_account_linked',
  exported: 'audit.actions.exported',
  storage_reclaimed: 'audit.actions.storage_reclaimed',
};
