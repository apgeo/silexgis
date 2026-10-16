// SPDX-License-Identifier: AGPL-3.0-or-later
import { Modal, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import QrLink from '../qr/QrLink.tsx';

interface ViewShareDialogProps {
  /** The view being shared, by name, and the address its link opens; null keeps the dialog shut. */
  shared: { name: string; url: string } | null;
  onClose: () => void;
}

/**
 * The link to a saved map view, once it has been made shareable: the address to copy and the same
 * address as a square — for showing a view to somebody across a table, or putting it on a printed
 * sheet. Opening the link needs no account, which the dialog says.
 */
export default function ViewShareDialog({ shared, onClose }: ViewShareDialogProps) {
  const { t } = useTranslation();
  return (
    <Modal
      open={shared !== null}
      title={shared ? t('views.shareTitle', { name: shared.name }) : undefined}
      onCancel={onClose}
      footer={null}
      destroyOnHidden
      width={420}
    >
      {shared && (
        <>
          <Typography.Paragraph type="secondary">{t('views.shareExplain')}</Typography.Paragraph>
          <QrLink
            url={shared.url}
            copiedMessage={t('views.linkCopied')}
            label={t('views.shareQr', { name: shared.name })}
          />
        </>
      )}
    </Modal>
  );
}
