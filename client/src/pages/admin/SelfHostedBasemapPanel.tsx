// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { Alert, App, Card, Descriptions, Flex, InputNumber, Progress, Radio, Select, Tag, Typography } from 'antd';
import { useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';
import { queryKeys } from '../../api/hooks/queryKeys.ts';
import {
  isUnderway,
  useBasemapBuilds,
  useBasemapStatus,
  useCancelBasemapDownload,
  useEstimateBasemap,
  useStartBasemapDownload,
  type BasemapDownload,
  type BasemapEstimate,
  type BasemapRegion,
} from '../../api/hooks/basemap.ts';
import Button from '../../components/Button.tsx';
import { formatBytes } from './basemapSize.ts';

/** The regions offered by name, in the order the page lists them, and a box of one's own. */
type RegionChoice = 'romania' | 'apuseni' | 'custom';

/** The refusal codes a download or an estimate can end with, each said in words of its own. */
const REFUSALS = [
  'basemap.too_large',
  'basemap.disk_full',
  'basemap.source_unreachable',
  'basemap.build_not_found',
  'basemap.cancelled',
  'basemap.extract_failed',
  'basemap.download_running',
  'basemap.self_hosting_off',
] as const;

/** A box as west, south, east, north, in degrees. */
function formatRegion(region: BasemapRegion): string {
  return [region.west, region.south, region.east, region.north].map((v) => v.toFixed(2)).join(', ');
}

/**
 * The installation's own vector basemap: what is served now, and the download that replaces it.
 *
 * A region of a published planet build is cut out on the server, by a background job, and becomes
 * what the catalogue's self-hosted entry draws; until the first download that entry is not offered
 * on the map at all, which is said here. The source is the one the installation's configuration
 * names — a date and a box are chosen here, never an address — and nothing is fetched until the
 * administrator asks: weighing a region reads only the build's directories.
 */
export default function SelfHostedBasemapPanel() {
  const { t, i18n } = useTranslation();
  const { message, modal } = App.useApp();
  const { data: status, isError } = useBasemapStatus();
  const [region, setRegion] = useState<RegionChoice>('apuseni');
  // A cleared side stays cleared rather than turning into 0, which is a real meridian or the equator
  // and would quietly ask for a very different box.
  const [custom, setCustom] = useState<Record<keyof BasemapRegion, number | null>>({
    west: 22.2, south: 46.2, east: 23.4, north: 47.0,
  });
  const [maxZoom, setMaxZoom] = useState(15);
  const [build, setBuild] = useState<string | null>(null);
  const [buildsWanted, setBuildsWanted] = useState(false);
  const builds = useBasemapBuilds(buildsWanted);
  const estimate = useEstimateBasemap();
  const start = useStartBasemapDownload();
  const cancel = useCancelBasemapDownload();
  const [weighed, setWeighed] = useState<BasemapEstimate | null>(null);
  const queryClient = useQueryClient();

  // A download that has just finished is what puts the self-hosted entry on the map, so the
  // published list is read again as soon as this page sees it finish.
  const finished = status?.download?.state === 'succeeded' ? status.download.id : null;
  useEffect(() => {
    if (finished !== null) {
      void queryClient.invalidateQueries({ queryKey: queryKeys.mapLayers });
    }
  }, [finished, queryClient]);

  // What is wrong with the box of one's own, said before anything is asked of the server.
  const customProblem =
    region !== 'custom'
      ? null
      : custom.west === null || custom.south === null || custom.east === null || custom.north === null
        ? t('admin.basemap.boxIncomplete')
        : custom.west >= custom.east || custom.south >= custom.north
          ? t('admin.basemap.boxInverted')
          : null;
  const chosen = useMemo<BasemapRegion>(() => {
    const preset = status?.presets.find((p) => p.key === region);
    return preset?.region ?? {
      west: custom.west ?? 0, south: custom.south ?? 0, east: custom.east ?? 0, north: custom.north ?? 0,
    };
  }, [status, region, custom]);
  const request = { region: chosen, maxZoom, build };
  const download = status?.download ?? null;
  const underway = isUnderway(download);
  const date = (value: string | null | undefined) => (value ? new Date(value).toLocaleString(i18n.resolvedLanguage) : '—');
  // A code with words of its own is said in them; anything else — a refusal nobody has worded, or
  // a request that never reached the server — is said as a failure of the request, not as an
  // extract that was attempted and failed.
  const refusal = (code: string | null | undefined) =>
    code && (REFUSALS as readonly string[]).includes(code)
      ? t(`admin.basemap.refusal.${code.slice('basemap.'.length)}`)
      : t('admin.basemap.requestFailed');
  // A download that ended in failure did attempt the extract, so a failure with no worded code is
  // said as the extract's.
  const downloadRefusal = (code: string | null | undefined) =>
    code && (REFUSALS as readonly string[]).includes(code) ? refusal(code) : t('admin.basemap.refusal.extract_failed');

  if (isError) {
    return <Alert type="error" showIcon title={t('admin.basemap.loadFailed')} />;
  }
  if (!status) {
    return null;
  }

  const weigh = async () => {
    setWeighed(null);
    try {
      setWeighed(await estimate.mutateAsync(request));
    } catch (e) {
      message.error(refusal(e instanceof ApiError ? e.code : null));
    }
  };

  // Stopping a download that may be hours and gigabytes in is asked once, and its failure said.
  const stop = (id: BasemapDownload['id']) => {
    modal.confirm({
      title: t('admin.basemap.cancelTitle'),
      content: t('admin.basemap.cancelBody'),
      okText: t('admin.basemap.cancel'),
      okButtonProps: { danger: true },
      onOk: async () => {
        try {
          await cancel.mutateAsync(id);
        } catch (e) {
          message.error(e instanceof ApiError && e.code ? refusal(e.code) : t('admin.basemap.cancelFailed'));
        }
      },
    });
  };

  const begin = () => {
    const go = async () => {
      try {
        await start.mutateAsync(request);
        setWeighed(null);
      } catch (e) {
        message.error(refusal(e instanceof ApiError ? e.code : null));
      }
    };
    if (!status.current) {
      void go();
      return;
    }
    modal.confirm({
      title: t('admin.basemap.replaceTitle'),
      content: t('admin.basemap.replaceBody'),
      okText: t('admin.basemap.download'),
      onOk: go,
    });
  };

  return (
    <Card title={t('admin.basemap.title')} data-testid="basemap-panel" style={{ maxWidth: 860 }}>
      <Flex vertical gap={16}>
        <Typography.Paragraph type="secondary" style={{ margin: 0 }}>
          {t('admin.basemap.intro')}
        </Typography.Paragraph>

        {!status.selfHosted && (
          <Alert type="info" showIcon title={t('admin.basemap.off')} data-testid="basemap-off" />
        )}

        {status.selfHosted && !status.current && (
          <Alert
            type="warning"
            showIcon
            data-testid="basemap-none"
            title={t('admin.basemap.none')}
            description={
              status.entries.length > 0
                ? t('admin.basemap.waiting', { entries: status.entries.join(', ') })
                : t('admin.basemap.noEntry')
            }
          />
        )}

        {status.current && (
          <Descriptions
            size="small"
            column={1}
            bordered
            data-testid="basemap-current"
            title={t('admin.basemap.current')}
            items={[
              { key: 'build', label: t('admin.basemap.build'), children: status.current.build },
              { key: 'size', label: t('admin.basemap.size'), children: formatBytes(status.current.sizeBytes) },
              { key: 'at', label: t('admin.basemap.downloadedAt'), children: date(status.current.extractedAt) },
              {
                key: 'region',
                label: t('admin.basemap.region'),
                children: status.current.region ? formatRegion(status.current.region) : '—',
              },
              { key: 'zoom', label: t('admin.basemap.maxZoom'), children: status.current.maxZoom ?? '—' },
            ]}
          />
        )}

        {download && <DownloadState download={download} refusal={downloadRefusal} onCancel={() => stop(download.id)} />}

        {status.selfHosted && (
          <Flex vertical gap={12} data-testid="basemap-form">
            <Typography.Text strong>{t('admin.basemap.newDownload')}</Typography.Text>
            <Radio.Group
              value={region}
              onChange={(e) => {
                setRegion(e.target.value as RegionChoice);
                setWeighed(null);
              }}
              optionType="button"
              options={[
                { value: 'romania', label: t('admin.basemap.preset.romania') },
                { value: 'apuseni', label: t('admin.basemap.preset.apuseni') },
                { value: 'custom', label: t('admin.basemap.preset.custom') },
              ]}
              data-testid="basemap-region"
            />
            {region === 'custom' && (
              <Flex gap={8} wrap data-testid="basemap-custom">
                {(['west', 'south', 'east', 'north'] as const).map((side) => (
                  <Labelled key={side} label={t(`admin.basemap.side.${side}`)}>
                    <InputNumber
                      value={custom[side]}
                      min={side === 'west' || side === 'east' ? -180 : -85}
                      max={side === 'west' || side === 'east' ? 180 : 85}
                      step={0.1}
                      onChange={(value) => {
                        setCustom((c) => ({ ...c, [side]: value ?? null }));
                        setWeighed(null);
                      }}
                      aria-label={t(`admin.basemap.side.${side}`)}
                      data-testid={`basemap-side-${side}`}
                    />
                  </Labelled>
                ))}
                {customProblem && (
                  <Typography.Text type="danger" data-testid="basemap-box-problem">
                    {customProblem}
                  </Typography.Text>
                )}
              </Flex>
            )}
            <Flex gap={12} wrap align="center">
              <Labelled label={t('admin.basemap.maxZoom')}>
                <InputNumber
                  min={0}
                  max={status.maxZoom}
                  value={maxZoom}
                  onChange={(value) => {
                    setMaxZoom(value ?? status.maxZoom);
                    setWeighed(null);
                  }}
                  aria-label={t('admin.basemap.maxZoom')}
                  data-testid="basemap-max-zoom"
                />
              </Labelled>
              <Labelled label={t('admin.basemap.build')}>
                <Select<string>
                  style={{ minWidth: 220 }}
                  value={build ?? ''}
                  onOpenChange={(open) => open && setBuildsWanted(true)}
                  loading={builds.isFetching}
                  aria-label={t('admin.basemap.build')}
                  data-testid="basemap-build"
                  onChange={(value) => {
                    setBuild(value === '' ? null : value);
                    setWeighed(null);
                  }}
                  options={[
                    { value: '', label: t('admin.basemap.latest') },
                    ...(builds.data ?? []).map((b) => ({ value: b.build, label: b.build })),
                  ]}
                  notFoundContent={builds.isError ? t('admin.basemap.buildsFailed') : undefined}
                />
              </Labelled>
            </Flex>
            <Flex gap={8} wrap align="center">
              <Button
                onClick={() => void weigh()}
                loading={estimate.isPending}
                disabled={underway || customProblem !== null}
                data-testid="basemap-estimate"
              >
                {t('admin.basemap.estimate')}
              </Button>
              <Button
                type="primary"
                onClick={begin}
                loading={start.isPending}
                disabled={underway || customProblem !== null || weighed?.refusalCode != null}
                data-testid="basemap-download"
              >
                {t('admin.basemap.download')}
              </Button>
              {weighed && (
                <Typography.Text
                  type={weighed.refusalCode ? 'danger' : 'secondary'}
                  data-testid="basemap-estimate-result"
                >
                  {weighed.refusalCode
                    ? `${t('admin.basemap.estimated', { size: formatBytes(weighed.bytes), build: weighed.build })} ${refusal(weighed.refusalCode)}`
                    : t('admin.basemap.estimated', { size: formatBytes(weighed.bytes), build: weighed.build })}
                </Typography.Text>
              )}
            </Flex>
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              {t('admin.basemap.source', { source: status.source, limit: formatBytes(status.maxBytes) })}
            </Typography.Text>
          </Flex>
        )}
      </Flex>
    </Card>
  );
}

/** A control with its name beside it. */
function Labelled({ label, children }: { label: string; children: ReactNode }) {
  return (
    <Flex gap={6} align="center">
      <Typography.Text type="secondary">{label}</Typography.Text>
      {children}
    </Flex>
  );
}

function DownloadState({
  download,
  refusal,
  onCancel,
}: {
  download: BasemapDownload;
  refusal: (code: string | null | undefined) => string;
  onCancel: () => void;
}) {
  const { t } = useTranslation();
  const colour = { queued: 'default', running: 'processing', succeeded: 'success', failed: 'error', cancelled: 'warning' }[
    download.state
  ];
  return (
    <Flex vertical gap={8} data-testid="basemap-download-state">
      <Flex gap={8} align="center" wrap>
        <Typography.Text>{t('admin.basemap.latestDownload')}</Typography.Text>
        <Tag color={colour} data-testid="basemap-download-tag">
          {t(`admin.basemap.state.${download.state}`)}
        </Tag>
        <Typography.Text type="secondary">
          {download.build ?? t('admin.basemap.latest')} · {formatRegion(download.region)} · z{download.maxZoom}
        </Typography.Text>
      </Flex>
      {isUnderway(download) && (
        <Flex gap={12} align="center">
          <Progress percent={download.percent} style={{ flex: 1 }} data-testid="basemap-progress" />
          <Button danger onClick={onCancel} data-testid="basemap-cancel">
            {t('admin.basemap.cancel')}
          </Button>
        </Flex>
      )}
      {(download.state === 'failed' || download.state === 'cancelled') && (
        <Alert
          type={download.state === 'cancelled' ? 'info' : 'error'}
          showIcon
          title={refusal(download.errorCode)}
          data-testid="basemap-error"
        />
      )}
    </Flex>
  );
}
