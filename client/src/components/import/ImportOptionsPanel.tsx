// SPDX-License-Identifier: AGPL-3.0-or-later
import { Card, Checkbox, Col, Collapse, Form, Input, Row, Select, Slider } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useCavingGroups,
  useFeatureTypes,
  useGeofileColumns,
  useTermRuleSets,
  type ImportOptions,
} from '../../api/hooks.ts';

interface Props {
  options: ImportOptions;
  onChange: (next: ImportOptions) => void;
  geofileId?: string;
  /** Only a delimited upload has columns to choose between; asking about anything else is a 400. */
  isDelimited: boolean;
}

/**
 * The choices an import makes once for the whole file: which rules run, what everything it
 * creates is bound to, what happens to altitude and to tracks, and how far duplicate
 * detection looks.
 *
 * Visibility defaults to the most restrictive value rather than to the file's own. An import
 * is a bulk action, and a bulk action that publishes by default publishes a whole trip's worth
 * of holes at once.
 */
export default function ImportOptionsPanel({ options, onChange, geofileId, isDelimited }: Props) {
  const { t } = useTranslation();
  const ruleSets = useTermRuleSets();
  const featureTypes = useFeatureTypes();
  const cavingGroups = useCavingGroups();
  // Asked only of a delimited upload. Everything else names its fields inside its own rows, and
  // asking anyway would put a refused request in the browser's console on every review of a GPX.
  const columns = useGeofileColumns(geofileId, isDelimited).data?.columns ?? [];

  const set = <K extends keyof ImportOptions>(key: K, value: ImportOptions[K]) =>
    onChange({ ...options, [key]: value });

  return (
    <Card size="small" styles={{ body: { paddingBlock: 8 } }}>
      <Collapse
        ghost
        defaultActiveKey={['rules']}
        items={[
          {
            key: 'rules',
            label: t('vectorImport.optionsRules'),
            children: (
              <Row gutter={[16, 8]}>
                <Col xs={24} md={12}>
                  <Form.Item label={t('vectorImport.ruleSet')} style={{ marginBottom: 8 }}>
                    <Select
                      value={options.termRuleSetId ?? undefined}
                      onChange={(value: string) => set('termRuleSetId', value)}
                      loading={ruleSets.isLoading}
                      data-testid="import-rule-set"
                      options={(ruleSets.data ?? []).map((s) => ({
                        value: s.id,
                        label: s.isDefault ? `${s.name} ★` : s.name,
                      }))}
                    />
                  </Form.Item>
                </Col>
                <Col xs={24} md={12}>
                  <Form.Item
                    label={t('vectorImport.languages')}
                    tooltip={t('vectorImport.languagesHint')}
                    style={{ marginBottom: 8 }}
                  >
                    <Select
                      mode="multiple"
                      allowClear
                      value={options.languages ?? []}
                      onChange={(value: string[]) => set('languages', value)}
                      placeholder={t('vectorImport.languagesAll')}
                      options={[
                        { value: 'ro', label: 'Română' },
                        { value: 'en', label: 'English' },
                      ]}
                    />
                  </Form.Item>
                </Col>
                <Col xs={24} md={12}>
                  <Form.Item label={t('vectorImport.namePrefix')} style={{ marginBottom: 8 }}>
                    <Input
                      value={options.namePrefix ?? ''}
                      maxLength={100}
                      onChange={(e) => set('namePrefix', e.target.value || null)}
                    />
                  </Form.Item>
                </Col>
                <Col xs={24} md={12}>
                  <Form.Item
                    label={t('vectorImport.duplicateRadius')}
                    tooltip={t('vectorImport.duplicateRadiusHint')}
                    style={{ marginBottom: 8 }}
                  >
                    <Slider
                      min={0}
                      max={500}
                      step={5}
                      value={options.duplicateRadiusMeters ?? 50}
                      onChange={(value: number) => set('duplicateRadiusMeters', value)}
                      tooltip={{ formatter: (value) => `${value} m` }}
                    />
                  </Form.Item>
                </Col>
              </Row>
            ),
          },
          {
            key: 'created',
            label: t('vectorImport.optionsCreated'),
            children: (
              <Row gutter={[16, 8]}>
                <Col xs={24} md={8}>
                  <Form.Item label={t('features.visibility')} style={{ marginBottom: 8 }}>
                    <Select
                      value={options.visibility ?? 'private'}
                      onChange={(value) => set('visibility', value)}
                      data-testid="import-visibility"
                      options={(['private', 'cavingGroup', 'authenticated', 'public'] as const).map((v) => ({
                        value: v,
                        label: t(`caves.visibilityValues.${v}`),
                      }))}
                    />
                  </Form.Item>
                </Col>
                <Col xs={24} md={8}>
                  <Form.Item label={t('documents.cavingGroup')} style={{ marginBottom: 8 }}>
                    <Select
                      allowClear
                      value={options.cavingGroupId ?? undefined}
                      onChange={(value?: string) => set('cavingGroupId', value ?? null)}
                      loading={cavingGroups.isLoading}
                      options={(cavingGroups.data ?? []).map((g) => ({ value: g.id, label: g.name }))}
                    />
                  </Form.Item>
                </Col>
                <Col xs={24} md={8}>
                  <Form.Item
                    label={t('vectorImport.elevation')}
                    tooltip={t('vectorImport.elevationHint')}
                    style={{ marginBottom: 8 }}
                  >
                    <Select
                      value={options.elevation ?? 'keep'}
                      onChange={(value) => set('elevation', value)}
                      data-testid="import-elevation"
                      options={[
                        { value: 'keep', label: t('vectorImport.elevationKeep') },
                        { value: 'discard', label: t('vectorImport.elevationDiscard') },
                        { value: 'perCandidate', label: t('vectorImport.elevationPerCandidate') },
                      ]}
                    />
                  </Form.Item>
                </Col>
                <Col xs={24}>
                  <Checkbox
                    checked={options.locationProtected ?? false}
                    onChange={(e) => set('locationProtected', e.target.checked)}
                  >
                    {t('vectorImport.locationProtected')}
                  </Checkbox>
                </Col>
              </Row>
            ),
          },
          {
            key: 'tracks',
            label: t('vectorImport.optionsTracks'),
            children: (
              <Row gutter={[16, 8]}>
                <Col xs={24} md={12}>
                  <Form.Item label={t('vectorImport.tracks')} style={{ marginBottom: 8 }}>
                    <Select
                      value={options.tracks ?? 'ignore'}
                      onChange={(value) => set('tracks', value)}
                      data-testid="import-tracks"
                      options={[
                        { value: 'ignore', label: t('vectorImport.tracksIgnore') },
                        { value: 'importAsLine', label: t('vectorImport.tracksAsLine') },
                      ]}
                    />
                  </Form.Item>
                </Col>
                <Col xs={24} md={12}>
                  <Form.Item label={t('vectorImport.trackKind')} style={{ marginBottom: 8 }}>
                    <Select
                      allowClear
                      disabled={options.tracks !== 'importAsLine'}
                      value={options.trackFeatureTypeCode ?? undefined}
                      onChange={(value?: string) => set('trackFeatureTypeCode', value ?? null)}
                      options={(featureTypes.data ?? [])
                        .filter((type) => type.acceptedGeometryClasses.some((c) => c.includes('ine')))
                        .map((type) => ({ value: type.code, label: type.name }))}
                    />
                  </Form.Item>
                </Col>
              </Row>
            ),
          },
          {
            key: 'mapping',
            label: t('vectorImport.optionsMapping'),
            children: (
              <Row gutter={[16, 8]}>
                {(['nameField', 'descriptionField', 'codeField', 'elevationField'] as const).map((field) => (
                  <Col xs={24} md={12} key={field}>
                    <Form.Item label={t(`vectorImport.mapping.${field}`)} style={{ marginBottom: 8 }}>
                      {/* A delimited file knows its own columns, so the mapping is a choice
                          rather than a name to remember and type correctly. Everything else
                          names its fields inside the rows, so free text it is. */}
                      {columns.length > 0 ? (
                        <Select
                          allowClear
                          showSearch
                          placeholder={t('vectorImport.mappingAuto')}
                          value={options.mapping?.[field] ?? undefined}
                          onChange={(value?: string) =>
                            set('mapping', { ...options.mapping, [field]: value ?? null })
                          }
                          options={columns.map((column) => ({ value: column, label: column }))}
                        />
                      ) : (
                        <Input
                          placeholder={t('vectorImport.mappingAuto')}
                          value={options.mapping?.[field] ?? ''}
                          onChange={(e) =>
                            set('mapping', { ...options.mapping, [field]: e.target.value || null })
                          }
                        />
                      )}
                    </Form.Item>
                  </Col>
                ))}
              </Row>
            ),
          },
        ]}
      />
    </Card>
  );
}
