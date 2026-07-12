// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef, useState } from 'react';
import { Alert, Spin } from 'antd';
import { useTranslation } from 'react-i18next';
import { CAVEVIEW_HOME, loadCaveView, type CaveViewUi } from '../../caveview/loadCaveView.ts';

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
}

// CaveView addresses its container by element id; keep ids unique across remounts and
// multiple simultaneous panels (main window + pop-outs).
let panelSequence = 0;

/**
 * Isolated wrapper around the vendored CaveView.js viewer: fetches the survey file,
 * hands it to CaveView as a named File (parser choice), and tears the viewer down on
 * unmount. CaveView owns everything inside its container div — React never touches it.
 */
export default function CaveViewPanel({ fileUrl, fileName, height = 480, onEntrancePick }: CaveViewPanelProps) {
  const { t } = useTranslation();
  const containerIdRef = useRef<string>(null);
  containerIdRef.current ??= `caveview-panel-${panelSequence++}`;

  const [status, setStatus] = useState<'loading' | 'ready' | 'error'>('loading');
  const [errorDetail, setErrorDetail] = useState<string>();

  // The callback rides a ref so a new identity doesn't reload the whole viewer.
  const onEntrancePickRef = useRef(onEntrancePick);
  onEntrancePickRef.current = onEntrancePick;

  useEffect(() => {
    let disposed = false;
    let ui: CaveViewUi | null = null;
    setStatus('loading');

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
      ui = new cv2.CaveViewUI(viewer);
      ui.loadCave(new File([blob], fileName));
    })().catch((error: unknown) => {
      if (disposed) return;
      setStatus('error');
      setErrorDetail(error instanceof Error ? error.message : String(error));
    });

    return () => {
      disposed = true;
      ui?.dispose();
      ui = null;
    };
  }, [fileUrl, fileName]);

  return (
    <div style={{ position: 'relative', height }}>
      {status === 'loading' && (
        <Spin style={{ position: 'absolute', inset: 0, marginTop: 48 }} data-testid="caveview-loading" />
      )}
      {status === 'error' && (
        <Alert type="error" showIcon message={t('caveview.loadError')} description={errorDetail} />
      )}
      <div
        id={containerIdRef.current}
        data-testid="caveview-container"
        style={{ width: '100%', height: '100%', display: status === 'error' ? 'none' : undefined }}
      />
    </div>
  );
}
