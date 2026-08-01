// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { CameraOutlined, DeleteOutlined, HomeFilled, LinkOutlined, SaveOutlined } from '@ant-design/icons';
import { App, Button, Divider, Flex, Input, List, Tooltip, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useCreateMapView,
  useDeleteMapView,
  useMapViews,
  useShareMapView,
  type MapViewInfo,
} from '../../api/hooks.ts';
import { exportMapImage } from '../../map/viewConfig.ts';

interface ViewsPanelProps {
  onCapture: () => object;
  onApply: (view: MapViewInfo) => void;
}

/** Saved-views section of the layer panel: save current, apply, share, home, export image. */
export default function ViewsPanel({ onCapture, onApply }: ViewsPanelProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data: views } = useMapViews();
  const createView = useCreateMapView();
  const deleteView = useDeleteMapView();
  const shareView = useShareMapView();
  const [name, setName] = useState('');

  const save = async (isHome: boolean) => {
    const trimmed = name.trim();
    if (!trimmed) {
      return;
    }
    try {
      await createView.mutateAsync({
        name: trimmed,
        description: null,
        config: onCapture() as never,
        isHome,
        cavingGroupId: null,
        visibility: 'private',
      });
      setName('');
      message.success(t('common.saved'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const share = async (view: MapViewInfo) => {
    try {
      const updated = await shareView.mutateAsync(view.id);
      const url = `${window.location.origin}/shared/view/${updated.shareToken}`;
      await navigator.clipboard.writeText(url);
      message.success(t('views.linkCopied'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const exportImage = async () => {
    const blob = await exportMapImage();
    if (!blob) {
      message.error(t('common.saveFailed'));
      return;
    }
    const href = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = href;
    anchor.download = `silexgis-map-${new Date().toISOString().slice(0, 10)}.png`;
    anchor.click();
    URL.revokeObjectURL(href);
  };

  return (
    <>
      <Divider style={{ margin: '12px 0' }} />
      <Flex justify="space-between" align="center">
        <Typography.Text strong>{t('views.title')}</Typography.Text>
        <Tooltip title={t('views.exportImage')}>
          <Button size="small" type="text" icon={<CameraOutlined />} onClick={() => void exportImage()} />
        </Tooltip>
      </Flex>
      <Flex gap={4} style={{ marginTop: 8 }}>
        <Input
          size="small"
          placeholder={t('views.namePlaceholder')}
          value={name}
          onChange={(e) => setName(e.target.value)}
          onPressEnter={() => void save(false)}
        />
        <Tooltip title={t('views.save')}>
          <Button size="small" icon={<SaveOutlined />} disabled={!name.trim()} onClick={() => void save(false)} />
        </Tooltip>
      </Flex>
      <List
        size="small"
        dataSource={views}
        locale={{ emptyText: ' ' }}
        renderItem={(view) => (
          <List.Item
            style={{ paddingInline: 0 }}
            actions={[
              <Tooltip key="share" title={t('views.share')}>
                <Button size="small" type="text" icon={<LinkOutlined />} onClick={() => void share(view)} />
              </Tooltip>,
              <Button
                key="delete"
                size="small"
                type="text"
                danger
                icon={<DeleteOutlined />}
                onClick={() => deleteView.mutateAsync(view.id).catch(() => message.error(t('common.saveFailed')))}
              />,
            ]}
          >
            <Typography.Link onClick={() => onApply(view)}>
              {view.isHome && <HomeFilled style={{ marginRight: 4 }} />}
              {view.name}
            </Typography.Link>
          </List.Item>
        )}
      />
    </>
  );
}
