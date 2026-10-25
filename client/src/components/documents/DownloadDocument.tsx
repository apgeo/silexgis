// SPDX-License-Identifier: AGPL-3.0-or-later
import { DownloadOutlined } from '@ant-design/icons';
import { Tooltip } from 'antd';
import Button from '../Button.tsx';
import { useTranslation } from 'react-i18next';
import type { FileInfo } from '../../api/hooks.ts';
import { downloadTitle } from '../attachments/downloadTitle.ts';

/**
 * Download of the stored bytes. Present for every document and disabled — with the reason —
 * where the caller may not have the original, because a control that fails when followed
 * teaches nothing.
 *
 * It lives on its own rather than beside the viewer because every branch that cannot show
 * something ends here: a format nothing can draw, a page that would not draw, a document
 * whose bytes are withheld. Saying "there is nothing to see" without offering the one thing
 * that does work would be a dead end rather than an answer.
 */
export default function DownloadDocument({ file, block }: { file: FileInfo; block?: boolean }) {
  const { t } = useTranslation();
  return (
    <Tooltip title={downloadTitle(file, t)}>
      <Button
        icon={<DownloadOutlined />}
        block={block}
        disabled={!file.mayDownloadOriginal}
        href={file.mayDownloadOriginal ? file.contentUrl : undefined}
        download={file.originalName}
      >
        {t('documents.download')}
      </Button>
    </Tooltip>
  );
}
