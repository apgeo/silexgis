// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';

const attach = vi.fn();

/** What the trip's gallery answers with. Two pictures with their own instants, one without. */
let photos: {
  documentId: string;
  title: string;
  takenAt: string | null;
}[] = [];

vi.mock('../../api/hooks.ts', () => ({
  usePhotos: () => ({ data: { items: photos }, isPending: false }),
  useAttachTrackingPictures: () => ({ mutate: attach, isPending: false }),
}));

const { default: TrackingPicturesDialog } = await import('./TrackingPicturesDialog.tsx');

const CAVERS = [
  { caverId: 'caver-1', name: 'Ana' },
  { caverId: 'caver-2', name: 'Bogdan' },
];

/** The moment the dialog is opened at — a report's own hour, or the scrubber's. */
const OPENED_AT = Date.parse('2026-09-12T16:00:00Z');

function show(defaultCaverId: string | null = 'caver-1') {
  return render(
    <App>
      <TrackingPicturesDialog
        open
        tripLogId="trip-1"
        defaultAt={OPENED_AT}
        defaultCaverId={defaultCaverId}
        cavers={CAVERS}
        onClose={vi.fn()}
      />
    </App>,
  );
}

/**
 * Ticks one photograph in the chooser. antd puts the test id on whichever of the label and the
 * input it forwards rest props to, so the box is found either way rather than by assuming.
 */
function pick(documentId: string) {
  const node = screen.getByTestId(`trip-tracking-pictures-pick-${documentId}`);
  const box = node instanceof HTMLInputElement ? node : node.querySelector('input');
  fireEvent.click(box ?? node);
}

async function save() {
  await act(async () => {
    fireEvent.click(screen.getByRole('button', { name: /Attach/ }));
  });
}

/** What the last write would have sent, per photograph. */
function sent() {
  return (attach.mock.calls.at(-1)![0] as { items: { documentId: string; at: string; caverId: string | null }[] })
    .items;
}

beforeEach(() => {
  attach.mockReset();
  photos = [
    { documentId: 'doc-1', title: 'Pitch head', takenAt: '2026-09-12T09:05:00Z' },
    { documentId: 'doc-2', title: 'Sump', takenAt: '2026-09-12T10:40:00Z' },
    { documentId: 'doc-3', title: 'Scanned print', takenAt: null },
  ];
});

afterEach(cleanup);

describe('TrackingPicturesDialog', () => {
  it('files each photograph at the moment its own file says it was taken', () => {
    show();
    pick('doc-1');
    pick('doc-2');
    void save();

    expect(sent().map((item) => item.at)).toEqual([
      '2026-09-12T09:05:00.000Z',
      '2026-09-12T10:40:00.000Z',
    ]);
    // Who the moment is about travels with it, so the replay knows whose position to draw it at.
    expect(sent().every((item) => item.caverId === 'caver-1')).toBe(true);
  });

  it('corrects every prefilled moment by the camera-clock offset, stated once', async () => {
    show();
    pick('doc-1');
    pick('doc-2');

    // "The camera was 37 minutes fast" — taken off what the clock said, for all of them at once,
    // which is the work this dialog exists to remove.
    await act(async () => {
      const node = screen.getByTestId('trip-tracking-pictures-offset');
      const box = node instanceof HTMLInputElement ? node : node.querySelector('input')!;
      fireEvent.change(box, { target: { value: '37' } });
    });
    await save();

    expect(sent().map((item) => item.at)).toEqual([
      '2026-09-12T08:28:00.000Z',
      '2026-09-12T10:03:00.000Z',
    ]);
  });

  it('flags a photograph whose file says nothing about when it was taken, and never the ones that do', async () => {
    show();
    pick('doc-1');
    // Its twin: with only dated pictures chosen there is no warning at all, so the one below is a
    // decision about the picture rather than a box the dialog always shows.
    expect(screen.queryByTestId('trip-tracking-pictures-guessed')).toBeNull();

    pick('doc-3');
    expect(screen.getByTestId('trip-tracking-pictures-guessed')).toBeInTheDocument();

    await save();
    // The undated one falls back to the moment the dialog was opened with — offered, flagged, and
    // never applied silently. The offset is a correction to a camera's clock and is not applied to
    // a moment that never came off one.
    expect(sent().map((item) => item.at)).toEqual([
      '2026-09-12T09:05:00.000Z',
      new Date(OPENED_AT).toISOString(),
    ]);
  });

  /**
   * A camera whose battery died reports 1970 or 2000 — a clock that was never set rather than one
   * set wrongly. The replay refuses to stretch its scrubber around such a moment, so nothing is
   * destroyed by attaching one; what is left is a photograph filed where nobody meant, and this is
   * the surface that can say so before it is stored. It says it and does not refuse: the moment can
   * be right, and throwing away the record of a photograph over a number somebody can correct here
   * would be the worse trade.
   */
  it('remarks on a moment far outside the trip, and never on the ordinary wrong hour', async () => {
    photos = [
      { documentId: 'doc-1', title: 'Pitch head', takenAt: '2026-09-12T09:05:00Z' },
      { documentId: 'doc-dead', title: 'Dead battery', takenAt: '2000-01-01T00:00:00Z' },
    ];
    show();

    // The twin, and it is the important half: a clock out by hours — a time zone, a date rolled
    // over at midnight, a camp that ran a week — is the ordinary case and passes without a word.
    pick('doc-1');
    expect(screen.queryByTestId('trip-tracking-pictures-stray')).toBeNull();

    pick('doc-dead');
    expect(screen.getByTestId('trip-tracking-pictures-stray')).toBeInTheDocument();

    // Remarked on, not refused: both are still sent, at the moments their files claim.
    await save();
    expect(sent().map((item) => item.at)).toEqual([
      '2026-09-12T09:05:00.000Z',
      '2000-01-01T00:00:00.000Z',
    ]);
  });

  it('sends no subject for a picture of the party, and nothing at all with nothing chosen', async () => {
    show(null);
    pick('doc-1');
    await save();
    expect(sent()[0].caverId).toBeNull();

    attach.mockReset();
    cleanup();
    show();
    // Nothing chosen is nothing to send: the button is refused rather than writing an empty batch.
    expect(screen.getByRole('button', { name: /Attach/ })).toBeDisabled();
  });
});
