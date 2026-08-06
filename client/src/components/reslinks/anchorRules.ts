// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';
import { readAnchorNumber } from './anchorTypes.ts';

/**
 * What the server will and will not accept for the anchors this client can compose, said
 * here so the form can say it before the round trip. Each rule mirrors a server rule
 * exactly; the two agreeing is the whole value of stating them twice, so a change on either
 * side belongs on both.
 *
 * Each returns the message to show, or null when the payload would be accepted.
 */

function wholeNumberFrom(anchor: unknown, key: string, minimum: number): number | null {
  const value = readAnchorNumber(anchor, key);
  return value !== null && Number.isInteger(value) && value >= minimum ? value : null;
}

function secondsFrom(anchor: unknown, key: string): number | null {
  const value = readAnchorNumber(anchor, key);
  return value !== null && value >= 0 ? value : null;
}

export function validatePageAnchor(anchor: unknown, t: TFunction): string | null {
  return wholeNumberFrom(anchor, 'page', 1) === null
    ? t('resLinks.anchorEditors.pageRequired')
    : null;
}

export function validatePageRangeAnchor(anchor: unknown, t: TFunction): string | null {
  const from = wholeNumberFrom(anchor, 'fromPage', 1);
  const to = wholeNumberFrom(anchor, 'toPage', 1);
  if (from === null || to === null) {
    return t('resLinks.anchorEditors.pageRangeRequired');
  }
  // A range of one page is a legitimate thing to point at; only a backwards one is not.
  return to < from ? t('resLinks.anchorEditors.pageRangeBackwards') : null;
}

export function validateTimePointAnchor(anchor: unknown, t: TFunction): string | null {
  return secondsFrom(anchor, 't') === null ? t('resLinks.anchorEditors.timeRequired') : null;
}

export function validateTimeRangeAnchor(anchor: unknown, t: TFunction): string | null {
  const start = secondsFrom(anchor, 'start');
  const end = secondsFrom(anchor, 'end');
  if (start === null || end === null) {
    return t('resLinks.anchorEditors.timeRangeRequired');
  }
  // Strictly forward: a span that ends where it starts points at a moment, and a moment
  // has its own anchor kind — the server refuses the degenerate span for that reason.
  return end <= start ? t('resLinks.anchorEditors.timeRangeBackwards') : null;
}
