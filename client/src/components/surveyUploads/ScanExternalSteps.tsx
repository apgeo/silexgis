// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { App, Alert, Flex, Tag, Typography, Upload } from 'antd';
import { CloudDownloadOutlined, DownloadOutlined, UploadOutlined } from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import Button from '../Button.tsx';
import { DownloadError, downloadFile } from '../../api/download.ts';
import {
  surveyUploadExternalBundleUrl,
  useSendExternalOutput,
  useUploadRefinedSession,
  type PointCloudUploadPhase,
  type SurveyUpload,
  type SurveyUploadExternalWait,
} from '../../api/hooks.ts';
import { externalOutputName } from './externalOutputName.ts';
import { SURVEY_UPLOAD_REQUEST_ERROR_KEYS, surveyUploadRequestErrorText } from './surveyUploadProblems.ts';

/**
 * A scan's run stopped before a step done on another computer: what the step is, the download of
 * what it reads (the inputs, its parameters in the tool's own format and a README), and one picker
 * per product to send back. When the last product is in, the server queues the run again and it
 * carries on from the next step; the status page then follows it as it would any run.
 *
 * Drawn only for somebody who may write the cave — the server says which step and what to send to
 * nobody else — because both the download (the recording, with whatever position its satellite
 * log holds) and the upload back need Write.
 */
export function ExternalStepPanel({ id, external }: { id: string; external: SurveyUploadExternalWait }) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const send = useSendExternalOutput();
  const [downloading, setDownloading] = useState(false);
  const [sending, setSending] = useState<string>();

  const download = async () => {
    setDownloading(true);
    try {
      await downloadFile(surveyUploadExternalBundleUrl(id));
    } catch (error) {
      // The download is fetched outside the typed client, so its refusal comes with the code alone.
      const key = error instanceof DownloadError && error.code ? SURVEY_UPLOAD_REQUEST_ERROR_KEYS[error.code] : undefined;
      message.error(key ? t(key) : surveyUploadRequestErrorText(error, t));
    } finally {
      setDownloading(false);
    }
  };

  const upload = async (names: readonly string[], file: File) => {
    const name = externalOutputName(names, file.name);
    setSending(names[0]);
    try {
      const answer = await send.mutateAsync({ id, name, file });
      message.success(
        t(answer.status === 'queued' ? 'surveyUploads.external.resumed' : 'surveyUploads.external.received', { name }),
      );
    } catch (error) {
      message.error(surveyUploadRequestErrorText(error, t));
    } finally {
      setSending(undefined);
    }
  };

  return (
    <Alert
      type="info"
      showIcon
      data-testid="scan-external-wait"
      title={t('surveyUploads.external.title', { step: external.stepName })}
      description={
        <Flex vertical gap={8}>
          <Typography.Text>{t('surveyUploads.external.explain', { tool: external.tool })}</Typography.Text>
          <Flex>
            <Button
              icon={<DownloadOutlined />}
              loading={downloading}
              onClick={() => void download()}
              data-testid="scan-external-bundle"
            >
              {t('surveyUploads.external.download')}
            </Button>
          </Flex>
          <Typography.Text type="secondary">{t('surveyUploads.external.sendBack')}</Typography.Text>
          {external.outputs.map((output) => (
            <Flex key={output.names.join('|')} gap={8} align="center" wrap>
              <Typography.Text code>{output.names.join(' / ')}</Typography.Text>
              {output.received ? (
                <Tag color="green" data-testid={`scan-external-received-${output.names[0]}`}>
                  {t('surveyUploads.external.receivedTag')}
                </Tag>
              ) : (
                <Upload
                  showUploadList={false}
                  multiple={false}
                  maxCount={1}
                  beforeUpload={(file) => {
                    void upload(output.names, file);
                    return false;
                  }}
                >
                  <Button
                    size="small"
                    icon={<UploadOutlined />}
                    loading={sending === output.names[0]}
                    disabled={sending !== undefined}
                    data-testid={`scan-external-send-${output.names[0]}`}
                  >
                    {t('surveyUploads.external.send')}
                  </Button>
                </Upload>
              )}
            </Flex>
          ))}
        </Flex>
      }
    />
  );
}

/**
 * The desktop refinement round trip of a finished scan: the scan's HDMapping session downloaded,
 * refined in HDMapping's desktop program, and sent back as the scan's next version, whose run
 * starts at the export against the recording already on the server.
 *
 * The session holds the scanner's path, so the server gives its address only with the exact
 * location; without it there is no session to download and nothing is drawn.
 */
export function RefineOnDesktopPanel({
  upload,
  sessionUrl,
}: {
  upload: Pick<SurveyUpload, 'cave' | 'caveSurvey'>;
  sessionUrl: string;
}) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const navigate = useNavigate();
  const send = useUploadRefinedSession();
  const [phase, setPhase] = useState<PointCloudUploadPhase>();

  const upload_ = async (file: File) => {
    try {
      const queued = await send.mutateAsync({
        caveSurveyId: upload.caveSurvey.id,
        surveyTitle: upload.caveSurvey.title,
        file,
        onPhase: setPhase,
      });
      message.success(t('surveyUploads.external.refinedSent'));
      navigate(queued.statusPageUrl);
    } catch (error) {
      message.error(surveyUploadRequestErrorText(error, t));
    } finally {
      setPhase(undefined);
    }
  };

  return (
    <Flex vertical gap={8} data-testid="scan-refine">
      <Typography.Text strong>{t('surveyUploads.external.refineTitle')}</Typography.Text>
      <Typography.Text type="secondary">{t('surveyUploads.external.refineExplain')}</Typography.Text>
      <Flex gap={8} wrap>
        <Button icon={<CloudDownloadOutlined />} href={sessionUrl} data-testid="scan-refine-session">
          {t('surveyUploads.external.refineDownload')}
        </Button>
        <Upload
          accept=".zip"
          showUploadList={false}
          multiple={false}
          maxCount={1}
          beforeUpload={(file) => {
            void upload_(file);
            return false;
          }}
        >
          <Button
            icon={<UploadOutlined />}
            loading={send.isPending}
            data-testid="scan-refine-send"
          >
            {phase ? t(`surveyUploads.pointCloud.phase.${phase}`) : t('surveyUploads.external.refineSend')}
          </Button>
        </Upload>
      </Flex>
    </Flex>
  );
}
