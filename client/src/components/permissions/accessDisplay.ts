// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';
import { parseAccessActions, type AccessActionFlag } from '../../api/hooks.ts';

/** Display order for action flags — the wire's comma-joined string has no stable order. */
export const ACTION_ORDER: readonly AccessActionFlag[] = [
  'read', 'write', 'create', 'delete', 'share', 'execute', 'managePermissions', 'viewExactLocation',
];

/** A comma-joined action set ("read, write") ordered and translated for display. */
export function actionSetLabels(t: TFunction, actions: string | null | undefined): string[] {
  const held = parseAccessActions(actions);
  return ACTION_ORDER.filter((flag) => held.has(flag)).map((flag) => t(`access.actions.${flag}`));
}

/** Sorts a set of flags into display order and joins them into the wire format. */
export function joinActions(flags: ReadonlySet<AccessActionFlag>): string {
  return ACTION_ORDER.filter((flag) => flags.has(flag)).join(', ');
}

interface ExplanationShape {
  allowed: boolean;
  source: string;
  level?: string | null;
  ruleName?: string | null;
  redacted: boolean;
}

/**
 * The reason behind one verdict, as a sentence. A redacted answer is presented honestly
 * — "a rule you cannot see decides this" — never as an error and never left blank: the
 * server withheld the anchor's name on purpose, and the gap is the information.
 */
export function explanationReason(t: TFunction, explanation: ExplanationShape): string {
  const level = explanation.level ? t(`access.levels.${explanation.level}`) : '';
  switch (explanation.source) {
    case 'fullAdministrators':
      return t('access.sources.fullAdministrators');
    case 'ownership':
      return t('access.sources.ownership');
    case 'visibility':
      return t('access.sources.visibility');
    case 'entries':
      if (explanation.redacted) {
        return t('access.decidedByHiddenRule', { level });
      }
      return explanation.ruleName
        ? t('access.decidedByRule', { level, rule: explanation.ruleName })
        : t('access.decidedByDirectRule', { level });
    default:
      // anonymous / defaultDeny — nothing grants it (and nothing specifically denies it).
      return t('access.sources.none');
  }
}
