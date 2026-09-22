// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { useEffect } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { ResLink, SurveyModelInfo } from '../../api/hooks.ts';
import SurveyModelViewerModal from './SurveyModelViewerModal.tsx';

/**
 * What this file proves is one sentence in the design of the map tabs: <b>the 3D viewer is
 * created once and never remounts on a tab switch.</b> A remount is not cosmetic — the real
 * panel re-fetches and re-parses the whole survey and burns a WebGL context doing it — so the
 * panel is faked with a mount counter and the assertions are about the counter, not the scene.
 */
const panelLifecycle = { mounts: 0, unmounts: 0 };

vi.mock('./CaveViewPanel.tsx', () => ({
  default: function FakeCaveViewPanel() {
    useEffect(() => {
      panelLifecycle.mounts += 1;
      return () => {
        panelLifecycle.unmounts += 1;
      };
    }, []);
    return <div data-testid="fake-caveview">the 3D scene</div>;
  },
}));

vi.mock('../../rastermap/RasterMapPane.tsx', () => ({
  default: function FakeRasterMapPane({
    declaration,
    active,
  }: {
    declaration: { linkId: string };
    active: boolean;
  }) {
    return <div data-testid={`fake-map-pane-${declaration.linkId}`} data-active={String(active)} />;
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

describe('SurveyModelViewerModal', () => {
  it('shows the viewer without a visible tab strip while the model declares no maps', () => {
    linksAnswer = [];
    render(<SurveyModelViewerModal model={MODEL} onClose={vi.fn()} />);

    expect(screen.getByTestId('fake-caveview')).toBeInTheDocument();
    // The Tabs element is in the tree — that is what keeps the pane's place stable when
    // declarations arrive — but its bar says nothing and is hidden.
    const bar = document.querySelector('.ant-tabs-nav') as HTMLElement;
    expect(bar).not.toBeNull();
    expect(bar.style.display).toBe('none');
  });

  it('keeps the viewer mounted, not merely re-created, when the declarations arrive', () => {
    linksAnswer = undefined;
    const view = render(<SurveyModelViewerModal model={MODEL} onClose={vi.fn()} />);
    expect(panelLifecycle.mounts).toBe(1);

    // The link read lands and two tabs appear beside the 3D one…
    linksAnswer = [mapLink('link-a', 'Plan sheet'), mapLink('link-b', 'Profile sheet', 'map-profile-of')];
    view.rerender(<SurveyModelViewerModal model={MODEL} onClose={vi.fn()} />);

    expect(screen.getByRole('tab', { name: /Plan sheet/ })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /Profile sheet/ })).toBeInTheDocument();
    // …and the viewer neither unmounted nor mounted again: same element, same scene.
    expect(panelLifecycle.mounts).toBe(1);
    expect(panelLifecycle.unmounts).toBe(0);
  });

  it('never remounts the viewer across tab switches, and keeps the hidden pane in the DOM', () => {
    linksAnswer = [mapLink('link-a', 'Plan sheet')];
    render(<SurveyModelViewerModal model={MODEL} onClose={vi.fn()} />);
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
    render(<SurveyModelViewerModal model={MODEL} onClose={vi.fn()} />);

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
