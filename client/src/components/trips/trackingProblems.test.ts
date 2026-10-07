// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';
import {
  TRACKING_PROBLEM_MESSAGE_KEYS,
  trackingProblemCodeMessage,
  trackingProblemMessage,
} from './trackingProblems.ts';

const t = i18n.t.bind(i18n);

/**
 * The refusals about photographs on a trip's moments, each with a few words only its own sentence
 * holds. Written out rather than read back from the translation file, so that a sentence swapped
 * with its neighbour — or a code pointed at somebody else's key — fails here instead of telling a
 * reader to correct a camera's clock when the file they chose was a document.
 */
const PICTURE_REFUSALS: [code: string, says: RegExp][] = [
  ['tracking.not_tracked', /never been started for this trip.*no moments to hang a photograph on/],
  ['tracking.picture_in_future', /moment that has not happened yet.*camera's clock/],
  ['tracking.picture_not_image', /chosen files is not a picture/],
  ['tracking.picture_already_attached', /already on that moment of the trip/],
  ['tracking.picture_relation_missing', /installation is missing the kind of link/],
  ['tracking.picture_not_found', /no longer on this moment/],
];

describe('the wording of a tracking refusal', () => {
  it.each(PICTURE_REFUSALS)('%s has a sentence of its own', (code, says) => {
    const general = 'The photographs could not be attached.';
    const sentence = trackingProblemMessage(new ApiError(409, code), t, general);
    expect(sentence).toMatch(says);
    expect(sentence).not.toBe(general);
    // The same sentence whichever way the code arrived: as a refused request, or as the reason one
    // photograph was left out of an answer that took the others.
    expect(trackingProblemCodeMessage(code, t)).toBe(sentence);
    // And no other code is given it.
    const others = Object.keys(TRACKING_PROBLEM_MESSAGE_KEYS).filter((other) => other !== code);
    expect(others.map((other) => trackingProblemCodeMessage(other, t))).not.toContain(sentence);
  });

  it('falls back to the sentence its caller chose, and to the saving one when none was', () => {
    const unknown = new ApiError(409, 'tracking.something_nobody_has_worded');
    expect(trackingProblemMessage(unknown, t, 'Chosen by the caller.')).toBe('Chosen by the caller.');
    expect(trackingProblemMessage(unknown, t)).toBe(t('common.saveFailed'));
    // A failure that is not a refusal at all — the network, a thrown bug — and a refusal with no code.
    expect(trackingProblemMessage(new Error('offline'), t, 'Chosen by the caller.')).toBe('Chosen by the caller.');
    expect(trackingProblemMessage(new ApiError(500), t, 'Chosen by the caller.')).toBe('Chosen by the caller.');
    // Its twin: a known code is never replaced by the caller's general sentence.
    expect(trackingProblemMessage(new ApiError(404, 'tracking.team_not_found'), t, 'Chosen by the caller.')).toBe(
      t('trips.tracking.problems.teamNotFound'),
    );
  });

  it('has nothing to say about a bare code it does not know, rather than something general', () => {
    expect(trackingProblemCodeMessage('tracking.something_nobody_has_worded', t)).toBeUndefined();
    expect(trackingProblemCodeMessage(null, t)).toBeUndefined();
    expect(trackingProblemCodeMessage('', t)).toBeUndefined();
    // The code is the server's text and the table a plain object: a name every object answers to
    // must not be mistaken for a row of it.
    expect(trackingProblemCodeMessage('constructor', t)).toBeUndefined();
    expect(trackingProblemCodeMessage('toString', t)).toBeUndefined();
  });
});
