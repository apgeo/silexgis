// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it, vi } from 'vitest';
import type { SurveyModelInfo } from './hooks.ts';

// Nothing here goes near the network; the transport is stubbed only so the module can be imported.
vi.mock('./client.ts', () => ({
  api: {},
  ApiError: class ApiError extends Error {},
  lastReadETag: () => undefined,
}));

const { surveyModelCanPlaceACaver, surveyModelPlacingObstacle } = await import('./hooks.ts');

const STATUSES: SurveyModelInfo['status'][] = ['ready', 'pending', 'processing', 'failed'];
const FORMATS: SurveyModelInfo['format'][] = ['lox', 'survex3d', 'stl'];

/**
 * A survey holds no stations for three different reasons, and the act that answers each is
 * different: wait, import again, or choose something else entirely. Told apart wrongly, a surface
 * sends the owner of a wall mesh to re-import a wall mesh for ever, or tells somebody an import
 * failed while the import is running.
 */
describe('why a survey cannot place a caver', () => {
  it('finds no obstacle at all on a line plot that was read right through', () => {
    expect(surveyModelPlacingObstacle({ status: 'ready', format: 'survex3d' })).toBeNull();
    expect(surveyModelPlacingObstacle({ status: 'ready', format: 'lox' })).toBeNull();
  });

  it('calls a line plot whose reading failed a failed import', () => {
    expect(surveyModelPlacingObstacle({ status: 'failed', format: 'survex3d' })).toBe('importFailed');
  });

  it('calls a reading still queued or running one that is still being read', () => {
    expect(surveyModelPlacingObstacle({ status: 'pending', format: 'lox' })).toBe('stillReading');
    expect(surveyModelPlacingObstacle({ status: 'processing', format: 'lox' })).toBe('stillReading');
  });

  it('calls a wall mesh a wall mesh, however well its conversion went', () => {
    expect(surveyModelPlacingObstacle({ status: 'ready', format: 'stl' })).toBe('notALinePlot');
  });

  /**
   * The ordering rule, and the case that proves it is a rule rather than an accident of writing:
   * a wall mesh whose conversion failed is still a wall mesh. "Import it again" is advice that
   * cannot work — re-reading a mesh yields a picture and never a station — so the permanent half
   * of the answer has to be the one that is given.
   */
  it('answers a failed wall mesh by what it is, not by what happened to it', () => {
    expect(surveyModelPlacingObstacle({ status: 'failed', format: 'stl' })).toBe('notALinePlot');
  });

  /**
   * The two questions this module publishes about one model — may anybody be placed on it, and if
   * not why not — must never disagree about a single survey. A chooser that offered a model the
   * card beside it then explained was unusable is the exact failure this pair exists to prevent,
   * so the agreement is asserted over every status and format the contract allows rather than over
   * the handful a test happened to think of.
   */
  it('never disagrees with the question the chooser narrows by', () => {
    for (const status of STATUSES) {
      for (const format of FORMATS) {
        expect({ status, format, can: surveyModelCanPlaceACaver({ status, format }) }).toEqual({
          status,
          format,
          can: surveyModelPlacingObstacle({ status, format }) === null,
        });
      }
    }
  });

  // And the twin that keeps the agreement above from being satisfied by two functions that both
  // say no to everything: exactly two of the twelve combinations can carry a position.
  it('leaves exactly the read-through line plots placeable', () => {
    const placeable = STATUSES.flatMap((status) =>
      FORMATS.filter((format) => surveyModelCanPlaceACaver({ status, format })).map(
        (format) => `${status}/${format}`,
      ),
    );
    expect(placeable).toEqual(['ready/lox', 'ready/survex3d']);
  });
});
