// SPDX-License-Identifier: AGPL-3.0-or-later
import { AimOutlined, ExportOutlined } from '@ant-design/icons';
import { Alert, Button, Descriptions, Empty, Flex, Spin, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { useCave, useCaveTypes, useEntrances } from '../../api/hooks.ts';
import { formatLonLat } from '../../geo/coords.ts';
import { flyTo } from '../../map/mapContext.ts';
import { useWorkspaceStore } from '../../stores/workspaceStore.ts';

export default function SelectionPanel() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const selection = useWorkspaceStore((s) => s.selection);
  const { data: cave, isPending } = useCave(selection?.caveId);
  const { data: entrances } = useEntrances(selection?.caveId);
  const { data: caveTypes } = useCaveTypes();

  if (!selection) {
    return (
      <Flex align="center" justify="center" style={{ height: '100%', padding: 16 }}>
        <Empty description={t('map.noSelection')} image={Empty.PRESENTED_IMAGE_SIMPLE} />
      </Flex>
    );
  }

  if (isPending || !cave) {
    return (
      <Flex align="center" justify="center" style={{ height: '100%' }}>
        <Spin />
      </Flex>
    );
  }

  const entrance = entrances?.find((e) => e.id === selection.entranceId);
  const typeName = caveTypes?.find((x) => x.id === cave.caveTypeId)?.name;

  return (
    <div style={{ padding: 12, overflow: 'auto', height: '100%' }}>
      <Typography.Title level={5} style={{ marginTop: 0 }}>
        {cave.name}
      </Typography.Title>
      {cave.approximateLocation && (
        <Alert type="warning" showIcon message={t('map.approximate')} style={{ marginBottom: 12 }} />
      )}
      <Descriptions column={1} size="small">
        {typeName && <Descriptions.Item label={t('caves.type')}>{typeName}</Descriptions.Item>}
        {cave.region && <Descriptions.Item label={t('caves.region')}>{cave.region}</Descriptions.Item>}
        {cave.surveyedLength != null && (
          <Descriptions.Item label={t('caves.surveyedLength')}>{cave.surveyedLength}</Descriptions.Item>
        )}
        {cave.depth != null && <Descriptions.Item label={t('caves.depth')}>{cave.depth}</Descriptions.Item>}
        <Descriptions.Item label={t('caves.entrances')}>{cave.entranceCount}</Descriptions.Item>
        {entrance && (
          <Descriptions.Item label={t('entrances.coordinates')}>
            {formatLonLat(entrance.geom.coordinates[0], entrance.geom.coordinates[1])}
          </Descriptions.Item>
        )}
        <Descriptions.Item label={t('caves.visibility')}>
          <Tag>{t(`caves.visibilityValues.${cave.visibility}`)}</Tag>
        </Descriptions.Item>
      </Descriptions>
      <Flex gap={8} style={{ marginTop: 12 }}>
        <Button
          icon={<ExportOutlined />}
          onClick={() => navigate(`/caves/${cave.id}`)}
          type="primary"
          size="small"
        >
          {t('map.openCave')}
        </Button>
        {entrance && (
          <Button
            icon={<AimOutlined />}
            size="small"
            onClick={() => flyTo(entrance.geom.coordinates[0], entrance.geom.coordinates[1], 16)}
          >
            {t('map.zoomTo')}
          </Button>
        )}
      </Flex>
    </div>
  );
}
