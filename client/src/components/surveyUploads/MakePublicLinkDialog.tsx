// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, App, Checkbox, Flex, Modal, Radio, Typography } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';
import Button from '../Button.tsx';
import {
  SURVEY_UPLOAD_LINK_AUDIENCES,
  useMintSurveyUploadPublicLink,
  type SurveyUploadLinkAudience,
} from '../../api/hooks.ts';
import { surveyUploadRequestErrorText } from './surveyUploadProblems.ts';

/** The server's refusal of a link to a protected cave's results asked for without the acknowledgement. */
const PROTECTED_REFUSAL = 'survey_upload.public_link_refused_protected';

/**
 * Making an upload's public link, or a new one in place of the old: who it opens for, and — for a
 * cave whose position is protected — the person's explicit acknowledgement.
 *
 * A protected cave's results (the 3D model, the plans) show where its passages are, so its link is
 * made only when somebody says, here and for this upload, that they know. The box starts empty
 * every time the dialog opens and nothing stands in for it. When the page did not know the cave
 * was protected (its position was protected after the page loaded, or the upload's reason for
 * having no link was another one), the server's refusal turns the dialog into the one that asks.
 */
export default function MakePublicLinkDialog({
  uploadId,
  open,
  onClose,
  initialAudience,
  caveProtected,
  replacing,
}: {
  uploadId: string;
  open: boolean;
  onClose: () => void;
  initialAudience: SurveyUploadLinkAudience;
  /** Whether the page already knows the cave's position is protected. */
  caveProtected: boolean;
  /** Whether the upload has a link the new one replaces. */
  replacing: boolean;
}) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const mint = useMintSurveyUploadPublicLink();
  const [audience, setAudience] = useState<SurveyUploadLinkAudience>(initialAudience);
  const [acknowledged, setAcknowledged] = useState(false);
  const [refusedProtected, setRefusedProtected] = useState(false);
  const asksAcknowledgement = caveProtected || refusedProtected;

  const reset = () => {
    setAudience(initialAudience);
    setAcknowledged(false);
    setRefusedProtected(false);
  };

  const make = async () => {
    try {
      await mint.mutateAsync({ id: uploadId, audience, allowProtected: asksAcknowledgement ? acknowledged : false });
      message.success(t('surveyUploads.link.made'));
      onClose();
    } catch (error) {
      if (error instanceof ApiError && error.code === PROTECTED_REFUSAL && !asksAcknowledgement) {
        setRefusedProtected(true);
        return;
      }
      message.error(surveyUploadRequestErrorText(error, t));
    }
  };

  return (
    <Modal
      open={open}
      title={t('surveyUploads.link.dialogTitle')}
      onCancel={onClose}
      footer={
        <Flex justify="flex-end" gap={8}>
          <Button onClick={onClose}>{t('common.cancel')}</Button>
          <Button
            type="primary"
            loading={mint.isPending}
            disabled={asksAcknowledgement && !acknowledged}
            onClick={() => void make()}
            data-testid="survey-upload-link-dialog-ok"
          >
            {t('surveyUploads.link.make')}
          </Button>
        </Flex>
      }
      afterOpenChange={(visible) => {
        if (visible) {
          reset();
        }
      }}
      destroyOnHidden
    >
      <Flex vertical gap={12} data-testid="survey-upload-link-dialog">
        {replacing && <Alert type="info" showIcon title={t('surveyUploads.link.remintConfirm')} />}
        <Typography.Text>{t('surveyUploads.link.audienceLabel')}</Typography.Text>
        <Radio.Group
          value={audience}
          onChange={(e) => setAudience(e.target.value as SurveyUploadLinkAudience)}
          data-testid="survey-upload-link-audience"
        >
          <Flex vertical gap={4}>
            {SURVEY_UPLOAD_LINK_AUDIENCES.map((a) => (
              <Radio key={a} value={a} data-testid={`survey-upload-link-audience-${a}`}>
                {t(`surveyUploads.link.audience.${a}`)}
                <Typography.Text type="secondary" style={{ display: 'block', fontSize: 12 }}>
                  {t(`surveyUploads.link.audienceHint.${a}`)}
                </Typography.Text>
              </Radio>
            ))}
          </Flex>
        </Radio.Group>
        {asksAcknowledgement && (
          <>
            <Alert
              type="warning"
              showIcon
              data-testid="survey-upload-link-protected-warning"
              title={t('surveyUploads.link.protectedWarning')}
            />
            <Checkbox
              checked={acknowledged}
              onChange={(e) => setAcknowledged(e.target.checked)}
              data-testid="survey-upload-link-acknowledge"
            >
              {t('surveyUploads.link.protectedAcknowledge')}
            </Checkbox>
          </>
        )}
      </Flex>
    </Modal>
  );
}
