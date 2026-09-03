// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef, useState } from 'react';
import { Alert, Spin } from 'antd';
import { useTranslation } from 'react-i18next';
import { acquireCrsRewrite, CAVEVIEW_HOME, loadCaveView, type CaveViewUi } from '../../caveview/loadCaveView.ts';
import {
  partFromLeg,
  partFromStation,
  sectionForRef,
  type PickedModelPart,
} from '../../caveview/modelParts.ts';
import type { ResourceRef } from '../../viewlinks/resourceRef.ts';
import { useViewControl } from '../../viewlinks/useViewControl.ts';

export interface CaveViewPanelProps {
  /** Delivery URL of the survey file (carries its own access token, no auth header). */
  fileUrl: string;
  /**
   * File name ending in the format extension (.lox or .3d) — CaveView selects its parser
   * from the name, not the URL, so signed URLs without extensions work fine.
   */
  fileName: string;
  /** CSS height of the viewer surface. */
  height?: number | string;
  /** Fired with the survey's entrance label when one is clicked in the 3D scene. */
  onEntrancePick?: (displayName: string) => void;
  /**
   * Fired when a station or a leg is clicked and can be named — the caller then decides what to
   * offer. Absent means clicks do only what the viewer does with them.
   *
   * A pick that cannot be named produces nothing rather than an empty offer: a splay's far end
   * was never a station, and an anchor naming one end and inventing the other would read as
   * exact while pointing at nothing.
   */
  onPartPick?: (part: PickedModelPart) => void;
  /**
   * Which survey model this panel is showing, when the caller knows.
   *
   * Supplied only so the panel can answer links: a passage that names a station of *this* model
   * is something it can show, and a passage that names another cave's model is not. Without it
   * the panel still works and simply never volunteers to answer one, which is the right default
   * — a viewer that accepted every link would jump to a station name that happens to exist in
   * whatever cave it has open.
   */
  surveyModelId?: string;
}

// CaveView addresses its container by element id; keep ids unique across remounts and
// multiple simultaneous panels (main window + pop-outs).
let panelSequence = 0;

/**
 * Isolated wrapper around the vendored CaveView.js viewer: fetches the survey file,
 * hands it to CaveView as a named File (parser choice), and tears the viewer down on
 * unmount. CaveView owns everything inside its container div — React never touches it.
 */
export default function CaveViewPanel({
  fileUrl,
  fileName,
  height = 480,
  onEntrancePick,
  onPartPick,
  surveyModelId,
}: CaveViewPanelProps) {
  const { t } = useTranslation();
  const containerIdRef = useRef<string>(null);
  containerIdRef.current ??= `caveview-panel-${panelSequence++}`;

  const [status, setStatus] = useState<'loading' | 'ready' | 'error'>('loading');
  const [errorDetail, setErrorDetail] = useState<string>();

  // The loaded survey and the viewer that holds it, kept so a link can be answered after the
  // load. The file is kept as well because showing a named part of the survey is done by loading
  // it again with that part named — the vendored viewer's one way in — and fetching the bytes a
  // second time to do it would be a download per click.
  const loadedRef = useRef<{ ui: CaveViewUi; file: File } | null>(null);

  const sectionOf = (ref: ResourceRef): string | null => sectionForRef(ref, surveyModelId);

  useViewControl({
    id: `caveview-${containerIdRef.current}`,
    kind: 'caveview',
    labelKey: 'viewLinks.controls.caveview',
    enabled: status === 'ready' && surveyModelId !== undefined,
    canReveal: (ref) => sectionOf(ref) !== null,
    reveal: (ref) => {
      const section = sectionOf(ref);
      const loaded = loadedRef.current;
      if (section !== null && loaded !== null) {
        loaded.ui.loadCave(loaded.file, section);
      }
    },
  });

  // The callbacks ride refs so a new identity doesn't reload the whole viewer. Naming either of
  // them in the effect's dependencies is how a parent that re-renders per keystroke ends up
  // re-fetching and re-parsing a survey file on every one.
  const onEntrancePickRef = useRef(onEntrancePick);
  onEntrancePickRef.current = onEntrancePick;
  const onPartPickRef = useRef(onPartPick);
  onPartPickRef.current = onPartPick;

  useEffect(() => {
    let disposed = false;
    let ui: CaveViewUi | null = null;
    setStatus('loading');

    // Acquired synchronously, before any await, so a survey that starts parsing the moment the
    // bundle is ready cannot get its CRS lookup out to the internet ahead of the rewrite.
    const releaseCrsRewrite = acquireCrsRewrite();

    (async () => {
      const cv2 = await loadCaveView();
      const response = await fetch(fileUrl);
      if (!response.ok) throw new Error(`survey file request failed (${response.status})`);
      const blob = await response.blob();
      if (disposed) return;

      const viewer = new cv2.CaveViewer(containerIdRef.current!, { home: CAVEVIEW_HOME });
      viewer.addEventListener('newCave', () => {
        if (!disposed) setStatus('ready');
      });
      viewer.addEventListener('entrance', (event) => {
        const name = (event as { displayName?: unknown }).displayName;
        if (!disposed && typeof name === 'string' && name) {
          onEntrancePickRef.current?.(name);
        }
      });

      // Both are watched unconditionally and answer only when somebody is listening, because the
      // listeners are attached once with the viewer and the caller's interest can change without
      // the survey being reloaded.
      //
      // `handled` is deliberately left alone. The bundle reads it back after dispatching and
      // treats a true as "the application dealt with this click", which would stop the viewer
      // selecting and highlighting what was clicked — so offering to link a station would take
      // away the ability to simply look at one.
      viewer.addEventListener('station', (event) => {
        const part = partFromStation((event as { node?: unknown }).node);
        if (!disposed && part !== null) {
          onPartPickRef.current?.(part);
        }
      });
      viewer.addEventListener('leg', (event) => {
        const part = partFromLeg((event as { leg?: unknown }).leg);
        if (!disposed && part !== null) {
          onPartPickRef.current?.(part);
        }
      });
      ui = new cv2.CaveViewUI(viewer);
      const file = new File([blob], fileName);
      loadedRef.current = { ui, file };
      ui.loadCave(file);
    })().catch((error: unknown) => {
      if (disposed) return;
      setStatus('error');
      setErrorDetail(error instanceof Error ? error.message : String(error));
    });

    return () => {
      disposed = true;
      loadedRef.current = null;
      ui?.dispose();
      ui = null;
      releaseCrsRewrite();
    };
  }, [fileUrl, fileName]);

  return (
    <div style={{ position: 'relative', height }}>
      {status === 'loading' && (
        <Spin style={{ position: 'absolute', inset: 0, marginTop: 48 }} data-testid="caveview-loading" />
      )}
      {status === 'error' && (
        <Alert type="error" showIcon title={t('caveview.loadError')} description={errorDetail} />
      )}
      <div
        id={containerIdRef.current}
        data-testid="caveview-container"
        style={{ width: '100%', height: '100%', display: status === 'error' ? 'none' : undefined }}
      />
    </div>
  );
}
