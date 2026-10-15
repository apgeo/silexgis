// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef, useState } from 'react';
import { Alert, Button, Flex, Typography } from 'antd';
import { ExportOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import type { SurveyModelInfo } from '../../api/hooks.ts';
import {
  DANKEZ_CAVEVIEW_PAGE,
  readViewerEvent,
  sendModel,
} from '../../caveview/dankezCaveView.ts';
import { viewerFileName } from '../../caveview/viewerFileName.ts';

interface DankezCaveViewPaneProps {
  model: SurveyModelInfo;
  height: string;
}

type Status =
  | { kind: 'sending' }
  | { kind: 'loaded'; stations: number }
  | { kind: 'error'; message: string };

/**
 * The same survey in dankez-caveview (LochViewer), a second viewer on trial beside the embedded one.
 *
 * Framed rather than built into the page because it is a whole application of its own — its own
 * React, its own three.js and its own interface — which shares nothing with this one but the file.
 * The caller mounts this only while its tab is shown: the frame holds a drawing context of its own,
 * out of the same small budget the embedded viewer and the map draw from.
 *
 * The file is read here once and handed to whichever viewer says it is ready — the frame, or a
 * window opened from the button. The window is there because a deployed installation forbids
 * framing its pages, this one included, so outside the development server the frame stays empty.
 */
export default function DankezCaveViewPane({ model, height }: DankezCaveViewPaneProps) {
  const { t } = useTranslation();
  const frameRef = useRef<HTMLIFrameElement>(null);
  const windowRef = useRef<Window | null>(null);
  const bytesRef = useRef<Promise<ArrayBuffer> | null>(null);
  const [status, setStatus] = useState<Status>({ kind: 'sending' });
  const [station, setStation] = useState<string | null>(null);
  const fileName = viewerFileName(model);

  // Read once per model. A failure is shown here: the viewer would only ever be sent nothing.
  useEffect(() => {
    setStatus({ kind: 'sending' });
    setStation(null);
    const bytes = fetch(model.modelUrl).then((response) => {
      if (!response.ok) {
        throw new Error(`survey file request failed (${response.status})`);
      }
      return response.arrayBuffer();
    });
    bytes.catch(() => setStatus({ kind: 'error', message: t('caveview.loadError') }));
    bytesRef.current = bytes;
  }, [model.modelUrl, t]);

  useEffect(() => {
    // Served from this application's own origin; the one place that would change if it moved.
    const origin = window.location.origin;
    const onMessage = (event: MessageEvent) => {
      const frame = frameRef.current?.contentWindow ?? null;
      const viewer =
        event.source !== null && event.source === frame
          ? frame
          : event.source !== null && event.source === windowRef.current
            ? windowRef.current
            : null;
      const message = readViewerEvent(event, viewer, origin);
      if (message === null || viewer === null) {
        return;
      }
      switch (message.type) {
        case 'lochviewer:ready':
          void bytesRef.current?.then((bytes) => sendModel(viewer, origin, fileName, bytes)).catch(() => {});
          break;
        // What the frame reports is what this pane shows; a window speaks for itself.
        case 'lochviewer:loaded':
          if (viewer === frame) setStatus({ kind: 'loaded', stations: message.stations });
          break;
        case 'lochviewer:error':
          if (viewer === frame) setStatus({ kind: 'error', message: message.message });
          break;
        case 'lochviewer:station':
          setStation(message.name);
          break;
      }
    };
    window.addEventListener('message', onMessage);
    return () => window.removeEventListener('message', onMessage);
  }, [fileName]);

  const statusText =
    status.kind === 'sending'
      ? t('caveview.dankezCaveView.sending')
      : status.kind === 'loaded'
        ? t('caveview.dankezCaveView.loaded', { count: status.stations })
        : status.message;

  return (
    <Flex vertical gap="small">
      <Alert
        type="warning"
        showIcon
        title={t('caveview.dankezCaveView.trialNotice')}
        action={
          <Button
            size="small"
            icon={<ExportOutlined />}
            data-testid="dankez-caveview-open-window"
            // Not `noopener`: the window has to be able to say it is ready, and is sent the file
            // in answer.
            onClick={() => {
              windowRef.current = window.open(DANKEZ_CAVEVIEW_PAGE, '_blank');
            }}
          >
            {t('caveview.dankezCaveView.openWindow')}
          </Button>
        }
      />
      <Flex gap="middle" wrap>
        <Typography.Text
          type={status.kind === 'error' ? 'danger' : 'secondary'}
          data-testid="dankez-caveview-status"
        >
          {statusText}
        </Typography.Text>
        {station !== null && (
          <Typography.Text data-testid="dankez-caveview-station">
            {t('caveview.dankezCaveView.station', { name: station })}
          </Typography.Text>
        )}
      </Flex>
      <iframe
        ref={frameRef}
        data-testid="dankez-caveview-frame"
        title={t('caveview.dankezCaveView.tab')}
        src={DANKEZ_CAVEVIEW_PAGE}
        style={{ width: '100%', height, border: 0 }}
        allow="fullscreen"
      />
    </Flex>
  );
}
