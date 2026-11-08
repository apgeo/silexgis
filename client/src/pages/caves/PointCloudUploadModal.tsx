// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { InboxOutlined } from '@ant-design/icons';
import { Alert, App, Form, Input, Modal, Typography, Upload } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import {
  isPointCloudFileName,
  POINT_CLOUD_EXTENSIONS,
  useStorageRoom,
  useSurveyUploadCapabilities,
  useUploadPointCloud,
  type PointCloudUploadPhase,
} from '../../api/hooks.ts';
import { formatSize } from '../../components/attachments/fileFormat.ts';
import { surveyUploadRequestErrorText } from '../../components/surveyUploads/surveyUploadProblems.ts';
import StorageRoomSummary from '../../components/storage/StorageRoomSummary.tsx';

/** The cap the server publishes for a cloud when the capabilities have not answered yet: 4 GiB, its own default. */
const DEFAULT_MAX_CLOUD_BYTES = 4 * 1024 * 1024 * 1024;

/** The file's name without its extension: what the survey is called unless the uploader says otherwise. */
function titleFromFileName(name: string): string {
  const dot = name.lastIndexOf('.');
  const stem = dot > 0 ? name.slice(0, dot) : name;
  return stem.trim();
}

/**
 * Uploading a point cloud — one `.laz` or `.las` file — as a survey of the cave.
 *
 * A cloud goes through the survey-upload protocol rather than the model upload: the file is
 * archived as the survey's original, its header is read into the scan's facts, and tiling runs as a
 * job whose progress has a page of its own. So this dialog declares the file, sends its bytes and
 * hands the person to that page, where the estimate table waits when the cloud is large.
 *
 * Nothing is asked about where the cloud sits. A file whose header names a coordinate system is
 * placed from it by the server; a file without one is shown unplaced in its own metres and placed
 * later from the scan's card.
 */
export default function PointCloudUploadModal({
  caveId,
  open,
  onClose,
}: {
  caveId: string;
  open: boolean;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const navigate = useNavigate();
  const [form] = Form.useForm<{ title: string }>();
  const [file, setFile] = useState<File>();
  const [phase, setPhase] = useState<PointCloudUploadPhase>();
  const { data: capabilities } = useSurveyUploadCapabilities();
  const upload = useUploadPointCloud();

  const maxBytes = capabilities?.pointCloud.maxFileBytes ?? DEFAULT_MAX_CLOUD_BYTES;
  const accepted = capabilities?.pointCloud.acceptedExtensions ?? [...POINT_CLOUD_EXTENSIONS];
  const tooLarge = file !== undefined && file.size > maxBytes;
  const wrongKind = file !== undefined && !isPointCloudFileName(file.name);
  // Asked once a file is chosen, so the totals and what this one adds are on screen before the
  // upload starts. The same reading the summary below shows; a cloud that will not fit is not
  // offered for sending, and the server refuses it anyway with the same figures.
  const roomFor = file !== undefined && !tooLarge && !wrongKind ? file.size : null;
  const { data: room } = useStorageRoom({ addingBytes: roomFor, scan: true });
  const noRoom = roomFor !== null && room?.fits === false;

  const close = () => {
    form.resetFields();
    setFile(undefined);
    setPhase(undefined);
    onClose();
  };

  const submit = async () => {
    if (!file || tooLarge || wrongKind || noRoom) {
      return;
    }
    let title: string;
    try {
      title = (await form.validateFields()).title.trim();
    } catch {
      return;
    }
    try {
      const queued = await upload.mutateAsync({ caveId, file, surveyTitle: title, onPhase: setPhase });
      message.success(t('surveyUploads.pointCloud.uploaded'));
      close();
      navigate(queued.statusPageUrl);
    } catch (error) {
      setPhase(undefined);
      message.error(surveyUploadRequestErrorText(error, t));
    }
  };

  return (
    <Modal
      title={t('surveyUploads.pointCloud.uploadTitle')}
      open={open}
      onCancel={close}
      onOk={() => void submit()}
      okText={t('surveyUploads.pointCloud.upload')}
      okButtonProps={{ disabled: !file || tooLarge || wrongKind || noRoom, loading: upload.isPending }}
      cancelButtonProps={{ disabled: upload.isPending }}
      confirmLoading={upload.isPending}
      closable={!upload.isPending}
      mask={{ closable: !upload.isPending }}
      width="min(640px, 95vw)"
      destroyOnHidden
    >
      <Upload.Dragger
        accept={accepted.join(',')}
        multiple={false}
        maxCount={1}
        showUploadList={false}
        disabled={upload.isPending}
        beforeUpload={(chosen) => {
          setFile(chosen);
          if (!form.isFieldTouched('title')) {
            form.setFieldsValue({ title: titleFromFileName(chosen.name) });
          }
          return false;
        }}
        data-testid="point-cloud-file"
      >
        <p className="ant-upload-drag-icon">
          <InboxOutlined />
        </p>
        <p className="ant-upload-text">{file ? file.name : t('surveyUploads.pointCloud.chooseFile')}</p>
        <p className="ant-upload-hint">
          {file
            ? formatSize(file.size)
            : t('surveyUploads.pointCloud.acceptHint', { extensions: accepted.join(', '), max: formatSize(maxBytes) })}
        </p>
      </Upload.Dragger>

      {tooLarge && (
        <Alert
          type="error"
          showIcon
          style={{ marginTop: 12 }}
          data-testid="point-cloud-too-large"
          title={t('surveyUploads.pointCloud.tooLarge', { max: formatSize(maxBytes) })}
        />
      )}
      {wrongKind && (
        <Alert
          type="error"
          showIcon
          style={{ marginTop: 12 }}
          data-testid="point-cloud-wrong-kind"
          title={t('surveyUploads.pointCloud.wrongKind', { extensions: accepted.join(', ') })}
        />
      )}

      {roomFor !== null && <StorageRoomSummary addingBytes={roomFor} scan />}

      <Form form={form} layout="vertical" style={{ marginTop: 16 }} disabled={upload.isPending}>
        <Form.Item
          name="title"
          label={t('surveyUploads.pointCloud.surveyTitle')}
          extra={t('surveyUploads.pointCloud.surveyTitleHint')}
          rules={[{ required: true, whitespace: true, message: t('surveyUploads.pointCloud.surveyTitleRequired') }]}
        >
          <Input maxLength={200} data-testid="point-cloud-title" />
        </Form.Item>
      </Form>

      <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
        {t('surveyUploads.pointCloud.whatHappens')}
      </Typography.Paragraph>

      {phase && (
        <Typography.Text type="secondary" data-testid="point-cloud-phase" style={{ display: 'block', marginTop: 8 }}>
          {t(`surveyUploads.pointCloud.phase.${phase}`)}
        </Typography.Text>
      )}
    </Modal>
  );
}
