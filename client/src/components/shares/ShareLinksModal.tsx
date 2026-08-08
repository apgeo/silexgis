// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { CopyOutlined, LinkOutlined } from '@ant-design/icons';
import { Alert, App, Button, Checkbox, Flex, Input, Modal, Popconfirm, Select, Table, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useCreateFeatureShare,
  useFeatureShares,
  useRevokeFeatureShare,
  type FeatureShare,
  type FeatureShareCreated,
} from '../../api/hooks.ts';

interface ShareLinksModalProps {
  /** The shared entity's feature id (a cave, entrance, centerline or generic feature). */
  featureId: string;
  open: boolean;
  onClose: () => void;
}

/** Public URL a minted token resolves to — the anonymous shared-feature page. */
function shareUrl(token: string): string {
  return `${window.location.origin}/shared/features/${token}`;
}

/**
 * Share-link manager for one feature: mint a public or login-required link, list the
 * existing links, revoke. The token is returned exactly once at mint time — it is shown
 * here for copying and never retrievable again, which is why the list carries metadata only.
 */
export default function ShareLinksModal({ featureId, open, onClose }: ShareLinksModalProps) {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const { data: shares, isError } = useFeatureShares(featureId, open);
  const createShare = useCreateFeatureShare();
  const revokeShare = useRevokeFeatureShare();

  const [mode, setMode] = useState<FeatureShare['mode']>('public');
  const [includeSubtree, setIncludeSubtree] = useState(true);
  // The one link whose token we still know — the one minted in this dialog session.
  const [minted, setMinted] = useState<FeatureShareCreated | null>(null);

  const onCreate = async () => {
    try {
      const created = await createShare.mutateAsync({
        id: featureId,
        body: { mode, includeSubtree },
      });
      setMinted(created);
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const copyMinted = async () => {
    if (!minted) {
      return;
    }
    try {
      await navigator.clipboard.writeText(shareUrl(minted.token));
      message.success(t('shares.linkCopied'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const onRevoke = async (share: FeatureShare) => {
    try {
      await revokeShare.mutateAsync({ id: featureId, shareId: share.id });
      if (minted?.id === share.id) {
        setMinted(null);
      }
      message.success(t('shares.revoked'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const formatWhen = (iso: string) => new Date(iso).toLocaleString(i18n.resolvedLanguage);

  return (
    <Modal
      title={t('shares.title')}
      open={open}
      onCancel={() => {
        // The token is gone once the dialog closes — deliberate, it should not linger on screen.
        setMinted(null);
        onClose();
      }}
      footer={null}
      width={680}
      destroyOnHidden
    >
      {isError ? (
        <Tag color="warning">{t('shares.notAllowed')}</Tag>
      ) : (
        <>
          <Flex gap={8} align="center" wrap style={{ marginBottom: 12 }}>
            <Select
              value={mode}
              style={{ width: 200 }}
              onChange={setMode}
              options={[
                { value: 'public', label: t('shares.modes.public') },
                { value: 'requiresLogin', label: t('shares.modes.requiresLogin') },
              ]}
            />
            <Checkbox checked={includeSubtree} onChange={(e) => setIncludeSubtree(e.target.checked)}>
              {t('shares.includeSubtree')}
            </Checkbox>
            <Button
              type="primary"
              icon={<LinkOutlined />}
              loading={createShare.isPending}
              onClick={() => void onCreate()}
            >
              {t('shares.create')}
            </Button>
          </Flex>

          {minted && (
            <Alert
              type="success"
              showIcon
              style={{ marginBottom: 12 }}
              title={t('shares.linkReady')}
              description={
                <>
                  <Flex gap={8} style={{ marginTop: 4 }}>
                    <Input readOnly value={shareUrl(minted.token)} onFocus={(e) => e.target.select()} />
                    <Button icon={<CopyOutlined />} onClick={() => void copyMinted()}>
                      {t('shares.copy')}
                    </Button>
                  </Flex>
                  <Typography.Text type="secondary">{t('shares.tokenOnce')}</Typography.Text>
                </>
              }
            />
          )}

          <Table<FeatureShare>
            scroll={{ x: 'max-content' }}
            rowKey="id"
            size="small"
            pagination={false}
            dataSource={shares}
            locale={{ emptyText: t('shares.empty') }}
            columns={[
              {
                title: t('shares.mode'),
                dataIndex: 'mode',
                render: (value: FeatureShare['mode']) => (
                  <Tag color={value === 'public' ? 'green' : 'blue'}>{t(`shares.modes.${value}`)}</Tag>
                ),
              },
              {
                title: t('shares.includeSubtree'),
                dataIndex: 'includeSubtree',
                width: 130,
                align: 'center',
                render: (value: boolean) => (value ? '✓' : ''),
              },
              {
                title: t('shares.createdAt'),
                dataIndex: 'createdAt',
                width: 170,
                render: formatWhen,
              },
              {
                title: t('shares.status'),
                key: 'status',
                width: 110,
                render: (_, share) =>
                  share.revokedAt ? (
                    <Tag>{t('shares.statusRevoked')}</Tag>
                  ) : (
                    <Tag color="success">{t('shares.statusActive')}</Tag>
                  ),
              },
              {
                key: 'actions',
                width: 100,
                render: (_, share) =>
                  share.revokedAt ? null : (
                    <Popconfirm
                      title={t('shares.revokeConfirm')}
                      onConfirm={() => void onRevoke(share)}
                      okButtonProps={{ danger: true }}
                    >
                      <Button size="small" danger>
                        {t('shares.revoke')}
                      </Button>
                    </Popconfirm>
                  ),
              },
            ]}
          />
        </>
      )}
    </Modal>
  );
}
