// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Button, Flex } from 'antd';
import { ExportOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import type { SurveyModelInfo } from '../../api/hooks.ts';
import { lochViewerUrl } from '../../caveview/lochViewer.ts';
import { viewerFileName } from '../../caveview/viewerFileName.ts';

interface LochViewerPaneProps {
  model: SurveyModelInfo;
  height: string;
}

/**
 * The same survey file in LochViewer, a second viewer on trial beside the embedded one.
 *
 * Framed rather than built into the page because it is a whole application of its own — its own
 * React, its own three.js and its own interface — which shares nothing with this one but the file.
 * The caller mounts this only while its tab is shown: the frame holds a drawing context of its own,
 * out of the same small budget the embedded viewer and the map draw from.
 *
 * The window button is there because a deployed installation forbids framing its pages, this one
 * included, so outside the development server the frame stays empty and a window is how to try it.
 */
export default function LochViewerPane({ model, height }: LochViewerPaneProps) {
  const { t } = useTranslation();
  const src = lochViewerUrl(model.modelUrl, viewerFileName(model), window.location.origin);

  return (
    <Flex vertical gap="small">
      <Alert
        type="warning"
        showIcon
        title={t('caveview.lochViewer.trialNotice')}
        action={
          <Button
            size="small"
            icon={<ExportOutlined />}
            data-testid="lochviewer-open-window"
            onClick={() => window.open(src, '_blank', 'noopener')}
          >
            {t('caveview.lochViewer.openWindow')}
          </Button>
        }
      />
      <iframe
        data-testid="lochviewer-frame"
        title={t('caveview.lochViewer.tab')}
        src={src}
        style={{ width: '100%', height, border: 0 }}
        allow="fullscreen"
      />
    </Flex>
  );
}
