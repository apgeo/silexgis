// SPDX-License-Identifier: AGPL-3.0-or-later
import { GlobalOutlined, StopOutlined } from '@ant-design/icons';
import { Alert, App, Button, Descriptions, Divider, Flex, Modal, Popconfirm, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useCaveQrPublication, usePublishCaveQr, useRevokeCaveQr } from '../../api/hooks.ts';
import QrCodeSquare from './QrCodeSquare.tsx';

interface QrPublicationModalProps {
  /** The cave the decision is about. Publication is per cave; everything inside one inherits it. */
  caveId: string;
  /**
   * A code carried by the cave itself, if it has one. Places inside the cave carry their own and
   * each of them resolves through this same decision, so this is what to print rather than the
   * only thing publishing affects.
   */
  code?: string | null;
  open: boolean;
  onClose: () => void;
}

/**
 * Deciding whether a code printed on a label at this cave resolves for somebody with no account,
 * and taking that decision back.
 *
 * What publishing does and does not do is stated on the dialog rather than left to be inferred,
 * because the two plausible readings are very far apart: it does not put the cave on a public map,
 * and it does not disclose a name or a position to anyone. It makes a printed code answer
 * "yes, this is registered here" instead of answering nothing.
 */
export default function QrPublicationModal({ caveId, code, open, onClose }: QrPublicationModalProps) {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const { data: publication, isError } = useCaveQrPublication(caveId, open);
  const publish = usePublishCaveQr();
  const revoke = useRevokeCaveQr();

  const formatWhen = (value: string | null) =>
    value ? new Date(value).toLocaleString(i18n.language) : '—';

  const onPublish = async () => {
    try {
      await publish.mutateAsync(caveId);
      message.success(t('qr.published'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const onRevoke = async () => {
    try {
      await revoke.mutateAsync(caveId);
      message.success(t('qr.revoked'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  return (
    <Modal
      open={open}
      onCancel={onClose}
      title={t('qr.title')}
      footer={null}
      width={560}
      destroyOnHidden
    >
      {isError ? (
        <Tag color="warning">{t('qr.notAllowed')}</Tag>
      ) : (
        <>
          <Alert type="info" showIcon title={t('qr.explainTitle')} description={t('qr.explainBody')} />

          <Flex align="center" gap={12} wrap style={{ marginTop: 16 }}>
            {publication?.published ? (
              <Tag color="success" data-testid="qr-state-published">
                {t('qr.statusPublished')}
              </Tag>
            ) : (
              <Tag data-testid="qr-state-unpublished">{t('qr.statusUnpublished')}</Tag>
            )}
            {publication?.published ? (
              <Popconfirm
                title={t('qr.revokeConfirm')}
                onConfirm={() => void onRevoke()}
                okButtonProps={{ danger: true }}
              >
                <Button danger icon={<StopOutlined />} loading={revoke.isPending}>
                  {t('qr.revoke')}
                </Button>
              </Popconfirm>
            ) : (
              <Button
                type="primary"
                icon={<GlobalOutlined />}
                loading={publish.isPending}
                onClick={() => void onPublish()}
              >
                {t('qr.publish')}
              </Button>
            )}
          </Flex>

          {publication?.publishedAt && (
            <Descriptions size="small" column={1} style={{ marginTop: 16 }}>
              <Descriptions.Item label={t('qr.publishedAt')}>
                {formatWhen(publication.publishedAt)}
              </Descriptions.Item>
              {publication.revokedAt && (
                <Descriptions.Item label={t('qr.revokedAt')}>
                  {formatWhen(publication.revokedAt)}
                </Descriptions.Item>
              )}
            </Descriptions>
          )}

          <Divider />

          {code ? (
            <QrCodeSquare code={code} />
          ) : (
            <Typography.Text type="secondary">{t('qr.noCodeHere')}</Typography.Text>
          )}
        </>
      )}
    </Modal>
  );
}
