// SPDX-License-Identifier: AGPL-3.0-or-later
import { DownloadOutlined, EyeOutlined } from '@ant-design/icons';
import { Empty, Flex, Tag, Typography } from 'antd';
import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import Button from '../Button.tsx';
import { formatSize } from '../attachments/fileFormat.ts';
import type { SurveyUploadArtefactView } from '../../api/hooks.ts';
import { surveyUploadRunErrorText } from './surveyUploadProblems.ts';

/**
 * What a run produced, each with the file to download and — where the application has a page for
 * it — the way to open it.
 *
 * Takes the narrow shape both answers satisfy, so the status page and the public page draw the
 * same list. An output that was asked for and not produced has no file; it is listed with what the
 * server says went wrong with it instead of a download button. The delivery addresses are short-lived
 * signed links; the page asks the server again rather than keeping them, and a link past its
 * lifetime is refused by the server, not guessed at here.
 */
export default function SurveyUploadArtefacts({ artefacts }: { artefacts: readonly SurveyUploadArtefactView[] }) {
  const { t } = useTranslation();

  if (artefacts.length === 0) {
    return (
      <Empty
        image={Empty.PRESENTED_IMAGE_SIMPLE}
        description={t('surveyUploads.artefacts.empty')}
        data-testid="survey-upload-no-artefacts"
      />
    );
  }

  return (
    <Flex vertical gap={8} data-testid="survey-upload-artefacts">
      {artefacts.map((artefact) => (
        <Flex key={`${artefact.kind}:${artefact.name}`} align="center" gap={8} wrap>
          <Tag>{t(`surveyUploads.outputs.${artefact.kind}`, { defaultValue: artefact.kind })}</Tag>
          <Typography.Text>{artefact.name}</Typography.Text>
          {artefact.sizeBytes !== null && (
            <Typography.Text type="secondary">{formatSize(artefact.sizeBytes)}</Typography.Text>
          )}
          {artefact.contentUrl && (
            <Button size="small" icon={<DownloadOutlined />} href={artefact.contentUrl} target="_blank" rel="noreferrer">
              {t('surveyUploads.artefacts.download')}
            </Button>
          )}
          {artefact.viewUrl && <ViewLink url={artefact.viewUrl} />}
          {artefact.error && (
            <Typography.Text type="warning" data-testid="survey-upload-artefact-error">
              {surveyUploadRunErrorText(artefact.error.code, t) ?? artefact.error.message ?? artefact.error.code}
            </Typography.Text>
          )}
        </Flex>
      ))}
    </Flex>
  );
}

/** An in-application address is followed by the router; anything else is an ordinary link. */
function ViewLink({ url }: { url: string }) {
  const { t } = useTranslation();
  const inside = url.startsWith('/') && !url.startsWith('//');
  return inside ? (
    <Link to={url}>
      <Button size="small" icon={<EyeOutlined />}>
        {t('surveyUploads.artefacts.view')}
      </Button>
    </Link>
  ) : (
    <Button size="small" icon={<EyeOutlined />} href={url} target="_blank" rel="noreferrer">
      {t('surveyUploads.artefacts.view')}
    </Button>
  );
}
