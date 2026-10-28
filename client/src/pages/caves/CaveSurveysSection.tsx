// SPDX-License-Identifier: AGPL-3.0-or-later
import { DownloadOutlined } from '@ant-design/icons';
import { Card, Flex, Skeleton, Table, Tag, Typography } from 'antd';
import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import {
  useCaveSurveys,
  useSurveyUpload,
  type CaveSurveyGroup,
  type SurveyUploadListItem,
} from '../../api/hooks.ts';
import Button from '../../components/Button.tsx';
import { formatSize } from '../../components/attachments/fileFormat.ts';
import SurveyUploadArtefacts from '../../components/surveyUploads/SurveyUploadArtefacts.tsx';
import OlderVersionsWarning from '../../components/surveyUploads/OlderVersionsWarning.tsx';

/**
 * The surveys a cave holds as they arrived from a device, each with every version that was handed
 * over, newest first.
 *
 * A survey is what its uploads are versions of; the cave may hold any number and none is the
 * default. Each version opens onto what it brought in — the archive and Therion files it was sent,
 * and what the run produced from them — and onto its own status page. The compiled models and
 * archived sources further down this page stay as they are: those lists are the whole cave's, this
 * one says which upload each piece of work came from, so nothing is repeated here.
 *
 * Nothing is drawn for a cave with no uploads. That is every cave that never had a device send it
 * a survey, and an empty card on all of them would be noise. The listing the section reads is
 * withheld from callers who may not see the cave's position exactly, so a protected cave looks the
 * same as one with none — by design.
 *
 * A survey whose older versions hold more of their runs' results than the installation warns at
 * says so above the list, with the size and the setting that keeps fewer versions' results.
 */
export default function CaveSurveysSection({ caveId }: { caveId: string }) {
  const { t, i18n } = useTranslation();
  const { data: surveys, isPending } = useCaveSurveys(caveId);

  if (isPending) {
    return null;
  }
  if (!surveys || surveys.length === 0) {
    return null;
  }

  const when = (value: string) => new Date(value).toLocaleString(i18n.language);

  return (
    <Card title={t('surveyUploads.surveys.title')} style={{ marginTop: 16 }} data-testid="cave-surveys">
      <Typography.Paragraph type="secondary" style={{ fontSize: 12 }}>
        {t('surveyUploads.surveys.explanation')}
      </Typography.Paragraph>
      {surveys
        .filter((survey) => survey.versionCount > 1)
        .map((survey) => (
          <SurveySizeWarning key={survey.id} survey={survey} />
        ))}
      <Table<CaveSurveyGroup>
        scroll={{ x: 'max-content' }}
        rowKey="id"
        size="small"
        dataSource={surveys}
        pagination={false}
        expandable={{
          expandedRowRender: (survey) => <VersionList survey={survey} />,
        }}
        columns={[
          { title: t('surveyUploads.surveys.survey'), dataIndex: 'title' },
          {
            title: t('surveyUploads.surveys.source'),
            dataIndex: 'source',
            render: (source: CaveSurveyGroup['source']) => <Tag>{t(`surveyUploads.origins.${source}`)}</Tag>,
          },
          {
            title: t('surveyUploads.surveys.versions'),
            dataIndex: 'versionCount',
            align: 'right',
          },
          {
            title: t('surveyUploads.surveys.newest'),
            dataIndex: 'newestVersionAt',
            render: (value: string) => when(value),
          },
        ]}
      />
    </Card>
  );
}

/**
 * The older-versions warning of one survey. The size is on the single upload's answer (the same on
 * every version of a survey), so it is asked for the newest version, and only for surveys that
 * have older versions at all.
 */
function SurveySizeWarning({ survey }: { survey: CaveSurveyGroup }) {
  const { data: upload } = useSurveyUpload(survey.versions[0]?.id);
  if (!upload?.olderVersions.warn) {
    return null;
  }
  return (
    <div style={{ marginBottom: 12 }} data-testid={`cave-survey-size-warning-${survey.id}`}>
      <OlderVersionsWarning olderVersions={upload.olderVersions} surveyTitle={survey.title} />
    </div>
  );
}

function VersionList({ survey }: { survey: CaveSurveyGroup }) {
  const { t, i18n } = useTranslation();
  return (
    <Table<SurveyUploadListItem>
      scroll={{ x: 'max-content' }}
      rowKey="id"
      size="small"
      showHeader={false}
      pagination={false}
      dataSource={survey.versions}
      expandable={{ expandedRowRender: (version) => <VersionContents version={version} /> }}
      data-testid={`cave-survey-versions-${survey.id}`}
      columns={[
        { render: (_, version) => new Date(version.version).toLocaleString(i18n.language) },
        {
          render: (_, version) => <Tag>{t(`surveyUploads.status.${version.status}`)}</Tag>,
        },
        { render: (_, version) => version.uploadedBy.displayName },
        {
          render: (_, version) => <Link to={version.statusPageUrl}>{t('surveyUploads.surveys.openStatus')}</Link>,
        },
      ]}
    />
  );
}

/**
 * What one version holds: the files it was sent, from the listing, and what the run produced,
 * which only the single upload's answer carries — so it is asked for when the version is opened,
 * not for every version of every survey when the page loads.
 */
function VersionContents({ version }: { version: SurveyUploadListItem }) {
  const { t } = useTranslation();
  const { data: upload } = useSurveyUpload(version.id);
  const sent = [...(version.archive ? [version.archive] : []), ...version.therionSources];

  return (
    <Flex vertical gap={12} data-testid={`cave-survey-version-${version.id}`}>
      <div>
        <Typography.Text strong>{t('surveyUploads.surveys.sent')}</Typography.Text>
        <Flex vertical gap={4} style={{ marginTop: 4 }}>
          {sent.length === 0 ? (
            <Typography.Text type="secondary">{t('surveyUploads.page.noFiles')}</Typography.Text>
          ) : (
            sent.map((file) => (
              <Flex key={file.name} align="center" gap={8} wrap>
                <Typography.Text>{file.name}</Typography.Text>
                <Typography.Text type="secondary">{formatSize(file.sizeBytes)}</Typography.Text>
                <Button size="small" icon={<DownloadOutlined />} href={file.contentUrl} target="_blank" rel="noreferrer">
                  {t('surveyUploads.artefacts.download')}
                </Button>
              </Flex>
            ))
          )}
        </Flex>
      </div>
      <div>
        <Typography.Text strong>{t('surveyUploads.artefacts.title')}</Typography.Text>
        <div style={{ marginTop: 4 }}>
          {upload ? <SurveyUploadArtefacts artefacts={upload.artefacts} /> : <Skeleton active paragraph={{ rows: 1 }} title={false} />}
        </div>
      </div>
    </Flex>
  );
}
