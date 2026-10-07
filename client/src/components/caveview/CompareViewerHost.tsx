// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useId, useRef, useState } from 'react';
import { Alert, Spin } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  caveViewerOptions,
  loadCaveView,
  type CaveViewToolbar,
  type CaveViewToolbarOptions,
  type CaveViewUi,
  type CaveViewer,
} from '../../caveview/loadCaveView.ts';
import { releaseWebGlContext } from '../../caveview/releaseWebGlContext.ts';

/** One survey file a comparison's viewer is built from. */
export interface CompareViewerFile {
  /** Where the file is read from, at the moment the viewer is built. */
  url: string;
  /** The name the viewer chooses its parser by. */
  fileName: string;
  /** The survey this file's own surveys hang from, where it is drawn with another. */
  label?: string;
  /** The colour it is drawn in under the by-survey shading. */
  color?: string;
}

export interface CompareViewerHostProps {
  /**
   * What the viewer draws: one file alone, or several as one model, each under its label.
   *
   * <b>Read once, when the viewer is built.</b> A file's address is signed and re-issued every few
   * minutes for as long as anything lists the survey; a viewer rebuilt for each new address would
   * reload the model under the reader and put the camera back where it started.
   */
  files: readonly CompareViewerFile[];
  /** What identifies those files, so the viewer is built again only for other ones. */
  filesKey: string;
  /** Draws each labelled file in its own colour rather than the model by height. */
  colourBySurvey?: boolean;
  /** The viewer's own controls over the model, or null for none. */
  toolbar: CaveViewToolbarOptions | null;
  /** See the panel's prop of the same name. Read when the viewer is built. */
  crsLookup?: (code: string) => Promise<string | null>;
  /**
   * Keeps the model out of sight while it is still being decided whether it may be shown. The
   * surface keeps its size, which the viewer reads when it is built; only its picture is withheld.
   */
  concealed?: boolean;
  /** Told the viewer once its model is drawn, and null when that viewer goes. */
  onViewer(viewer: CaveViewer | null): void;
  testId: string;
}

type Status = 'loading' | 'ready' | 'error';

/**
 * The second viewer of a comparison: the other survey beside the first, or both laid over one
 * another.
 *
 * <b>A viewer of its own, never the panel's.</b> The panel's viewer is the one every other feature
 * of it is attached to — the pictures on its stations, the links that fly to them, a pick that is
 * offered for linking — and all of them name stations by their path in that one file. Loading a
 * second file into it would put every path under a label and leave each of those pointing at
 * nothing. So what is compared is drawn here, and the panel's viewer is left exactly as it was to
 * come back to.
 *
 * Built with what every viewer in this application is built with (see `caveViewerOptions`), and
 * disposed the moment it goes off the screen: it holds a drawing context, of which a page has few.
 */
export default function CompareViewerHost({
  files,
  filesKey,
  colourBySurvey = false,
  toolbar,
  crsLookup,
  concealed = false,
  onViewer,
  testId,
}: CompareViewerHostProps) {
  const { t, i18n } = useTranslation();
  const containerId = `caveview-compare-${useId().replace(/[^a-zA-Z0-9_-]/g, '')}`;
  const [status, setStatus] = useState<Status>('loading');
  const [errorDetail, setErrorDetail] = useState<string>();
  // The viewer lives in a ref and React holds only a count of the models drawn, so the effects
  // that set things on a viewer run again for the next one and none is keyed on an engine.
  const viewerRef = useRef<CaveViewer | null>(null);
  const [loads, setLoads] = useState(0);

  // Read at the moment a viewer is built and never a reason to build one — see the panel, which
  // reads the same three things the same way.
  const filesRef = useRef(files);
  filesRef.current = files;
  const languageRef = useRef(i18n.language);
  languageRef.current = i18n.language;
  const crsLookupRef = useRef(crsLookup);
  crsLookupRef.current = crsLookup;
  const colourBySurveyRef = useRef(colourBySurvey);
  colourBySurveyRef.current = colourBySurvey;
  const onViewerRef = useRef(onViewer);
  onViewerRef.current = onViewer;

  useEffect(() => {
    let disposed = false;
    let ui: CaveViewUi | null = null;
    let built: CaveViewer | null = null;
    const download = new AbortController();
    // Held from the start: by the time an unmount's cleanup runs, the page no longer has it.
    const container = document.getElementById(containerId);
    const wanted = filesRef.current;
    setStatus('loading');
    setErrorDetail(undefined);

    (async () => {
      const cv2 = await loadCaveView();
      const blobs = await Promise.all(
        wanted.map(async (file) => {
          const response = await fetch(file.url, { signal: download.signal });
          if (!response.ok) throw new Error(`survey file request failed (${response.status})`);
          return response.blob();
        }),
      );
      if (disposed) return;

      const viewer = new cv2.CaveViewer(
        containerId,
        caveViewerOptions(languageRef.current, crsLookupRef.current),
      );
      built = viewer;
      viewer.addEventListener('newCave', (event) => {
        if (disposed) return;
        // The viewer says this for a model that has loaded, carrying the survey, and also,
        // carrying none, when a key switches how its controls behave. Only the first is a model.
        if ((event as { survey?: unknown } | null)?.survey === undefined) return;
        // Set on the loaded viewer rather than asked for when it is built: the viewer re-applies
        // whatever its "save as default" button last stored in this browser on every load, and a
        // stored shading would otherwise draw two surveys in one scale of heights.
        if (colourBySurveyRef.current) {
          viewer.shadingMode = cv2.SHADING_SURVEY;
        }
        viewerRef.current = viewer;
        setLoads((count) => count + 1);
        setStatus('ready');
        onViewerRef.current(viewer);
      });

      ui = new cv2.CaveViewUI(viewer);
      const described = wanted.map((file, index) => ({
        file: new File([blobs[index]], file.fileName),
        label: file.label,
        color: file.color,
      }));
      if (described.length === 1 && described[0].label === undefined) {
        ui.loadCave(described[0].file);
      } else {
        ui.loadCaves(described);
      }
    })().catch((error: unknown) => {
      if (disposed) return;
      setStatus('error');
      setErrorDetail(error instanceof Error ? error.message : String(error));
    });

    return () => {
      disposed = true;
      // A file still on its way is not wanted any more.
      download.abort();
      if (viewerRef.current !== null) {
        viewerRef.current = null;
        onViewerRef.current(null);
      }
      const canvas = container?.querySelector('canvas') ?? null;
      (ui as CaveViewUi | null)?.dispose();
      ui = null;
      // The first viewer of the comparison is still on the screen, and is the older of the two.
      releaseWebGlContext(canvas);
      // A viewer disposed under a load is still asked to draw by its own progress dial; see the
      // panel, which answers the same late asks the same way.
      if (built !== null) {
        (built as CaveViewer).renderView = () => {};
        built = null;
      }
    };
  }, [containerId, filesKey]);

  // The viewer watches the window and nothing else, and this surface changes size while the
  // window does not: when the two surveys go from side by side to one above the other, and when
  // the list of their parts is opened under them.
  useEffect(() => {
    const surface = document.getElementById(containerId);
    const viewer = viewerRef.current;
    if (surface === null || viewer === null || typeof ResizeObserver === 'undefined') {
      return;
    }
    let last = '';
    const observer = new ResizeObserver((entries) => {
      const box = entries[0]?.contentRect;
      // Measured zero is not measured: a surface that is not displayed has no size to draw at.
      if (box === undefined || box.width === 0 || box.height === 0) {
        return;
      }
      const size = `${Math.round(box.width)}x${Math.round(box.height)}`;
      if (size !== last) {
        last = size;
        viewer.resize();
      }
    });
    observer.observe(surface);
    return () => observer.disconnect();
  }, [containerId, loads]);

  // ---- The viewer's own toolbar ----
  const toolbarWanted = toolbar !== null;
  const toolbarPlacement = toolbar?.placement ?? 'top';
  const toolbarButtons = toolbar?.buttons ?? [];
  const toolbarButtonKey = toolbarButtons.join(',');
  const toolbarButtonsRef = useRef(toolbarButtons);
  toolbarButtonsRef.current = toolbarButtons;
  useEffect(() => {
    const viewer = viewerRef.current;
    if (!toolbarWanted || viewer === null) {
      return;
    }
    let disposed = false;
    let bar: CaveViewToolbar | null = null;
    void loadCaveView().then((cv2) => {
      if (disposed || viewerRef.current !== viewer) {
        return;
      }
      bar = new cv2.CaveViewToolbar(viewer, containerId, {
        placement: toolbarPlacement,
        buttons: [...toolbarButtonsRef.current],
      });
    });
    return () => {
      disposed = true;
      bar?.dispose();
      bar = null;
    };
  }, [containerId, loads, toolbarWanted, toolbarPlacement, toolbarButtonKey]);

  return (
    <>
      {status === 'loading' && (
        <Spin className="caveview-compare-loading" data-testid={`${testId}-loading`} />
      )}
      {status === 'error' && (
        <Alert type="error" showIcon title={t('caveview.loadError')} description={errorDetail} />
      )}
      <div
        id={containerId}
        className="caveview-panel-surface"
        data-testid={testId}
        style={{
          width: '100%',
          height: '100%',
          display: status === 'error' ? 'none' : undefined,
          visibility: concealed ? 'hidden' : undefined,
        }}
      />
    </>
  );
}
