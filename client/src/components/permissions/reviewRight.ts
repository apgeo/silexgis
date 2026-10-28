// SPDX-License-Identifier: AGPL-3.0-or-later
import type { AccessDomainName, AccessEffect } from '../../api/hooks.ts';

/** What a rule needs to say for this warning to be about it. */
interface RuleShape {
  effect: AccessEffect;
  domain: AccessDomainName;
}

/**
 * Whether any of these rules hands out the right to review uploads. Either of its actions counts:
 * deciding is done from the same screen that shows everything waiting.
 */
export function grantsUploadReview(rules: readonly RuleShape[] | undefined): boolean {
  return (rules ?? []).some((rule) => rule.effect === 'allow' && rule.domain === 'uploadReview');
}
