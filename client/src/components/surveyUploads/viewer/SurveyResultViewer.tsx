// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState, type ReactNode } from 'react';
import { CopyOutlined, DownloadOutlined } from '@ant-design/icons';
import { Alert, App, Card, Empty, Flex, Skeleton, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { downloadFile } from '../../../api/download.ts';
import {
  SURVEY_GEODATA_FORMATS,
  useMapLayers,
  type SurveyGeodataFormat,
  type SurveyLineWork,
  type SurveyLogFigures,
  type SurveyUploadAnalyses,
  type ViewerModel,
} from '../../../api/hooks.ts';
import { DOWNLOAD_BUTTON } from '../../../theme.ts';
import Button from '../../Button.tsx';
import SurveyModelTabs from '../../caveview/SurveyModelTabs.tsx';
import SurveyLineMap from './SurveyLineMap.tsx';
import SurveyLogFiguresView from './SurveyLogFiguresView.tsx';
import SurveyUploadAnalysesView from './SurveyUploadAnalysesView.tsx';
import { useFirstAddress } from './useFirstAddress.ts';

/** What the page can say about the survey's place in the world. */
export type SurveyGeoState =
  | { kind: 'loading' }
  /**
   * The run placed the survey, and the page has its line. `catalogue` says whether the
   * page may ask for the installation's map catalogue for its background — a signed-in route, which
   * a public link's page must not ask; without it the map draws its fallback background.
   */
  | { kind: 'shown'; lineWork: SurveyLineWork; downloadUrl: (format: SurveyGeodataFormat) => string; catalogue: boolean }
  /** The run placed no line in the world (no fixed point, no coordinate system). */
  | { kind: 'unplaced' }
  | { kind: 'failed' };

export interface SurveyResultViewerProps {
  title: string;
  caveName: string;
  /** The upload's version of its survey, an ISO moment. */
  version: string;
  /** The absolute address "Copy link" copies. */
  shareUrl: string;
  /** Whether that address is a public link anybody may open, or the application's own. */
  shareKind: 'public' | 'inApp';
  /** The way into the application for this cave, when there is one for this reader. */
  openInApp: ReactNode;
  /** The model the viewers draw, or null when the run produced none. */
  model: ViewerModel | null;
  figures: { data: SurveyLogFigures | null | undefined; isPending: boolean; isError: boolean };
  /** The upload's own survey measured; asked for with the figures, so pending and failing with them. */
  analyses: SurveyUploadAnalyses | null | undefined;
  geo: SurveyGeoState;
  /** Above the title: a breadcrumb or a way back. */
  navigation?: ReactNode;
}

/**
 * One survey's results in one place: the header with its link and the way into the application,
 * the model in both viewers, where it lies, what the compiler measured, and the upload's own survey
 * measured — its shape, network, orientation and depth profile.
 *
 * Shared by the signed-in page and a public link's page, which differ only in what they hand it —
 * this component decides nothing about who may see what; the server does, route by route.
 *
 * The model's address is a short-lived signed link that each read of the upload renews. The first
 * address seen for a model is kept, so a page that reads the upload again does not tear down and
 * reload a viewer the reader is looking at.
 */
export default function SurveyResultViewer({
  title,
  caveName,
  version,
  shareUrl,
  shareKind,
  openInApp,
  model,
  figures,
  analyses,
  geo,
  navigation,
}: SurveyResultViewerProps) {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const stableModel = useFirstAddress(model);

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(shareUrl);
      message.success(t('surveyViewer.header.copied'));
    } catch {
      message.error(t('surveyViewer.header.copyFailed'));
    }
  };

  return (
    <main
      data-testid="survey-viewer"
      style={{ padding: '16px clamp(8px, 3vw, 24px)', maxWidth: 1200, margin: '0 auto', boxSizing: 'border-box', width: '100%' }}
    >
      <Flex vertical gap={16}>
        {navigation}
        <Flex justify="space-between" align="flex-start" wrap gap={12}>
          <Flex vertical style={{ minWidth: 0 }}>
            <Typography.Title level={3} style={{ margin: 0, overflowWrap: 'anywhere' }} data-testid="survey-viewer-title">
              {title}
            </Typography.Title>
            <Typography.Text type="secondary">
              {t('surveyViewer.header.caveAndVersion', {
                cave: caveName,
                when: new Date(version).toLocaleString(i18n.language),
              })}
            </Typography.Text>
          </Flex>
          <Flex gap={8} wrap align="center">
            <Button icon={<CopyOutlined />} onClick={() => void copy()} data-testid="survey-viewer-copy-link">
              {t('surveyViewer.header.copyLink')}
            </Button>
            {openInApp}
          </Flex>
        </Flex>
        <Typography.Text type="secondary" data-testid="survey-viewer-share-kind" data-share-kind={shareKind}>
          {shareKind === 'public' ? t('surveyViewer.header.sharesPublic') : t('surveyViewer.header.sharesInApp')}
        </Typography.Text>

        <Card size="small" title={t('surveyViewer.model.title')} styles={{ body: { padding: 8 } }}>
          {stableModel ? (
            <SurveyModelTabs model={stableModel} height="min(70vh, 640px)" panel={{ toolbar: true }} />
          ) : (
            <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('surveyViewer.model.none')} data-testid="survey-viewer-no-model" />
          )}
        </Card>

        <GeoCard geo={geo} />

        <Card size="small" title={t('surveyViewer.figures.title')}>
          {figures.isPending ? (
            <Skeleton active paragraph={{ rows: 3 }} />
          ) : figures.isError ? (
            <Alert type="error" showIcon title={t('surveyViewer.figures.failed')} />
          ) : (
            <SurveyLogFiguresView figures={figures.data} />
          )}
        </Card>

        {!figures.isPending && !figures.isError && <SurveyUploadAnalysesView analyses={analyses} />}
      </Flex>
    </main>
  );
}

/** Where the survey lies: the files to take away and the map, or why there are none. */
function GeoCard({ geo }: { geo: SurveyGeoState }) {
  const { t } = useTranslation();
  return (
    <Card size="small" title={t('surveyViewer.map.title')} data-testid="survey-viewer-geo">
      {geo.kind === 'loading' ? (
        <Skeleton active paragraph={{ rows: 2 }} />
      ) : geo.kind === 'shown' ? (
        <Flex vertical gap={12}>
          <GeodataDownloads downloadUrl={geo.downloadUrl} />
          {geo.catalogue ? <CatalogueMap lineWork={geo.lineWork} /> : <SurveyLineMap lineWork={geo.lineWork} layers={undefined} />}
        </Flex>
      ) : (
        <Typography.Text type="secondary" data-testid={`survey-viewer-geo-${geo.kind}`}>
          {t(`surveyViewer.map.${geo.kind}`)}
        </Typography.Text>
      )}
    </Card>
  );
}

/**
 * The map of a placed survey over the installation's own default background. The catalogue is
 * asked for only here: it is a signed-in route, and a public link's page must not ask.
 */
function CatalogueMap({ lineWork }: { lineWork: SurveyLineWork }) {
  const { data: layers, isPending } = useMapLayers();
  return isPending ? (
    <Skeleton.Node active style={{ width: '100%', height: 360 }} />
  ) : (
    <SurveyLineMap lineWork={lineWork} layers={layers} />
  );
}

function GeodataDownloads({ downloadUrl }: { downloadUrl: (format: SurveyGeodataFormat) => string }) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [saving, setSaving] = useState<SurveyGeodataFormat | null>(null);

  const save = async (format: SurveyGeodataFormat) => {
    setSaving(format);
    try {
      await downloadFile(downloadUrl(format));
    } catch {
      message.error(t('surveyViewer.map.downloadFailed'));
    } finally {
      setSaving(null);
    }
  };

  return (
    <Flex gap={8} wrap align="center">
      <Typography.Text>{t('surveyViewer.map.downloads')}</Typography.Text>
      {SURVEY_GEODATA_FORMATS.map((format) => (
        <Button
          key={format}
          size="small"
          {...DOWNLOAD_BUTTON}
          icon={<DownloadOutlined />}
          loading={saving === format}
          onClick={() => void save(format)}
          data-testid={`survey-viewer-geodata-${format}`}
        >
          {t(`surveyViewer.map.format.${format}`)}
        </Button>
      ))}
    </Flex>
  );
}
