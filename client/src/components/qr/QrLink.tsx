// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { CopyOutlined } from '@ant-design/icons';
import { App, Flex, Input, QRCode } from 'antd';
import { useTranslation } from 'react-i18next';
import Button from '../Button.tsx';

interface QrLinkProps {
  /** The address the square carries and the box shows. */
  url: string;
  /** What the person is told once it is on their clipboard. */
  copiedMessage: string;
  /** Under the square, before the address — a printed code, say. */
  caption?: ReactNode;
  /** Names the square for a screen reader: what the address opens. */
  label: string;
}

/**
 * An address as a square a phone can read, with the address itself beside it to copy — for a
 * link that is going to be shown across a table, printed on a label or held up to a camera
 * rather than pasted into a message.
 */
export default function QrLink({ url, copiedMessage, caption, label }: QrLinkProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(url);
      message.success(copiedMessage);
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  return (
    <Flex vertical align="center" gap={8}>
      <div role="img" aria-label={label}>
        <QRCode value={url} size={160} bordered={false} />
      </div>
      {caption}
      <Flex gap={8} style={{ width: '100%' }}>
        <Input readOnly value={url} aria-label={t('qr.address')} onFocus={(e) => e.target.select()} />
        <Button icon={<CopyOutlined />} onClick={() => void copy()}>
          {t('qr.copyUrl')}
        </Button>
      </Flex>
    </Flex>
  );
}
