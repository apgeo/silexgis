// SPDX-License-Identifier: AGPL-3.0-or-later
import { cloneElement, isValidElement, type ReactElement } from 'react';
import type { PaginationProps } from 'antd';
import type { TFunction } from 'i18next';

const LABELS: Partial<Record<string, string>> = {
  prev: 'a11y.previousPage',
  next: 'a11y.nextPage',
  'jump-prev': 'a11y.previousPages',
  'jump-next': 'a11y.nextPages',
};

/**
 * Pagination items that say what they do.
 *
 * The library draws its arrows as buttons whose only content is an arrow icon, so each is named by
 * the icon's English glyph name — "left", "right", "double-left" — in either language. A page
 * number is named by its number and is left alone. Hand this to a table's or a pagination's
 * `itemRender`.
 */
export function labelledPaginationItems(t: TFunction): NonNullable<PaginationProps['itemRender']> {
  return (_page, type, element) => {
    const labelKey = LABELS[type];
    return labelKey && isValidElement(element)
      ? cloneElement(element as ReactElement<{ 'aria-label'?: string }>, { 'aria-label': t(labelKey) })
      : element;
  };
}
