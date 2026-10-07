// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { modelDeliveryIdentity } from './modelDelivery.ts';

describe('modelDeliveryIdentity', () => {
  it('is the same for one file signed twice, and differs for another file', () => {
    const first = '/api/v1/files/0190aaaa/content?token=one';
    const signedAgain = '/api/v1/files/0190aaaa/content?token=two';
    const another = '/api/v1/files/0190bbbb/content?token=one';

    expect(modelDeliveryIdentity(signedAgain)).toBe(modelDeliveryIdentity(first));
    expect(modelDeliveryIdentity(another)).not.toBe(modelDeliveryIdentity(first));
  });

  it('is the whole address where nothing is signed onto it', () => {
    expect(modelDeliveryIdentity('http://files.local/survey')).toBe('http://files.local/survey');
  });
});
