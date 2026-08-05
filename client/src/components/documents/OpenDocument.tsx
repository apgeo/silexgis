// SPDX-License-Identifier: AGPL-3.0-or-later
import { FileSearchOutlined } from '@ant-design/icons';
import { Button, Tooltip } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';

/**
 * Opens the document a file belongs to.
 *
 * A plain link rather than a fetch: the page is addressed by document id and needs no token,
 * unlike the bytes. It is offered to every reader, not only to editors — reading a document
 * is not an editing act, and it was the missing one.
 */
export default function OpenDocument({ documentId }: { documentId: string }) {
  const { t } = useTranslation();
  return (
    <Tooltip title={t('documents.open')}>
      <Link to={`/documents/${documentId}`}>
        <Button size="small" type="text" icon={<FileSearchOutlined />} aria-label={t('documents.open')} />
      </Link>
    </Tooltip>
  );
}
