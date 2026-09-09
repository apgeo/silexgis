// SPDX-License-Identifier: AGPL-3.0-or-later
import type { RegistryCorrelation } from '../../api/hooks.ts';

/**
 * Turning a fitted relationship into the two ends of a line, and — more often than not — into the
 * reason there is no line to draw.
 *
 * <p>
 * The registry answers this question with the relationship rather than with the caves behind it:
 * a slope, an intercept, a goodness figure and the number of pairs they were taken over. Nothing
 * here fits anything. What it decides is narrower and worth asserting on its own: whether the
 * answer supports drawing a line at all, and if it does not, which of the several different
 * reasons applies. They are genuinely different situations — no pair recorded both measurements,
 * or pairs exist but too few for a line through them to mean anything, or the measurement covers
 * no range to draw across — and a screen that showed one blank chart for all of them would be
 * saying the same thing about three different states of the registry.
 * </p>
 * <p>
 * A fitted line drawn with no observations under it is a strong-looking picture, so the two ends
 * come from the range the horizontal measurement actually covers in the same set, never from an
 * axis invented to make the line look long.
 * </p>
 */

/** Why no line is drawn. Absent when one is. */
export type CorrelationAbsence =
  /** No cave in this set records both measurements. */
  | 'noPairs'
  /** Pairs, but fewer than two: a line through one point is not a relationship. */
  | 'notFitted'
  /** The horizontal measurement covers no range here, so there is nothing to draw across. */
  | 'noRange'
  /**
   * The range is not known yet. Said apart from {@link 'noRange'} on purpose: the range comes from
   * a second question asked of the registry, and while it is unanswered nothing at all is known
   * about it. Telling a reader the measurement covers no range would be a statement about the
   * caves made out of a request that has not come back.
   */
  | 'rangeUnknown'
  /** The range could not be read at all. A failure to fetch, and again not a fact about caves. */
  | 'rangeUnavailable'
  /**
   * The fit is over logarithms and the horizontal range reaches zero or below, so it has no lower
   * end on a logarithmic axis. The figures still stand; only the drawing does not.
   */
  | 'noPositiveRange';

/**
 * Where the horizontal measurement starts and ends across the same set of caves.
 *
 * <p>
 * The two ends and <em>whether they are known at all</em> are carried together, because a caller
 * holding neither end has to say which of the two situations it is in. They arrive from a separate
 * question asked of the registry, which answers later than the fit itself and can fail on its own;
 * a pair of nulls standing for both "not back yet" and "no range in this set" turns the ordinary
 * first paint into a claim that the registry records no spread in this measurement.
 * </p>
 */
export type CorrelationDomain =
  /** The ends as the registry gave them. Either may still be null, which is a real no-range. */
  | { state: 'answered'; minimum: number | null; maximum: number | null }
  /** The question has not come back yet. */
  | { state: 'pending' }
  /** The question came back a refusal or an error. */
  | { state: 'unavailable' };

/** The two ends of the fitted line, in the measurements' own units. */
export type CorrelationLine = readonly [readonly [number, number], readonly [number, number]];

/** The value the fit predicts, in the vertical measurement's own units. */
function predict(fit: RegistryCorrelation, x: number): number {
  const slope = fit.slope ?? 0;
  const intercept = fit.intercept ?? 0;
  // The logarithmic form is fitted through the natural logarithms of both measurements, so it is
  // read back out through the exponential rather than plotted as a straight line in linear units,
  // where it is a curve and a straight line through it would be a different claim.
  return fit.logarithmic ? Math.exp(intercept + slope * Math.log(x)) : intercept + slope * x;
}

/**
 * The line the fit describes, or the reason there is none.
 *
 * Returns exactly one of the two, so a caller cannot draw a line and state an absence at the same
 * time, and cannot silently draw nothing while saying nothing either.
 */
export function correlationLine(
  fit: RegistryCorrelation | undefined,
  domain: CorrelationDomain,
): { line: CorrelationLine; absence: null } | { line: null; absence: CorrelationAbsence } {
  if (fit === undefined || fit.count === 0) {
    return { line: null, absence: 'noPairs' };
  }
  if (fit.slope === null || fit.intercept === null) {
    return { line: null, absence: 'notFitted' };
  }
  // Checked after the fit and before the range: whether a fit exists at all is known from the
  // answer already in hand, and is the more informative thing to say when both are true.
  if (domain.state === 'pending') {
    return { line: null, absence: 'rangeUnknown' };
  }
  if (domain.state === 'unavailable') {
    return { line: null, absence: 'rangeUnavailable' };
  }
  const { minimum, maximum } = domain;
  if (minimum === null || maximum === null || !(minimum < maximum)) {
    return { line: null, absence: 'noRange' };
  }
  if (fit.logarithmic && !(minimum > 0)) {
    return { line: null, absence: 'noPositiveRange' };
  }
  const left = predict(fit, minimum);
  const right = predict(fit, maximum);
  if (!Number.isFinite(left) || !Number.isFinite(right)) {
    return { line: null, absence: 'noRange' };
  }
  return { line: [[minimum, left], [maximum, right]], absence: null };
}
