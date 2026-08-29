// SPDX-License-Identifier: AGPL-3.0-or-later
import dayjs from 'dayjs';
import localeData from 'dayjs/plugin/localeData';
import { describe, expect, it } from 'vitest';
import i18n from './index.ts';

// The date-picker adapter extends dayjs with this globally; extended here too so the assertions
// below do not depend on a picker having been rendered first.
dayjs.extend(localeData);

/**
 * The weekday initials, the month names and the day a week starts on that every date picker
 * draws come from dayjs rather than from antd's locale bundle. dayjs holds only English until a
 * locale is imported, and asking it for one it does not hold returns English silently — so the
 * failure this guards against produces a Sunday-first English calendar under a Romanian
 * interface, with nothing logged and no test failing anywhere else.
 */
describe('the dates a Romanian reader is shown', () => {
  it('names its days and months in Romanian', () => {
    const monday = dayjs('2032-03-15').locale('ro');

    expect(monday.format('dddd')).toBe('Luni');
    expect(monday.format('MMMM')).toBe('Martie');
  });

  it('starts the week on Monday', () => {
    expect(dayjs().locale('ro').localeData().firstDayOfWeek()).toBe(1);
    expect(dayjs().locale('en').localeData().firstDayOfWeek()).toBe(0);
  });

  it('follows the language the interface settled on', async () => {
    await i18n.changeLanguage('ro');
    expect(dayjs.locale()).toBe('ro');

    await i18n.changeLanguage('en');
    expect(dayjs.locale()).toBe('en');
  });
});
