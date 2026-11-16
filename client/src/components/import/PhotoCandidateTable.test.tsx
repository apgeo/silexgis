// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { PhotoCandidate, PhotoDecision } from '../../api/hooks.ts';

vi.mock('../../api/hooks.ts', () => ({
  useCaveTypes: () => ({ data: [] }),
  useEntranceTypes: () => ({ data: [] }),
  useGenericFeatureTypes: () => ({ data: [] }),
}));

const { default: PhotoCandidateTable } = await import('./PhotoCandidateTable.tsx');

const place: PhotoCandidate = {
  key: 'file-1',
  geom: { type: 'Point', coordinates: [25.51, 45.61] },
  positionSource: 'exif',
  members: [
    {
      fileId: 'file-1',
      originalName: 'IMG_0001.jpg',
      mimeType: 'image/jpeg',
      kind: 'image',
      capturedAt: '2026-08-08T12:00:00Z',
      positionSource: 'exif',
      altitudeMeters: null,
      directionDegrees: null,
      directionIsMagnetic: false,
      dop: null,
      confidence: 'unknown',
      hasOwnPosition: true,
      thumbnailUrl: null,
    },
  ],
  proposedName: 'Sample place',
  altitudeMeters: null,
  directionDegrees: null,
  directionIsMagnetic: false,
  dop: null,
  confidence: 'unknown',
  trackMatchSecondsFromFix: null,
  trackMatchInterpolated: false,
  nearby: [],
  decision: null,
};

/** The page's part: it keeps the decisions and hands them back, as the workspace does. */
function Review({ onDecision }: { onDecision: (decision: PhotoDecision | null) => void }) {
  const [decisions, setDecisions] = useState<Record<string, PhotoDecision>>({});
  return (
    <PhotoCandidateTable
      items={[place]}
      defaultKind="caveEntrance"
      decisions={decisions}
      selected={new Set()}
      selectableKeys={['file-1']}
      onSelectedChange={vi.fn()}
      onDecision={(key, decision) => {
        onDecision(decision);
        setDecisions((current) => {
          const next = { ...current };
          if (decision === null) delete next[key];
          else next[key] = decision;
          return next;
        });
      }}
      onFocus={vi.fn()}
      page={1}
      pageSize={25}
      total={1}
      onPageChange={vi.fn()}
    />
  );
}

afterEach(cleanup);

describe("a place's name in the photo review", () => {
  it('can be emptied and typed afresh without the proposal coming back under the cursor', () => {
    const onDecision = vi.fn();
    render(<Review onDecision={onDecision} />);
    const input = screen.getByDisplayValue<HTMLInputElement>('Sample place');

    fireEvent.change(input, { target: { value: '' } });
    expect(input.value).toBe('');

    fireEvent.change(input, { target: { value: 'Izbuc' } });
    fireEvent.blur(input);

    expect(input.value).toBe('Izbuc');
    expect(onDecision).toHaveBeenLastCalledWith(expect.objectContaining({ name: 'Izbuc' }));
  });

  it('goes back to the proposal when left empty, since a place is never created without a name', () => {
    const onDecision = vi.fn();
    render(<Review onDecision={onDecision} />);
    const input = screen.getByDisplayValue<HTMLInputElement>('Sample place');

    fireEvent.change(input, { target: { value: 'Izbuc' } });
    fireEvent.blur(input);
    fireEvent.change(input, { target: { value: '' } });
    fireEvent.blur(input);

    expect(onDecision).toHaveBeenLastCalledWith(expect.objectContaining({ name: null }));
    expect(input.value).toBe('Sample place');
  });
});
