// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Descriptions, Drawer, Skeleton, Space, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useSpeologieCave, type SpeologieCave } from '../../api/hooks.ts';

interface Props {
  cave: SpeologieCave | null;
  onClose: () => void;
}

/**
 * Everything the catalogue says about one cave, including the description — which the list
 * deliberately never carries, because one description in that catalogue can be over a megabyte
 * of pasted markup and a page of a hundred rows would carry a hundred of them.
 *
 * The description shown is the description that would be stored: the server converts it with
 * exactly the code the import uses, and shortens it at exactly the same point. A preview that
 * looked better than the result would be worse than no preview.
 */
export default function SpeologieCaveDrawer({ cave, onClose }: Props) {
  const { t } = useTranslation();
  const detail = useSpeologieCave(cave?.id, cave !== null);

  const full = detail.data ?? cave;

  return (
    <Drawer
      open={cave !== null}
      onClose={onClose}
      size="min(720px, 96vw)"
      destroyOnHidden
      title={cave ? t('speologie.detail.title', { name: cave.title }) : undefined}
      extra={
        cave?.url ? (
          <Typography.Link href={cave.url} target="_blank" rel="noreferrer noopener">
            {t('speologie.openInPortal')}
          </Typography.Link>
        ) : null
      }
      data-testid="speologie-cave-drawer"
    >
      {detail.isPending && cave !== null ? <Skeleton active paragraph={{ rows: 6 }} /> : null}

      {full ? (
        <Space orientation="vertical" size="middle" style={{ width: '100%' }}>
          <Space wrap>
            {full.vanished ? <Tag color="red">{t('speologie.flags.vanished')}</Tag> : null}
            {full.sump ? <Tag color="blue">{t('speologie.flags.sump')}</Tag> : null}
            {full.protectionClass ? (
              <Tag color="gold">
                {t('speologie.columns.protectionClass')}: {full.protectionClass}
              </Tag>
            ) : null}
          </Space>

          <Descriptions
            size="small"
            column={2}
            bordered
            title={t('speologie.detail.identity')}
            items={[
              { key: 'id', label: t('speologie.detail.catalogueId'), children: full.id },
              { key: 'slug', label: t('speologie.detail.slug'), children: full.slug ?? '—' },
              { key: 'county', label: t('speologie.columns.county'), children: full.county ?? '—' },
              { key: 'locality', label: t('speologie.detail.locality'), children: full.locality ?? '—' },
              { key: 'mountain', label: t('speologie.columns.mountain'), children: full.mountain ?? '—' },
              { key: 'length', label: t('speologie.columns.length'), children: full.length ?? '—' },
              { key: 'depth', label: t('speologie.columns.depth'), children: full.depth ?? '—' },
              { key: 'altitude', label: t('speologie.columns.altitude'), children: full.altitude ?? '—' },
              {
                key: 'rock',
                label: t('speologie.detail.rockCode'),
                children: full.rockCode ? (
                  <Space orientation="vertical" size={0}>
                    <span>{full.rockCode}</span>
                    <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                      {t('speologie.detail.rockCodeHint')}
                    </Typography.Text>
                  </Space>
                ) : (
                  '—'
                ),
              },
              { key: 'science', label: t('speologie.detail.science'), children: full.science ?? '—' },
              {
                key: 'protectedArea',
                label: t('speologie.detail.protectedAreaCode'),
                children: full.protectedAreaCode ?? '—',
              },
              { key: 'hydroNumber', label: t('speologie.detail.hydroNumber'), children: full.hydroNumber ?? '—' },
              {
                key: 'hydroBasin',
                label: t('speologie.detail.hydroBasin'),
                span: 2,
                children: full.hydroBasin ? (
                  <Space orientation="vertical" size={0}>
                    <span>{full.hydroBasin.name}</span>
                    <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                      {full.hydroBasin.path}
                    </Typography.Text>
                  </Space>
                ) : full.hydroBasinId ? (
                  // The catalogue named a basin this installation's copy of its tree does not
                  // hold — show the number rather than nothing, and say why it is only a number.
                  <Space orientation="vertical" size={0}>
                    <span>{full.hydroBasinId}</span>
                    <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                      {t('speologie.detail.hydroBasinUnknown')}
                    </Typography.Text>
                  </Space>
                ) : (
                  '—'
                ),
              },
            ]}
          />

          <div>
            <Typography.Title level={5}>{t('speologie.detail.description')}</Typography.Title>
            {detail.data?.description ? (
              <>
                <Alert type="info" showIcon title={t('speologie.detail.descriptionNote')} />
                <Typography.Paragraph
                  style={{ whiteSpace: 'pre-wrap', marginTop: 12 }}
                  data-testid="speologie-description"
                >
                  {detail.data.description}
                </Typography.Paragraph>
              </>
            ) : detail.isPending ? null : (
              <Typography.Text type="secondary">{t('speologie.detail.noDescription')}</Typography.Text>
            )}
          </div>
        </Space>
      ) : null}
    </Drawer>
  );
}
