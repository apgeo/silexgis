// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { accuracyFormValues, accuracyRequest, describeAccuracy } from './terrainAccuracy.ts';

const t = (key: string, options?: Record<string, unknown>) =>
  `${key}:${JSON.stringify(options ?? {})}`;

describe('accuracyRequest', () => {
  it('sends nothing when nothing was stated, so the server may fill in a dataset s own figures', () => {
    expect(accuracyRequest({ resolutionM: null, accuracyM: null, accuracyStatistic: 'RMSE', accuracySource: '  ' })).toBeUndefined();
  });

  it('keeps the statistic only alongside a figure it describes', () => {
    expect(accuracyRequest({ resolutionM: 1, accuracyM: null, accuracyStatistic: 'RMSE', accuracySource: '' })).toEqual({
      sourceResolutionM: 1,
      verticalAccuracyM: null,
      verticalAccuracyStatistic: null,
      accuracySource: null,
    });
    expect(accuracyRequest({ resolutionM: null, accuracyM: 0.3, accuracyStatistic: 'LE90', accuracySource: ' report ' })).toEqual({
      sourceResolutionM: null,
      verticalAccuracyM: 0.3,
      verticalAccuracyStatistic: 'LE90',
      accuracySource: 'report',
    });
  });
});

describe('describeAccuracy', () => {
  it('says nothing for a build with no record rather than a zero', () => {
    expect(describeAccuracy(null, t)).toBeNull();
    expect(describeAccuracy({ sourceResolutionM: null, verticalAccuracyM: null, verticalAccuracyStatistic: null, accuracySource: 'x' }, t)).toBeNull();
  });

  it('names the statistic beside the error', () => {
    const line = describeAccuracy({ sourceResolutionM: 30, verticalAccuracyM: 4, verticalAccuracyStatistic: 'LE90', accuracySource: null }, t);
    expect(line).toContain('"statistic":"LE90"');
    expect(line).toContain('"value":30');
  });
});

describe('accuracyFormValues', () => {
  it('starts an empty editor on a statistic that needs no change for the commonest vendor figure', () => {
    expect(accuracyFormValues(null).accuracyStatistic).toBe('RMSE');
  });
});
