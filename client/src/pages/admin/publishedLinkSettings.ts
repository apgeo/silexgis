// SPDX-License-Identifier: AGPL-3.0-or-later

/** The unit a period set by an installation is said in. */
export type SettingSpanUnit = 'days' | 'hours' | 'minutes' | 'seconds';

/** A period as the number and the unit it is printed with. */
export interface SettingSpan {
  unit: SettingSpanUnit;
  amount: number;
}

const UNITS: readonly (readonly [SettingSpanUnit, number])[] = [
  ['days', 86_400],
  ['hours', 3_600],
  ['minutes', 60],
];

/**
 * A period of whole seconds in the largest unit that holds it exactly.
 *
 * Exactly, because the number printed is a setting somebody typed and will look for in their
 * configuration: thirty-six hours said as "1.5 days" or rounded to "2 days" is a figure the
 * server is not applying. So two weeks is fourteen days, a day and a half is thirty-six hours,
 * ninety seconds is ninety seconds — and nothing is said as zero of a larger unit.
 */
export function settingSpanOf(seconds: number): SettingSpan {
  for (const [unit, size] of UNITS) {
    if (seconds >= size && seconds % size === 0) {
      return { unit, amount: seconds / size };
    }
  }
  return { unit: 'seconds', amount: seconds };
}
