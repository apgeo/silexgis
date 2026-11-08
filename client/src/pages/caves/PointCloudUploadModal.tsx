// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { InboxOutlined } from '@ant-design/icons';
import { Alert, App, Form, Input, Modal, Segmented, Typography, Upload } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import {
  DEFAULT_RECORDING_CAPABILITIES,
  isPointCloudFileName,
  POINT_CLOUD_EXTENSIONS,
  recordingSelection,
  titleFromRecordingFolder,
  useStorageRoom,
  useSurveyUploadCapabilities,
  useUploadPointCloud,
  useUploadRecording,
  type PointCloudUploadPhase,
} from '../../api/hooks.ts';
import { formatSize } from '../../components/attachments/fileFormat.ts';
import { surveyUploadRequestErrorText } from '../../components/surveyUploads/surveyUploadProblems.ts';
import StorageRoomSummary from '../../components/storage/StorageRoomSummary.tsx';
import RecordingFolderSection, { type RecordingFile } from './RecordingFolderSection.tsx';

/** What is being sent: one cloud file, or the folder a scanner recorded. */
type UploadKind = 'file' | 'recording';

/** The cap the server publishes for a cloud when the capabilities have not answered yet: 4 GiB, its own default. */
const DEFAULT_MAX_CLOUD_BYTES = 4 * 1024 * 1024 * 1024;

/** The file's name without its extension: what the survey is called unless the uploader says otherwise. */
function titleFromFileName(name: string): string {
  const dot = name.lastIndexOf('.');
  const stem = dot > 0 ? name.slice(0, dot) : name;
  return stem.trim();
}

/**
 * Uploading a point cloud — one `.laz` or `.las` file, or the raw recording folder of a MANDEYE
 * scanner — as a survey of the cave.
 *
 * A cloud goes through the survey-upload protocol rather than the model upload: the file is
 * archived as the survey's original, its header is read into the scan's facts, and tiling runs as a
 * job whose progress has a page of its own. So this dialog declares the file, sends its bytes and
 * hands the person to that page, where the estimate table waits when the cloud is large.
 *
 * A recording is the scanner's folder, dropped or picked whole: its files are picked out of it
 * (camera folders are not sent), declared together, sent one by one and processed by HDMapping
 * into a cloud with the odometry profile chosen here, which the server remembers for next time.
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
  const [kind, setKind] = useState<UploadKind>('file');
  const [file, setFile] = useState<File>();
  const [picked, setPicked] = useState<RecordingFile[]>();
  const [profileId, setProfileId] = useState<string | null>();
  const [shownProfile, setShownProfile] = useState<string | null>(null);
  const [phase, setPhase] = useState<PointCloudUploadPhase>();
  const [sent, setSent] = useState<{ done: number; total: number }>();
  const { data: capabilities } = useSurveyUploadCapabilities();
  const uploadFile = useUploadPointCloud();
  const uploadRecording = useUploadRecording();
  const pending = uploadFile.isPending || uploadRecording.isPending;

  const limits = capabilities?.pointCloud.recording ?? DEFAULT_RECORDING_CAPABILITIES;
  const selection = useMemo(() => (picked ? recordingSelection(picked, limits) : undefined), [picked, limits]);
  const recordingReady = selection !== undefined && selection.problems.length === 0;

  const maxBytes = capabilities?.pointCloud.maxFileBytes ?? DEFAULT_MAX_CLOUD_BYTES;
  const accepted = capabilities?.pointCloud.acceptedExtensions ?? [...POINT_CLOUD_EXTENSIONS];
  const tooLarge = file !== undefined && file.size > maxBytes;
  const wrongKind = file !== undefined && !isPointCloudFileName(file.name);
  // Asked once a file is chosen, so the totals and what this one adds are on screen before the
  // upload starts. The same reading the summary below shows; a cloud that will not fit is not
  // offered for sending, and the server refuses it anyway with the same figures.
  const roomFor =
    kind === 'recording'
      ? recordingReady
        ? selection.totalBytes
        : null
      : file !== undefined && !tooLarge && !wrongKind
        ? file.size
        : null;
  const { data: room } = useStorageRoom({ addingBytes: roomFor, scan: true });
  const noRoom = roomFor !== null && room?.fits === false;
  // The run's own room on the worker's disk is shown but does not stop the upload: the recording
  // is kept either way, and the run is checked again, and refused with the figures, when it starts.
  const blocked = noRoom || (kind === 'recording' ? !recordingReady : !file || tooLarge || wrongKind);

  const close = () => {
    form.resetFields();
    setFile(undefined);
    setPicked(undefined);
    setProfileId(undefined);
    setPhase(undefined);
    setSent(undefined);
    onClose();
  };

  const pickFolder = (files: RecordingFile[]) => {
    setPicked(files);
    const chosen = recordingSelection(files, limits);
    const title = titleFromRecordingFolder(chosen.folder);
    if (title && !form.isFieldTouched('title')) {
      form.setFieldsValue({ title });
    }
  };

  const onPhase = (next: PointCloudUploadPhase, progress?: { done: number; total: number }) => {
    setPhase(next);
    setSent(progress);
  };

  const submit = async () => {
    if (blocked) {
      return;
    }
    let title: string;
    try {
      title = (await form.validateFields()).title.trim();
    } catch {
      return;
    }
    try {
      let queued: { statusPageUrl: string };
      if (kind === 'recording') {
        // The profile shown is sent as chosen: the one picked here, else the preselected one the
        // section drew (it reports it back the moment it draws it), else none — the defaults.
        queued = await uploadRecording.mutateAsync({
          caveId,
          files: selection!.files.map((candidate) => candidate.file),
          surveyTitle: title,
          profiles: shownProfile ? [shownProfile] : [],
          onPhase,
        });
        message.success(t('surveyUploads.recording.uploaded'));
      } else {
        queued = await uploadFile.mutateAsync({ caveId, file: file!, surveyTitle: title, onPhase: setPhase });
        message.success(t('surveyUploads.pointCloud.uploaded'));
      }
      close();
      navigate(queued.statusPageUrl);
    } catch (error) {
      setPhase(undefined);
      setSent(undefined);
      message.error(surveyUploadRequestErrorText(error, t));
    }
  };

  return (
    <Modal
      title={t('surveyUploads.pointCloud.uploadTitle')}
      open={open}
      onCancel={close}
      onOk={() => void submit()}
      okText={t(kind === 'recording' ? 'surveyUploads.recording.upload' : 'surveyUploads.pointCloud.upload')}
      okButtonProps={{ disabled: blocked, loading: pending }}
      cancelButtonProps={{ disabled: pending }}
      confirmLoading={pending}
      closable={!pending}
      mask={{ closable: !pending }}
      width="min(640px, 95vw)"
      destroyOnHidden
    >
      <Segmented<UploadKind>
        block
        value={kind}
        disabled={pending}
        onChange={setKind}
        style={{ marginBottom: 12 }}
        options={[
          { value: 'file', label: t('surveyUploads.recording.kindFile') },
          { value: 'recording', label: t('surveyUploads.recording.kindRecording') },
        ]}
        data-testid="point-cloud-kind"
      />

      {kind === 'recording' ? (
        <RecordingFolderSection
          limits={limits}
          selection={selection}
          onPick={pickFolder}
          profileId={profileId}
          onProfile={setProfileId}
          onShownProfile={setShownProfile}
          disabled={pending}
        />
      ) : (
        <>
      <Upload.Dragger
        accept={accepted.join(',')}
        multiple={false}
        maxCount={1}
        showUploadList={false}
        disabled={pending}
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
        </>
      )}

      <Form form={form} layout="vertical" style={{ marginTop: 16 }} disabled={pending}>
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
          {phase === 'sending' && sent
            ? t('surveyUploads.recording.sendingFile', { done: sent.done + 1, total: sent.total })
            : t(`surveyUploads.pointCloud.phase.${phase}`)}
        </Typography.Text>
      )}
    </Modal>
  );
}
