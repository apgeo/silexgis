// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Card, Checkbox, Col, Collapse, Form, Input, InputNumber, Row, Select, Slider } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useCavingGroups,
  usePhotoImportTracks,
  type ImportTargetKind,
  type PhotoImportOptions,
  type PhotoPreview,
} from '../../api/hooks.ts';

interface Props {
  options: PhotoImportOptions;
  onChange: (next: PhotoImportOptions) => void;
  preview?: PhotoPreview;
}

/**
 * The choices a photo review makes once for the whole drop: what the places become and are
 * bound to, how far apart two pictures are still one place, how far it looks for something
 * already in the registry, and how a picture with no fix of its own may get one.
 *
 * Visibility defaults to the most restrictive value rather than to anything the pictures carry.
 * A drop is a bulk action, and a bulk action that publishes by default publishes a whole trip's
 * worth of holes at once.
 */
export default function PhotoImportOptionsPanel({ options, onChange, preview }: Props) {
  const { t } = useTranslation();
  const cavingGroups = useCavingGroups();
  const tracks = usePhotoImportTracks();

  const set = <K extends keyof PhotoImportOptions>(key: K, value: PhotoImportOptions[K]) =>
    onChange({ ...options, [key]: value });

  // A track was chosen and it holds no times: that is a fact about the file, not a failure of
  // the review, and saying so is the difference between "nothing matched" and "nothing could".
  const trackHasNoTimes = Boolean(options.trackGeofileId) && preview?.trackFixCount === 0;

  return (
    <Card size="small" styles={{ body: { paddingBlock: 8 } }}>
      <Collapse
        ghost
        defaultActiveKey={['places']}
        items={[
          {
            key: 'places',
            label: t('photoImport.optionsPlaces'),
            children: (
              <Row gutter={[16, 8]}>
                <Col xs={24} md={12}>
                  <Form.Item label={t('photoImport.defaultKind')} style={{ marginBottom: 8 }}>
                    <Select<ImportTargetKind>
                      value={options.defaultKind ?? 'caveEntrance'}
                      onChange={(value) => set('defaultKind', value)}
                      data-testid="photo-default-kind"
                      options={(['cave', 'caveEntrance', 'surfaceFeature'] as const).map((kind) => ({
                        value: kind,
                        label: t(`vectorImport.kinds.${kind}`),
                      }))}
                    />
                  </Form.Item>
                </Col>
                <Col xs={24} md={12}>
                  <Form.Item label={t('photoImport.namePrefix')} style={{ marginBottom: 8 }}>
                    <Input
                      value={options.namePrefix ?? ''}
                      maxLength={100}
                      onChange={(e) => set('namePrefix', e.target.value || null)}
                    />
                  </Form.Item>
                </Col>
                <Col xs={24} md={12}>
                  <Form.Item
                    label={t('photoImport.clusterRadius')}
                    tooltip={t('photoImport.clusterRadiusHint')}
                    style={{ marginBottom: 8 }}
                  >
                    <Slider
                      min={0}
                      max={200}
                      value={options.clusterRadiusMeters ?? 25}
                      onChange={(value: number) => set('clusterRadiusMeters', value)}
                      tooltip={{ formatter: (value) => `${value} m` }}
                    />
                  </Form.Item>
                </Col>
                <Col xs={24} md={12}>
                  <Form.Item
                    label={t('photoImport.proximityRadius')}
                    tooltip={t('photoImport.proximityRadiusHint')}
                    style={{ marginBottom: 8 }}
                  >
                    <Slider
                      min={0}
                      max={500}
                      value={options.proximityRadiusMeters ?? 80}
                      onChange={(value: number) => set('proximityRadiusMeters', value)}
                      tooltip={{ formatter: (value) => `${value} m` }}
                    />
                  </Form.Item>
                </Col>
              </Row>
            ),
          },
          {
            key: 'created',
            label: t('photoImport.optionsCreated'),
            children: (
              <Row gutter={[16, 8]}>
                <Col xs={24} md={12}>
                  <Form.Item label={t('features.visibility')} style={{ marginBottom: 8 }}>
                    <Select
                      value={options.visibility ?? 'private'}
                      onChange={(value) => set('visibility', value)}
                      data-testid="photo-visibility"
                      options={(['private', 'cavingGroup', 'authenticated', 'public'] as const).map((v) => ({
                        value: v,
                        label: t(`caves.visibilityValues.${v}`),
                      }))}
                    />
                  </Form.Item>
                </Col>
                <Col xs={24} md={12}>
                  <Form.Item label={t('documents.cavingGroup')} style={{ marginBottom: 8 }}>
                    <Select
                      allowClear
                      value={options.cavingGroupId ?? undefined}
                      onChange={(value: string | undefined) => set('cavingGroupId', value ?? null)}
                      loading={cavingGroups.isLoading}
                      options={(cavingGroups.data ?? []).map((group) => ({
                        value: group.id,
                        label: group.name,
                      }))}
                    />
                  </Form.Item>
                </Col>
                <Col xs={24} md={12}>
                  <Form.Item
                    label={t('photoImport.elevation')}
                    tooltip={t('photoImport.elevationHint')}
                    style={{ marginBottom: 8 }}
                  >
                    <Select
                      value={options.elevation ?? 'discard'}
                      onChange={(value) => set('elevation', value)}
                      options={[
                        { value: 'keep', label: t('vectorImport.elevationKeep') },
                        { value: 'discard', label: t('vectorImport.elevationDiscard') },
                      ]}
                    />
                  </Form.Item>
                </Col>
                <Col xs={24} md={12}>
                  <Form.Item style={{ marginBottom: 8, marginTop: 30 }}>
                    <Checkbox
                      checked={options.locationProtected ?? false}
                      onChange={(e) => set('locationProtected', e.target.checked)}
                    >
                      {t('vectorImport.locationProtected')}
                    </Checkbox>
                  </Form.Item>
                </Col>
              </Row>
            ),
          },
          {
            key: 'clock',
            label: t('photoImport.optionsClock'),
            children: (
              <Row gutter={[16, 8]}>
                <Col span={24}>
                  <Alert
                    type="info"
                    showIcon
                    style={{ marginBottom: 12 }}
                    title={t('photoImport.clockIntro')}
                  />
                </Col>
                <Col xs={24} md={12}>
                  <Form.Item label={t('photoImport.track')} style={{ marginBottom: 8 }}>
                    <Select
                      allowClear
                      value={options.trackGeofileId ?? undefined}
                      onChange={(value: string | undefined) => set('trackGeofileId', value ?? null)}
                      loading={tracks.isLoading}
                      placeholder={t('photoImport.trackNone')}
                      data-testid="photo-track"
                      options={(tracks.data ?? []).map((track) => ({
                        value: track.geofileId,
                        label: track.name,
                      }))}
                    />
                  </Form.Item>
                </Col>
                <Col xs={24} md={12}>
                  <Form.Item
                    label={t('photoImport.clockOffset')}
                    tooltip={t('photoImport.clockOffsetHint')}
                    style={{ marginBottom: 8 }}
                  >
                    <InputNumber
                      style={{ width: '100%' }}
                      value={options.cameraClockOffsetSeconds ?? 0}
                      step={60}
                      min={-86400}
                      max={86400}
                      onChange={(value) => set('cameraClockOffsetSeconds', value ?? 0)}
                      addonAfter={t('photoImport.seconds')}
                    />
                  </Form.Item>
                </Col>
                <Col xs={24} md={12}>
                  <Form.Item
                    label={t('photoImport.matchTolerance')}
                    tooltip={t('photoImport.matchToleranceHint')}
                    style={{ marginBottom: 8 }}
                  >
                    <InputNumber
                      style={{ width: '100%' }}
                      value={options.trackMatchToleranceSeconds ?? 120}
                      step={30}
                      min={0}
                      max={86400}
                      onChange={(value) => set('trackMatchToleranceSeconds', value ?? 120)}
                      addonAfter={t('photoImport.seconds')}
                    />
                  </Form.Item>
                </Col>
                {trackHasNoTimes && (
                  <Col span={24}>
                    <Alert type="warning" showIcon title={t('photoImport.trackHasNoTimes')} />
                  </Col>
                )}
                {preview && preview.trackFixCount > 0 && (
                  <Col span={24}>
                    <Alert
                      type="success"
                      showIcon
                      title={t('photoImport.trackFixes', { count: preview.trackFixCount })}
                    />
                  </Col>
                )}
              </Row>
            ),
          },
        ]}
      />
    </Card>
  );
}
