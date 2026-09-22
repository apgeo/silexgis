// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { App } from 'antd';
import { useEffect } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { ResLink, SurveyModelInfo } from '../../api/hooks.ts';
import type { PickedModelPart } from '../../caveview/modelParts.ts';
import SurveyModelViewerModal from './SurveyModelViewerModal.tsx';

/**
 * What this file proves is one sentence in the design of the map tabs: <b>the 3D viewer is
 * created once and never remounts on a tab switch.</b> A remount is not cosmetic — the real
 * panel re-fetches and re-parses the whole survey and burns a WebGL context doing it — so the
 * panel is faked with a mount counter and the assertions are about the counter, not the scene.
 */
const panelLifecycle = { mounts: 0, unmounts: 0 };

vi.mock('./CaveViewPanel.tsx', () => ({
  default: function FakeCaveViewPanel({
    onPartPick,
    onStationsLoaded,
  }: {
    onPartPick?: (part: PickedModelPart) => void;
    onStationsLoaded?: (stations: readonly string[]) => void;
  }) {
    useEffect(() => {
      panelLifecycle.mounts += 1;
      return () => {
        panelLifecycle.unmounts += 1;
      };
    }, []);
    // The parsed drawing announcing its stations, as the real panel does on load.
    useEffect(() => {
      onStationsLoaded?.(['p.g.7', 'p.g.8', 'cave.deep.3']);
    }, [onStationsLoaded]);
    return (
      <div data-testid="fake-caveview">
        the 3D scene
        <button
          data-testid="fake-press-station"
          onClick={() =>
            onPartPick?.({ anchorKind: 'modelStation', anchor: { station: 'p.g.7' }, label: 'p.g.7' })
          }
        />
        <button
          data-testid="fake-press-survey"
          onClick={() =>
            onPartPick?.({ anchorKind: 'modelSurvey', anchor: { survey: 'p.g' }, label: 'p.g' })
          }
        />
      </div>
    );
  },
}));

vi.mock('../../rastermap/RasterMapPane.tsx', () => ({
  default: function FakeRasterMapPane({
    declaration,
    active,
    authoring,
  }: {
    declaration: { linkId: string };
    active: boolean;
    authoring?: { defining: boolean; onToggleDefining: () => void };
  }) {
    return (
      <div
        data-testid={`fake-map-pane-${declaration.linkId}`}
        data-active={String(active)}
        data-defining={String(authoring?.defining ?? false)}
      >
        <button
          data-testid={`fake-toggle-define-${declaration.linkId}`}
          onClick={() => authoring?.onToggleDefining()}
        />
      </div>
    );
  },
}));

// Mounted the moment anything is picked (open or not), and it reads the server — so it
// is faked here like the panes: its own tests hold its behavior.
vi.mock('../reslinks/AddMemberModal.tsx', () => ({
  default: function FakeAddMemberModal({ open }: { open: boolean }) {
    return open ? <div data-testid="fake-add-member" /> : null;
  },
}));

// A separate unit with its own tests and its own server reads; here only its doorway.
vi.mock('../../rastermap/DeclareMapModal.tsx', () => ({
  default: function FakeDeclareMapModal({ open, editing }: { open: boolean; editing?: unknown }) {
    return open ? <div data-testid={editing ? 'fake-edit-map' : 'fake-declare-map'} /> : null;
  },
}));

// The link read is the modal's own business; here it is a dial the tests turn.
let linksAnswer: ResLink[] | undefined;
vi.mock('../../rastermap/useRasterMapLinks.ts', () => ({
  useRasterMapLinks: () => ({ data: linksAnswer }),
}));

vi.mock('../../caveview/useStationMedia.ts', () => ({
  useStationMedia: () => new Map(),
}));

const MODEL = {
  id: 'model-1',
  name: 'P8_Master',
  format: '3d',
  modelUrl: 'http://files.local/model',
  status: 'ready',
} as unknown as SurveyModelInfo;

/** A declared map of this model, as the fold will read it. */
function mapLink(linkId: string, title: string, code = 'map-plan-of'): ResLink {
  return {
    id: linkId,
    shortCode: 'ABCD1234',
    relationType: { id: 1, code, name: code, directed: true, inverseName: 'x' },
    description: null,
    createdAt: '2026-09-01T10:00:00Z',
    updatedAt: '2026-09-01T10:00:00Z',
    mayEdit: true,
    members: [
      {
        id: `${linkId}-doc`,
        targetType: 'document',
        targetId: `${linkId}-document`,
        isMain: true,
        sortOrder: 0,
        note: null,
        anchorKind: 'whole',
        anchor: null,
        anchorFileId: null,
        anchorState: 'exact',
        display: {
          title,
          subtitle: null,
          route: null,
          thumbnailUrl: 'http://files.local/thumb?token=abc',
          mediaType: 'image/png',
        },
      },
      {
        id: `${linkId}-model`,
        targetType: 'surveyModel',
        targetId: MODEL.id,
        isMain: false,
        sortOrder: 1,
        note: null,
        anchorKind: 'whole',
        anchor: null,
        anchorFileId: null,
        anchorState: 'exact',
        display: null,
      },
    ],
  } as unknown as ResLink;
}

beforeEach(() => {
  panelLifecycle.mounts = 0;
  panelLifecycle.unmounts = 0;
  linksAnswer = undefined;
});

afterEach(() => {
  cleanup();
});

function show(model: SurveyModelInfo | null = MODEL) {
  return render(
    <App>
      <SurveyModelViewerModal model={model} onClose={vi.fn()} />
    </App>,
  );
}

describe('SurveyModelViewerModal', () => {
  it('shows the strip with only the 3D tab and the add-map doorway while no maps are declared', () => {
    linksAnswer = [];
    show();

    expect(screen.getByTestId('fake-caveview')).toBeInTheDocument();
    // The strip now always shows: with no maps it still carries the one action that gets
    // a model its first map — declaring one needs only what being here proves (a
    // signed-in reader of the model), which is the same floor the server's link create
    // asks for.
    expect(screen.getByRole('tab', { name: '3D' })).toBeInTheDocument();
    expect(screen.getAllByRole('tab')).toHaveLength(1);
    expect(screen.getByTestId('rastermap-add-map')).toBeInTheDocument();
    expect(screen.queryByTestId('fake-declare-map')).not.toBeInTheDocument();

    fireEvent.click(screen.getByTestId('rastermap-add-map'));
    expect(screen.getByTestId('fake-declare-map')).toBeInTheDocument();
  });

  it('keeps the viewer mounted, not merely re-created, when the declarations arrive', () => {
    linksAnswer = undefined;
    const view = show();
    expect(panelLifecycle.mounts).toBe(1);

    // The link read lands and two tabs appear beside the 3D one…
    linksAnswer = [mapLink('link-a', 'Plan sheet'), mapLink('link-b', 'Profile sheet', 'map-profile-of')];
    view.rerender(
      <App>
        <SurveyModelViewerModal model={MODEL} onClose={vi.fn()} />
      </App>,
    );

    expect(screen.getByRole('tab', { name: /Plan sheet/ })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /Profile sheet/ })).toBeInTheDocument();
    // …and the viewer neither unmounted nor mounted again: same element, same scene.
    expect(panelLifecycle.mounts).toBe(1);
    expect(panelLifecycle.unmounts).toBe(0);
  });

  it('never remounts the viewer across tab switches, and keeps the hidden pane in the DOM', () => {
    linksAnswer = [mapLink('link-a', 'Plan sheet')];
    show();
    expect(panelLifecycle.mounts).toBe(1);

    // To the map…
    fireEvent.click(screen.getByRole('tab', { name: /Plan sheet/ }));
    expect(screen.getByTestId('fake-map-pane-link-a')).toHaveAttribute('data-active', 'true');
    // The 3D pane is hidden, not destroyed: still in the document, lifecycle untouched.
    expect(screen.getByTestId('fake-caveview')).toBeInTheDocument();
    expect(panelLifecycle.unmounts).toBe(0);

    // …and back, twice, for good measure.
    fireEvent.click(screen.getByRole('tab', { name: '3D' }));
    fireEvent.click(screen.getByRole('tab', { name: /Plan sheet/ }));
    fireEvent.click(screen.getByRole('tab', { name: '3D' }));

    expect(panelLifecycle.mounts).toBe(1);
    expect(panelLifecycle.unmounts).toBe(0);
    // The map pane also stayed mounted, told it is off screen rather than taken down.
    expect(screen.getByTestId('fake-map-pane-link-a')).toHaveAttribute('data-active', 'false');
  });

  it('hands each map pane its own activation, so only the shown pane builds its map', () => {
    linksAnswer = [mapLink('link-a', 'Plan sheet'), mapLink('link-b', 'Profile sheet', 'map-profile-of')];
    show();

    // A pane never visited is not even mounted — the lazy half of the bargain…
    fireEvent.click(screen.getByRole('tab', { name: /Plan sheet/ }));
    expect(screen.getByTestId('fake-map-pane-link-a')).toHaveAttribute('data-active', 'true');
    expect(screen.queryByTestId('fake-map-pane-link-b')).not.toBeInTheDocument();

    // …and once visited, a pane stays mounted but is told it is off screen, which is what
    // gates its OL map: exactly one pane may believe it is being looked at.
    fireEvent.click(screen.getByRole('tab', { name: /Profile sheet/ }));
    expect(screen.getByTestId('fake-map-pane-link-b')).toHaveAttribute('data-active', 'true');
    expect(screen.getByTestId('fake-map-pane-link-a')).toHaveAttribute('data-active', 'false');
  });
});

describe('the paired define mode', () => {
  /** Opens the viewer on one declared map and enters its define mode. */
  function startDefining(extraLinks: ResLink[] = []) {
    linksAnswer = [mapLink('link-a', 'Plan sheet'), ...extraLinks];
    const view = show();
    fireEvent.click(screen.getByRole('tab', { name: /Plan sheet/ }));
    fireEvent.click(screen.getByTestId('fake-toggle-define-link-a'));
    expect(screen.getByTestId('fake-map-pane-link-a')).toHaveAttribute('data-defining', 'true');
    return view;
  }

  it('a station pressed in the 3D pane arms it, instead of offering a link', () => {
    startDefining();

    fireEvent.click(screen.getByTestId('fake-press-station'));

    expect(screen.getByTestId('rastermap-armed')).toHaveTextContent('p.g.7');
    // The generic offer is what the same press produces OUTSIDE define mode (below);
    // here the paired mode owns the click and no second banner competes for it.
    expect(screen.queryByRole('button', { name: /Link this/ })).not.toBeInTheDocument();
  });

  it('the same press outside define mode still offers to link the part', () => {
    linksAnswer = [mapLink('link-a', 'Plan sheet')];
    show();

    fireEvent.click(screen.getByTestId('fake-press-station'));

    expect(screen.getByRole('button', { name: /Link this/ })).toBeInTheDocument();
    expect(screen.queryByTestId('rastermap-armed')).not.toBeInTheDocument();
  });

  it('a survey press in define mode is refused with the reason, and arms nothing', async () => {
    startDefining();

    fireEvent.click(screen.getByTestId('fake-press-survey'));

    expect(await screen.findByText(/Pick a single station/)).toBeInTheDocument();
    expect(screen.queryByTestId('rastermap-armed')).not.toBeInTheDocument();
  });

  it('the typeahead over the loaded stations is the other way to arm', () => {
    startDefining();

    const search = screen.getByTestId('rastermap-station-search').querySelector('input')!;
    fireEvent.change(search, { target: { value: 'deep' } });
    fireEvent.click(document.querySelector('.ant-select-item-option[title="cave.deep.3"]')!);

    expect(screen.getByTestId('rastermap-armed')).toHaveTextContent('cave.deep.3');
  });

  it('Escape disarms — and only disarms: the viewer stays open', () => {
    startDefining();
    fireEvent.click(screen.getByTestId('fake-press-station'));
    expect(screen.getByTestId('rastermap-armed')).toBeInTheDocument();

    fireEvent.keyDown(document.body, { key: 'Escape' });

    expect(screen.queryByTestId('rastermap-armed')).not.toBeInTheDocument();
    // The press was spent on the arm, not on closing the dialog over it.
    expect(screen.getByTestId('fake-caveview')).toBeInTheDocument();
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  it('survives the 3D round trip — that is how pressing a station arms — and ends on tab-away', () => {
    startDefining([mapLink('link-b', 'Profile sheet', 'map-profile-of')]);
    fireEvent.click(screen.getByTestId('fake-press-station'));

    // To the 3D pane and back: the mode and the armed station both hold, because the 3D
    // pane is half of the paired mode.
    fireEvent.click(screen.getByRole('tab', { name: '3D' }));
    expect(screen.getByTestId('rastermap-armed')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('tab', { name: /Plan sheet/ }));
    expect(screen.getByTestId('fake-map-pane-link-a')).toHaveAttribute('data-defining', 'true');
    expect(screen.getByTestId('rastermap-armed')).toBeInTheDocument();

    // To a different map: that is leaving, and an armed click landing on the wrong sheet
    // is exactly what ending the mode prevents.
    fireEvent.click(screen.getByRole('tab', { name: /Profile sheet/ }));
    expect(screen.getByTestId('fake-map-pane-link-a')).toHaveAttribute('data-defining', 'false');
    expect(screen.queryByTestId('rastermap-armed')).not.toBeInTheDocument();
    expect(screen.queryByTestId('rastermap-authoring')).not.toBeInTheDocument();
  });
});
