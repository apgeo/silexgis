// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  publicSurveyViewerPath,
  surveyUploadGeodataUrl,
  surveyUploadViewerPath,
  viewerModelOf,
} from './surveyUploadViewer.ts';

/** The rules the page of an upload's results is built from: its addresses and the model it draws. */
describe('the page of results', () => {
  it('sits under the upload\'s own page and under its public link', () => {
    expect(surveyUploadViewerPath('/caves/c/surveys/s/uploads/u')).toBe('/caves/c/surveys/s/uploads/u/view');
    expect(surveyUploadViewerPath('/caves/c/surveys/s/uploads/u/')).toBe('/caves/c/surveys/s/uploads/u/view');
    expect(publicSurveyViewerPath('/p/survey/tok')).toBe('/p/survey/tok/view');
  });

  it('downloads the line work in the format asked for', () => {
    expect(surveyUploadGeodataUrl('u-1', 'kml')).toBe('/api/v1/survey-uploads/u-1/geodata?format=kml');
  });
});

describe('the model the viewers draw', () => {
  it('is the compiled 3D file before the Survex one, named without its extension', () => {
    const model = viewerModelOf([
      { kind: '3d', name: 'cave-20261025T101500Z.3d', sizeBytes: 10, contentUrl: '/f/3d' },
      { kind: 'lox', name: 'cave-20261025T101500Z.lox', sizeBytes: 10, contentUrl: '/f/lox' },
      { kind: 'plan-pdf', name: 'plan.pdf', sizeBytes: 10, contentUrl: '/f/pdf' },
    ]);
    expect(model).toEqual({ modelUrl: '/f/lox', name: 'cave-20261025T101500Z', format: 'lox' });
  });

  it('falls back to the Survex file, and is nothing without a file that can be fetched', () => {
    expect(viewerModelOf([{ kind: '3d', name: 'c.3d', sizeBytes: 1, contentUrl: '/f/3d' }])).toEqual({
      modelUrl: '/f/3d',
      name: 'c',
      format: 'survex3d',
    });
    expect(viewerModelOf([{ kind: 'lox', name: 'c.lox', sizeBytes: null, contentUrl: null }])).toBeNull();
    expect(viewerModelOf([{ kind: 'plan-pdf', name: 'p.pdf', sizeBytes: 1, contentUrl: '/f' }])).toBeNull();
  });
});
