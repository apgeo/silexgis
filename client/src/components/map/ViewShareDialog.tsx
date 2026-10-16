// SPDX-License-Identifier: AGPL-3.0-or-later
import { Modal, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import QrLink from '../qr/QrLink.tsx';

/** The view being shared, by name, the address its link opens and what the link leaves out. */
export interface SharedViewLink {
  name: string;
  url: string;
  /**
   * `dataLayers`: the view is narrowed by a saved filter or a filter document, which a link cannot
   * run, so the link shows neither the caves nor the features. `filterLayers`: the view draws
   * layers from saved filters, which a link never shows.
   */
  leavesOut?: { dataLayers: boolean; filterLayers: boolean };
}

interface ViewShareDialogProps {
  /** Null keeps the dialog shut. */
  shared: SharedViewLink | null;
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
          {shared.leavesOut?.dataLayers && (
            <Typography.Paragraph type="warning" data-testid="view-share-without-data">
              {t('views.shareWithoutData')}
            </Typography.Paragraph>
          )}
          {shared.leavesOut?.filterLayers && !shared.leavesOut.dataLayers && (
            <Typography.Paragraph type="secondary" data-testid="view-share-without-filter-layers">
              {t('views.shareWithoutFilterLayers')}
            </Typography.Paragraph>
          )}
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
