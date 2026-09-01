// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { AimOutlined, ArrowLeftOutlined, ImportOutlined } from '@ant-design/icons';
import {
  Alert,
  App,
  Button,
  Card,
  Checkbox,
  Flex,
  Modal,
  Select,
  Space,
  Statistic,
  Table,
  Tag,
  Tooltip,
  Typography,
} from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate, useSearchParams } from 'react-router-dom';
import {
  useCaveTypes,
  useCavingGroups,
  useImportFromSpeologie,
  useSpeologieCaves,
  useSpeologieStatus,
  type SpeologieAction,
  type SpeologieCave,
  type SpeologieImportResult,
  type Visibility,
} from '../../api/hooks.ts';
import CataloguePlacementMap, {
  type PlacedCave,
} from '../../components/catalogue/CataloguePlacementMap.tsx';
import { speologieErrorMessage } from '../../components/catalogue/errorMessage.ts';

/** What one row of the review has been decided to be. */
interface RowDecision {
  action: SpeologieAction;
  caveTypeCode?: string;
  longitude?: number;
  latitude?: number;
}

const VISIBILITIES: Visibility[] = ['private', 'cavingGroup', 'authenticated', 'public'];

/**
 * Confirming an import from the Romanian catalogue.
 *
 * Which caves are being imported comes from the address, so this screen is reloadable and
 * linkable and there is exactly one route that means "import these" however it was reached.
 *
 * The screen's whole shape is set by one fact about the source: <b>it publishes no coordinates</b>.
 * Every other importer here starts from a position and works out what it is; this one starts from
 * a record and has no position at all. So placing a cave is offered rather than required — a
 * position invented to satisfy a form is worse than an empty field — and the consequence of
 * leaving it empty is stated on the screen instead of being discovered later on a map with
 * nothing on it.
 */
export default function SpeologieImportPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { message } = App.useApp();
  const [params] = useSearchParams();

  const ids = useMemo(
    () =>
      (params.get('ids') ?? '')
        .split(',')
        .map((x) => Number.parseInt(x, 10))
        .filter((x) => Number.isInteger(x) && x > 0),
    [params],
  );

  const status = useSpeologieStatus();
  const caveTypes = useCaveTypes();
  const groups = useCavingGroups();
  const commit = useImportFromSpeologie();

  const [visibility, setVisibility] = useState<Visibility>('private');
  const [cavingGroupId, setCavingGroupId] = useState<string | null>(null);
  const [locationProtected, setLocationProtected] = useState(false);
  const [decisions, setDecisions] = useState<Record<number, RowDecision>>({});
  const [placingId, setPlacingId] = useState<number | null>(null);
  const [result, setResult] = useState<SpeologieImportResult | null>(null);

  const caves = useSpeologieCaves(ids, status.data?.configured === true);
  const loaded = caves.map((q) => q.data).filter((x): x is SpeologieCave => Boolean(x));
  const loading = caves.some((q) => q.isPending);
  const failed = caves.find((q) => q.error)?.error;

  const decisionOf = (cave: SpeologieCave): RowDecision =>
    decisions[cave.id] ?? { action: cave.alreadyImported ? 'update' : 'create' };

  const setDecision = (id: number, patch: Partial<RowDecision>) =>
    setDecisions((current) => ({
      ...current,
      [id]: { ...(current[id] ?? { action: 'create' }), ...patch },
    }));

  const placed: PlacedCave[] = loaded
    .map((cave) => ({ cave, d: decisionOf(cave) }))
    .filter((x) => x.d.longitude !== undefined && x.d.latitude !== undefined)
    .map((x) => ({
      id: x.cave.id,
      title: x.cave.title,
      longitude: x.d.longitude!,
      latitude: x.d.latitude!,
    }));

  const onCommit = async () => {
    try {
      const answer = await commit.mutateAsync({
        selection: loaded.map((c) => c.id),
        decisions: Object.fromEntries(
          loaded.map((cave) => {
            const d = decisionOf(cave);
            return [
              String(cave.id),
              {
                action: d.action,
                caveTypeCode: d.caveTypeCode ?? null,
                longitude: d.longitude ?? null,
                latitude: d.latitude ?? null,
              },
            ];
          }),
        ),
        visibility,
        cavingGroupId,
        locationProtected,
        parentId: null,
      });
      setResult(answer);
    } catch (error) {
      message.error(speologieErrorMessage(error, t) ?? t('speologie.errors.importFailed'));
    }
  };

  if (ids.length === 0) {
    return (
      <Flex vertical gap={16}>
        <Alert type="info" showIcon title={t('speologie.wizard.nothingSelected')} />
        <div>
          <Button icon={<ArrowLeftOutlined />} onClick={() => void navigate('/catalogue/speologie')}>
            {t('speologie.wizard.back')}
          </Button>
        </div>
      </Flex>
    );
  }

  const columns = [
    {
      title: t('speologie.columns.name'),
      key: 'name',
      render: (_: unknown, cave: SpeologieCave) => (
        <Space orientation="vertical" size={0}>
          <Typography.Text strong>{cave.title}</Typography.Text>
          <Typography.Link href={cave.url ?? undefined} target="_blank" rel="noreferrer noopener">
            {cave.url ? t('speologie.openInPortal') : null}
          </Typography.Link>
        </Space>
      ),
    },
    {
      title: t('speologie.wizard.action'),
      key: 'action',
      width: 200,
      render: (_: unknown, cave: SpeologieCave) => (
        <Space orientation="vertical" size={2}>
          <Select<SpeologieAction>
            size="small"
            style={{ width: 160 }}
            value={decisionOf(cave).action}
            onChange={(action) => setDecision(cave.id, { action })}
            data-testid={`speologie-action-${cave.id}`}
            options={[
              { value: 'create', label: t('speologie.wizard.actionCreate'), disabled: cave.alreadyImported },
              { value: 'update', label: t('speologie.wizard.actionUpdate'), disabled: !cave.alreadyImported },
              { value: 'skip', label: t('speologie.wizard.actionSkip') },
            ]}
          />
          {cave.alreadyImported ? (
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              {t('speologie.wizard.actionUpdateHint')}
            </Typography.Text>
          ) : null}
        </Space>
      ),
    },
    {
      title: t('speologie.wizard.caveType'),
      key: 'caveType',
      width: 190,
      render: (_: unknown, cave: SpeologieCave) => (
        <Select
          size="small"
          allowClear
          style={{ width: 170 }}
          value={decisionOf(cave).caveTypeCode}
          onChange={(caveTypeCode) => setDecision(cave.id, { caveTypeCode })}
          placeholder={t('speologie.wizard.caveTypeDerived')}
          options={(caveTypes.data ?? []).map((x) => ({ value: x.code, label: x.name }))}
        />
      ),
    },
    {
      title: t('speologie.wizard.position'),
      key: 'position',
      width: 240,
      render: (_: unknown, cave: SpeologieCave) => {
        const d = decisionOf(cave);
        const isPlaced = d.longitude !== undefined && d.latitude !== undefined;
        return (
          <Space size={4} wrap>
            {isPlaced ? (
              <Tag color="purple">
                {d.latitude!.toFixed(5)}, {d.longitude!.toFixed(5)}
              </Tag>
            ) : (
              <Tag>{t('speologie.wizard.unplaced')}</Tag>
            )}
            <Tooltip title={t('speologie.wizard.placing', { name: cave.title })}>
              <Button
                size="small"
                icon={<AimOutlined />}
                type={placingId === cave.id ? 'primary' : 'default'}
                onClick={() => setPlacingId(placingId === cave.id ? null : cave.id)}
                data-testid={`speologie-place-${cave.id}`}
              >
                {t('speologie.wizard.place')}
              </Button>
            </Tooltip>
            {isPlaced ? (
              <Button
                size="small"
                type="link"
                onClick={() => setDecision(cave.id, { longitude: undefined, latitude: undefined })}
              >
                {t('speologie.wizard.clearPosition')}
              </Button>
            ) : null}
          </Space>
        );
      },
    },
  ];

  return (
    <Flex vertical gap={16}>
      <Flex align="baseline" justify="space-between" wrap gap={8}>
        <Space orientation="vertical" size={0}>
          <Typography.Title level={3} style={{ margin: 0 }}>
            {t('speologie.wizard.title')}
          </Typography.Title>
          <Typography.Text type="secondary">
            {t('speologie.wizard.subtitle', { count: ids.length })}
          </Typography.Text>
        </Space>
        <Space>
          <Button icon={<ArrowLeftOutlined />} onClick={() => void navigate('/catalogue/speologie')}>
            {t('speologie.wizard.back')}
          </Button>
          <Button
            type="primary"
            icon={<ImportOutlined />}
            loading={commit.isPending}
            disabled={loading || loaded.length === 0}
            onClick={() => void onCommit()}
            data-testid="speologie-commit"
          >
            {commit.isPending ? t('speologie.wizard.committing') : t('speologie.wizard.commit')}
          </Button>
        </Space>
      </Flex>

      {failed ? <Alert type="error" showIcon title={speologieErrorMessage(failed, t)} /> : null}

      <Alert
        type="info"
        showIcon
        title={t('speologie.wizard.noCoordinates')}
        description={t('speologie.wizard.noCoordinatesBody')}
        data-testid="speologie-no-coordinates"
      />

      <Card size="small" title={t('speologie.wizard.options')}>
        <Flex gap={16} wrap align="center">
          <Space orientation="vertical" size={2}>
            <Typography.Text type="secondary">{t('caves.visibility')}</Typography.Text>
            <Select<Visibility>
              style={{ width: 200 }}
              value={visibility}
              onChange={setVisibility}
              data-testid="speologie-visibility"
              options={VISIBILITIES.map((v) => ({ value: v, label: t(`caves.visibilityValues.${v}`) }))}
            />
          </Space>
          <Space orientation="vertical" size={2}>
            <Typography.Text type="secondary">{t('nav.cavingGroups')}</Typography.Text>
            <Select
              style={{ width: 240 }}
              allowClear
              value={cavingGroupId}
              onChange={(id) => setCavingGroupId(id ?? null)}
              placeholder={t('speologie.wizard.parentPlaceholder')}
              options={(groups.data ?? []).map((g) => ({ value: g.id, label: g.name }))}
            />
          </Space>
          <Checkbox
            checked={locationProtected}
            onChange={(e) => setLocationProtected(e.target.checked)}
            data-testid="speologie-location-protected"
          >
            {t('caves.fields.locationProtected')}
          </Checkbox>
        </Flex>
      </Card>

      <Flex gap={16} wrap align="start">
        <div style={{ flex: '1 1 640px', minWidth: 400 }}>
          <Table<SpeologieCave>
            rowKey="id"
            size="small"
            scroll={{ x: 'max-content' }}
            loading={loading}
            dataSource={loaded}
            columns={columns}
            pagination={false}
            data-testid="speologie-candidates"
          />
        </div>
        <Card size="small" style={{ flex: '1 1 380px', minWidth: 320 }} title={t('speologie.wizard.position')}>
          <CataloguePlacementMap
            placed={placed}
            placingId={placingId}
            onPlace={(id, [lon, lat]) => {
              setDecision(id, { longitude: lon, latitude: lat });
              setPlacingId(null);
            }}
          />
        </Card>
      </Flex>

      <Modal
        open={result !== null}
        destroyOnHidden
        title={t('speologie.result.title')}
        onCancel={() => setResult(null)}
        footer={[
          <Button key="caves" type="primary" onClick={() => void navigate('/caves?unplaced=true')}>
            {t('speologie.result.viewCaves')}
          </Button>,
        ]}
      >
        {result ? (
          <Space orientation="vertical" size="middle" style={{ width: '100%' }}>
            <Flex gap={24}>
              <Statistic title={t('speologie.result.created')} value={result.createdCount} />
              <Statistic title={t('speologie.result.updated')} value={result.updatedCount} />
              <Statistic title={t('speologie.result.skipped')} value={result.skippedCount} />
            </Flex>
            {result.failures.length > 0 ? (
              <div>
                <Typography.Text strong>{t('speologie.result.failures')}</Typography.Text>
                <ul>
                  {result.failures.map((f) => (
                    <li key={f.speologieId}>
                      {f.title ?? f.speologieId}: {f.reason}
                    </li>
                  ))}
                </ul>
              </div>
            ) : null}
            <Typography.Text type="secondary">{t('speologie.result.undoHint')}</Typography.Text>
          </Space>
        ) : null}
      </Modal>
    </Flex>
  );
}
