// SPDX-License-Identifier: AGPL-3.0-or-later
import type { RegistryClustering } from '../../api/hooks.ts';

export type Projection = NonNullable<RegistryClustering['projection']>;

/** A share as a whole percentage, which is as much precision as an explained share deserves. */
export function sharePercent(share: number): number {
  return Math.round(share * 100);
}

/** The axis title the chart carries — the number of the axis and how much of the spread it holds. */
export function axisTitle(
  t: (key: string, options?: Record<string, unknown>) => string,
  axis: 1 | 2,
  projection: Projection,
): string {
  const share = axis === 1 ? projection.first.explainedShare : projection.second.explainedShare;
  return t('karstStats.axisTitle', { axis, share: sharePercent(share) });
}
