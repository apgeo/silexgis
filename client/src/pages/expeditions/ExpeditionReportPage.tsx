// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import {
  ArrowLeftOutlined,
  FilePdfOutlined,
  FileWordOutlined,
  PrinterOutlined,
  SaveOutlined,
} from '@ant-design/icons';
import {
  App,
  Alert,
  Button,
  Descriptions,
  Divider,
  Empty,
  Flex,
  Select,
  Spin,
  Tooltip,
  Typography,
} from 'antd';
import type { TFunction } from 'i18next';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router-dom';
import {
  downloadFile,
  expeditionReportUrl,
  reportPdfRefusal,
  type ReportFormat,
} from '../../api/download.ts';
import {
  parseAccessActions,
  useCan,
  useCavingGroups,
  useEffectiveAccess,
  useExpedition,
  useFileConfig,
  useKeepExpeditionReport,
  usePhotos,
  useReportTemplatesOfKind,
} from '../../api/hooks.ts';
import TripStateTag from '../../components/trips/TripStateTag.tsx';
import { formatTripDates, isMultiDay } from '../../components/trips/tripDates.ts';
// The one stylesheet about paper. Its rules are written for the trip's write-up and are the
// same rules this document needs — a measure for prose, the chrome hidden on print, the map
// replaced by its written form — so it is shared rather than copied under a second name.
import '../../components/trips/TripReport.css';
import TripGeometryField from '../../components/trips/TripGeometryField.tsx';
import { formatPosition, shapeLabelKey, tripGeometrySummary } from '../../components/trips/tripGeometrySummary.ts';
import ExpeditionRosterTab from './ExpeditionRosterTab.tsx';
import ExpeditionTripAccounts from './ExpeditionTripAccounts.tsx';
import ExpeditionTripsTab from './ExpeditionTripsTab.tsx';

/**
 * How many photographs the write-up carries — the server's own count for the document, so the
 * screen and the file show the same plates. The rest are one click away on the camp's own tab.
 */
const PlateCount = 24;

/** A drawn shape as this reader's language words it, or as it is stored when it has no wording. */
function shapeLabel(type: string, t: TFunction): string {
  const key = shapeLabelKey(type);
  return key ? t(key) : type;
}

/** A titled part of the document, kept whole across a page break. */
function Part({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="trip-report-section" style={{ marginTop: 24 }}>
      <Typography.Title level={4} style={{ marginBottom: 8 }}>
        {title}
      </Typography.Title>
      {children}
    </section>
  );
}

/**
 * One camp, laid out as the document a club circulates — and the way to the file.
 *
 * <p>
 * The same camp, from the same requests the camp's page makes, arranged to be read rather than
 * edited: what it was and when, who organised it, what it was about, roughly where it worked,
 * the trips gathered into it and what they add up to, and who was there. Nothing is re-derived
 * here. Every rule about who may see what has already been applied by the time each request
 * answers — the trips are the ones this reader may open, the roster is withheld whole where it
 * would be, and the caves the trips named are not placed anywhere on this page — so this page
 * asks no permission question of its own and prints what it was given.
 * </p>
 * <p>
 * The document the download produces is built on the server from the same readings, in the
 * layout chosen here. Filing a copy against the camp takes the right to change the camp and the
 * right to put a document in the archive, and that copy is built for everybody who may read the
 * camp rather than for the person filing it, which the button says before it is pressed.
 * </p>
 */
export default function ExpeditionReportPage() {
  const { t, i18n } = useTranslation();
  const { id } = useParams<{ id: string }>();
  const { data: camp, isPending } = useExpedition(id);
  const { data: cavingGroups } = useCavingGroups();
  const { data: templates } = useReportTemplatesOfKind('expedition');
  // Per-object capabilities once they arrive; the domain check only bridges the first render.
  // It decides nothing — filing a document against the camp is refused by the server for anyone
  // who may not change it, whatever this page offers.
  const { data: effective } = useEffectiveAccess('expedition', id);
  const domainFallback = useCan('expeditions', 'write');
  const held = effective ? parseAccessActions(effective.actions) : null;
  const mayFile = useCan('documents', 'create');
  const canKeep = (held ? held.has('write') : domainFallback) && mayFile;
  const [templateId, setTemplateId] = useState<string | undefined>(undefined);
  // Which format is on its way, so the button that was pressed spins and its twin waits.
  const [downloading, setDownloading] = useState<ReportFormat | null>(null);
  // Whether this installation can lay a write-up out as a PDF is a fact about the installation,
  // read from what it says about itself. Where it cannot, the choice is not offered: a button
  // that can only be refused is worse than no button, and printing to PDF is still on this page.
  const { data: fileConfig } = useFileConfig();
  const pdfOffered = fileConfig?.conversionAvailable === true;
  const keepReport = useKeepExpeditionReport();
  const { message } = App.useApp();
  // The pictures come the way the gallery gets them, through the photographs request, which
  // answers with what this caller may read of the camp and its trips and nothing else.
  const mayReadPhotos = useCan('documents', 'read');
  const photosQuery = usePhotos({ expeditionId: id, pageSize: PlateCount }, mayReadPhotos && !!id);

  // Printing has to leave the application's chrome behind, and the rules that do it are scoped to
  // this page rather than let loose over every screen that might one day be printed.
  useEffect(() => {
    document.body.classList.add('trip-report-page');
    return () => document.body.classList.remove('trip-report-page');
  }, []);

  if (isPending || !camp) {
    return (
      <Flex align="center" justify="center" style={{ height: '100%' }}>
        <Spin size="large" />
      </Flex>
    );
  }

  /**
   * Downloads the write-up in one format. A refusal that is about the PDF — the converter did
   * not answer, or could not lay this one out — is said in those words, because the remedy is
   * on this page: the Word document.
   */
  const downloadAs = (format: ReportFormat) => {
    setDownloading(format);
    downloadFile(expeditionReportUrl(camp.id, templateId, format))
      .catch(
        (error: unknown) =>
          void message.error(t(reportPdfRefusal(error) ?? 'trips.report.documentFailed')),
      )
      .finally(() => setDownloading(null));
  };

  const organizingCavingGroup = cavingGroups?.find((g) => g.id === camp.cavingGroupId);
  const spansDays = isMultiDay(camp.startDate, camp.endDate);
  const dateText = formatTripDates(camp.startDate, camp.endDate, i18n.resolvedLanguage);
  const area = tripGeometrySummary(camp.geom);
  const photos = photosQuery.data?.items ?? [];

  return (
    <div className="trip-report">
      <Flex
        className="trip-report-noprint"
        justify="space-between"
        align="center"
        gap={8}
        wrap
        style={{ marginBottom: 16 }}
      >
        <Link to={`/expeditions/${camp.id}`}>
          <Button icon={<ArrowLeftOutlined />}>{t('expeditions.report.backToCamp')}</Button>
        </Link>
        <Flex align="center" gap={8} wrap>
          {/* Offered only once a club has written a camp layout of its own: with nothing to
              choose between, a chooser is a control that can only be left alone. */}
          {(templates?.length ?? 0) > 0 && (
            <Select
              style={{ minWidth: 200 }}
              value={templateId}
              onChange={(value) => setTemplateId(value)}
              data-testid="expedition-report-template"
              aria-label={t('trips.report.template')}
              options={[
                { value: undefined, label: t('trips.report.templateDefault') },
                ...(templates ?? []).map((row) => ({ value: row.id, label: row.name })),
              ]}
            />
          )}
          <Button
            icon={<FileWordOutlined />}
            loading={downloading === 'docx'}
            disabled={downloading === 'pdf'}
            data-testid="expedition-report-download"
            onClick={() => downloadAs('docx')}
          >
            {t('trips.report.download')}
          </Button>
          {/* The same document, laid out by the installation's converter. */}
          {pdfOffered && (
            <Button
              icon={<FilePdfOutlined />}
              loading={downloading === 'pdf'}
              disabled={downloading === 'docx'}
              data-testid="expedition-report-download-pdf"
              onClick={() => downloadAs('pdf')}
            >
              {t('trips.report.downloadPdf')}
            </Button>
          )}
          {canKeep && (
            /* Said before the button rather than after it: what is filed against the camp is
               readable by everybody who may read the camp, so the server builds that copy for
               that audience — which is a narrower document than the one on this screen. */
            <Tooltip title={t('expeditions.report.keepAudience')}>
              <Button
                icon={<SaveOutlined />}
                loading={keepReport.isPending}
                data-testid="expedition-report-keep"
                onClick={() => {
                  keepReport.mutate(
                    { id: camp.id, templateId },
                    {
                      onSuccess: () => void message.success(t('expeditions.report.kept')),
                      onError: () => void message.error(t('trips.report.documentFailed')),
                    },
                  );
                }}
              >
                {t('expeditions.report.keep')}
              </Button>
            </Tooltip>
          )}
          <Button
            type="primary"
            icon={<PrinterOutlined />}
            onClick={() => window.print()}
            data-testid="expedition-report-print"
          >
            {t('trips.report.print')}
          </Button>
        </Flex>
      </Flex>

      <article data-testid="expedition-report">
        <Typography.Title level={2} style={{ marginBottom: 4 }} data-testid="expedition-report-name">
          {camp.name}
        </Typography.Title>
        <Flex align="center" gap={8} wrap style={{ marginBottom: 16 }}>
          <Typography.Text type="secondary">
            {[dateText, organizingCavingGroup?.name].filter(Boolean).join(' · ')}
          </Typography.Text>
          {/* Said on the document itself, because a draft printed and handed round is exactly how
              an unannounced plan comes to be read as the club's record of the camp. */}
          {camp.state === 'draft' && <TripStateTag state={camp.state} />}
        </Flex>

        <Descriptions column={1} size="small" bordered style={{ marginTop: 16 }}>
          <Descriptions.Item label={spansDays ? t('expeditions.dates') : t('expeditions.date')}>
            {dateText}
          </Descriptions.Item>
          {organizingCavingGroup && (
            <Descriptions.Item label={t('expeditions.cavingGroup')}>
              {organizingCavingGroup.name}
            </Descriptions.Item>
          )}
          {camp.publishedAt && (
            <Descriptions.Item label={t('trips.publishedAt')}>
              {new Date(camp.publishedAt).toLocaleString(i18n.resolvedLanguage)}
            </Descriptions.Item>
          )}
        </Descriptions>

        {camp.description && (
          <Part title={t('expeditions.report.account')}>
            <Typography.Paragraph style={{ whiteSpace: 'pre-wrap' }} data-testid="expedition-report-account">
              {camp.description}
            </Typography.Paragraph>
          </Part>
        )}

        {camp.geom && (
          <Part title={t('expeditions.workingArea')}>
            <div className="trip-report-map">
              <TripGeometryField value={camp.geom} readOnly height={280} testId="expedition-working-area" />
            </div>
            {/* What the printer gets in its place. The working area is exact for everyone who
                may read the camp, so writing its position down states nothing the map did not. */}
            {area && (
              <div className="trip-report-map-fallback" data-testid="expedition-report-area">
                <Typography.Text>
                  {t(
                    area.positions === 1 ? 'expeditions.report.areaPoint' : 'expeditions.report.areaShape',
                    {
                      shape: shapeLabel(area.type, t),
                      position: formatPosition(area.center, {
                        north: t('trips.report.north'),
                        south: t('trips.report.south'),
                        east: t('trips.report.east'),
                        west: t('trips.report.west'),
                      }),
                      count: area.positions,
                    },
                  )}
                </Typography.Text>
              </div>
            )}
            <Typography.Paragraph type="secondary" style={{ marginTop: 8, marginBottom: 0 }}>
              {t('expeditions.workingAreaWarning')}
            </Typography.Paragraph>
          </Part>
        )}

        {/* The trips this reader may open, and what they add up to — the same list and the same
            roll-up the camp's page draws, each carrying its own "as visible to you" wording. */}
        <Part title={t('expeditions.report.trips')}>
          <ExpeditionTripsTab expeditionId={camp.id} />
        </Part>

        {/* What each of those trips wrote about itself, from the same answer the list above is
            drawn from. It brings its own heading, so a camp none of whose trips wrote anything
            shows no part here rather than an empty one. */}
        <ExpeditionTripAccounts expeditionId={camp.id} />

        {/* The roster, read with the two rights that govern it and withheld whole where the
            reader may not read people; that refusal is drawn as it is on the camp's own tab. */}
        <Part title={t('expeditions.report.whoWasThere')}>
          <ExpeditionRosterTab expeditionId={camp.id} />
        </Part>

        {mayReadPhotos && (
          <Part title={t('trips.gallery')}>
            {photosQuery.isPending ? (
              <Spin />
            ) : photos.length === 0 ? (
              <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('expeditions.noPhotographs')} />
            ) : (
              <>
                {/* Two people printing the same write-up get two sets of pictures, and the
                    document says so rather than letting the difference read as a fault. */}
                <Typography.Paragraph type="secondary">
                  {t('expeditions.galleryVisibleToYou')}
                </Typography.Paragraph>
                <div className="trip-report-plates" data-testid="expedition-report-plates">
                  {photos.map((photo) => (
                    <figure key={photo.documentId} className="trip-report-plate">
                      {/* The rendering, never the stored file: a caller who may see the camp is
                          not thereby entitled to the original, which carries where it was taken. */}
                      {photo.thumbnailUrl && (
                        <img src={photo.thumbnailUrl} alt={photo.credit.caption ?? photo.title} />
                      )}
                      <figcaption>
                        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                          {[photo.credit.caption ?? photo.title, photo.credit.photographerName]
                            .filter(Boolean)
                            .join(' — ')}
                        </Typography.Text>
                      </figcaption>
                    </figure>
                  ))}
                </div>
              </>
            )}
          </Part>
        )}

        <Divider />
        <Alert
          className="trip-report-noprint"
          type="info"
          showIcon
          title={t('expeditions.report.audienceNotice')}
        />
      </article>
    </div>
  );
}
