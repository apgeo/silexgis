// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo } from 'react';
import { FolderOpenOutlined } from '@ant-design/icons';
import { Alert, Checkbox, Descriptions, Flex, Select, Table, Tag, Typography, Upload } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  candidateOf,
  ODOMETRY_STEP_ID,
  useProcessingProfiles,
  useRememberedProcessingProfiles,
  useScanStepParameterSchema,
  type RecordingCandidate,
  type RecordingProblem,
  type RecordingSelection,
  type SurveyRecordingCapabilities,
} from '../../api/hooks.ts';
import { formatSize } from '../../components/attachments/fileFormat.ts';
import StorageRoomSummary from '../../components/storage/StorageRoomSummary.tsx';
import { formatBytes } from '../../components/storage/storageFigures.ts';
import { parameterGroups } from '../../components/scanProfiles/parameterSchema.ts';
import { changedProfileKeys, preferredProfileId } from '../../components/surveyUploads/recordingProfile.ts';

export type RecordingFile = RecordingCandidate & { file: File };

/** The words of one problem with the chosen folder. */
function problemText(problem: RecordingProblem, t: (key: string, options?: Record<string, unknown>) => string) {
  switch (problem.code) {
    case 'unpaired':
      return t('surveyUploads.recording.problem.unpaired', { chunk: problem.chunk });
    case 'tooManyFiles':
      return t('surveyUploads.recording.problem.tooManyFiles', { max: problem.max });
    case 'tooLarge':
      return t('surveyUploads.recording.problem.tooLarge', { max: formatBytes(problem.max) });
    case 'fileTooLarge':
      return t('surveyUploads.recording.problem.fileTooLarge', { name: problem.name, max: formatBytes(problem.max) });
    case 'duplicate':
    case 'empty':
      return t(`surveyUploads.recording.problem.${problem.code}`, { name: problem.name });
    default:
      return t(`surveyUploads.recording.problem.${problem.code}`);
  }
}

/**
 * The raw recording half of the point-cloud dialog: the scanner's folder dropped or picked, the
 * files that will be sent and their size, what was left out, what the server would refuse, the
 * room the upload and its processing need, and the odometry profile to run it with.
 *
 * The folder is held by the dialog; this part draws it and reports the person's picks back. The
 * profile shown is the dialog's choice when the person made one, else the one they used last,
 * else the built-in that changes nothing.
 */
export default function RecordingFolderSection({
  limits,
  selection,
  onPick,
  profileId,
  onProfile,
  onShownProfile,
  odometryElsewhere,
  onOdometryElsewhere,
  disabled,
}: {
  limits: SurveyRecordingCapabilities;
  selection: RecordingSelection<RecordingFile> | undefined;
  onPick: (picked: RecordingFile[]) => void;
  /** The profile chosen in this dialog: undefined while the person has not chosen, null for the defaults. */
  profileId: string | null | undefined;
  onProfile: (id: string | null) => void;
  /** Told the profile on screen whenever it changes, chosen or preselected: what the upload sends. */
  onShownProfile: (id: string | null) => void;
  /** Whether the odometry is run on another computer instead of the server's worker. */
  odometryElsewhere: boolean;
  onOdometryElsewhere: (elsewhere: boolean) => void;
  disabled: boolean;
}) {
  const { t } = useTranslation();
  const { data: profiles } = useProcessingProfiles(ODOMETRY_STEP_ID);
  const { data: remembered } = useRememberedProcessingProfiles(ODOMETRY_STEP_ID);
  const { data: schema } = useScanStepParameterSchema(ODOMETRY_STEP_ID);
  const groups = useMemo(
    () => (schema ? parameterGroups(schema.schema, schema.defaults) : undefined),
    [schema],
  );

  const offered = useMemo(() => profiles ?? [], [profiles]);
  const shown =
    profileId !== undefined ? profileId : preferredProfileId(ODOMETRY_STEP_ID, offered, remembered, groups);
  const chosen = offered.find((profile) => profile.id === shown);
  const changed = chosen && groups !== undefined ? changedProfileKeys(chosen.values, groups) : [];
  const fits = selection && selection.problems.length === 0;

  useEffect(() => {
    onShownProfile(chosen ? chosen.id : null);
  }, [chosen, onShownProfile]);

  return (
    <Flex vertical gap={12}>
      <Upload.Dragger
        directory
        multiple
        showUploadList={false}
        disabled={disabled}
        beforeUpload={(file, fileList) => {
          // Called once per file of the drop with the whole drop beside it; the first call takes
          // the drop, so a second drop replaces the folder rather than adding to it.
          if (file === fileList[0]) {
            onPick(fileList.map(candidateOf));
          }
          return false;
        }}
        data-testid="point-cloud-folder"
      >
        <p className="ant-upload-drag-icon">
          <FolderOpenOutlined />
        </p>
        <p className="ant-upload-text">
          {selection?.folder ? selection.folder : t('surveyUploads.recording.chooseFolder')}
        </p>
        <p className="ant-upload-hint">
          {t('surveyUploads.recording.folderHint', {
            max: formatBytes(limits.maxTotalBytes),
            files: limits.maxFiles.toLocaleString(),
          })}
        </p>
      </Upload.Dragger>

      {selection && (
        <>
          <Descriptions size="small" column={{ xs: 1, sm: 2 }} bordered data-testid="point-cloud-recording-summary">
            <Descriptions.Item label={t('surveyUploads.recording.files')}>
              <span data-testid="point-cloud-recording-count">{selection.files.length}</span>
            </Descriptions.Item>
            <Descriptions.Item label={t('surveyUploads.recording.size')}>
              <span data-testid="point-cloud-recording-size">{formatBytes(selection.totalBytes)}</span>
            </Descriptions.Item>
            <Descriptions.Item label={t('surveyUploads.recording.chunks')}>{selection.chunkCount}</Descriptions.Item>
            <Descriptions.Item label={t('surveyUploads.recording.leftOut')}>
              <span data-testid="point-cloud-recording-skipped">{selection.skipped}</span>
            </Descriptions.Item>
          </Descriptions>
          {selection.skipped > 0 && (
            <Typography.Text type="secondary">{t('surveyUploads.recording.leftOutHint')}</Typography.Text>
          )}
          {selection.files.length > 0 && (
            <Table<RecordingFile>
              size="small"
              rowKey="name"
              virtual
              scroll={{ y: 180 }}
              pagination={false}
              dataSource={selection.files}
              data-testid="point-cloud-recording-files"
              columns={[
                { title: t('surveyUploads.recording.fileName'), dataIndex: 'name' },
                {
                  title: t('surveyUploads.recording.fileSize'),
                  dataIndex: 'size',
                  width: 110,
                  align: 'right',
                  render: (size: number) => formatSize(size),
                },
              ]}
            />
          )}
          {selection.problems.map((problem) => (
            <Alert
              key={problem.code}
              type="error"
              showIcon
              data-testid={`point-cloud-recording-problem-${problem.code}`}
              title={problemText(problem, t)}
            />
          ))}
          {selection.hasGnss && (
            <Alert type="info" showIcon data-testid="point-cloud-recording-gnss" title={t('surveyUploads.recording.gnssHint')} />
          )}
          {fits && (
            <>
              <StorageRoomSummary addingBytes={selection.totalBytes} scan />
              {/* Done elsewhere, the odometry's cache never lands on the worker's disk. */}
              {!odometryElsewhere && (
                <>
                  <Typography.Text type="secondary">{t('surveyUploads.recording.runRoom')}</Typography.Text>
                  <StorageRoomSummary addingBytes={selection.totalBytes * limits.odometryWorkerFactor} scan processing />
                </>
              )}
            </>
          )}
        </>
      )}

      <Flex vertical gap={4}>
        <Typography.Text strong>{t('surveyUploads.recording.profile')}</Typography.Text>
        <Select<string>
          allowClear
          disabled={disabled}
          value={shown ?? undefined}
          placeholder={t('surveyUploads.recording.profileDefaults')}
          onChange={(value) => onProfile(value ?? null)}
          options={offered.map((profile) => ({
            value: profile.id,
            label: `${profile.name} · ${t(`surveyUploads.recording.scope.${profile.scope}`)}`,
          }))}
          data-testid="point-cloud-profile"
        />
        {chosen && changed.length > 0 ? (
          <Flex gap={4} wrap align="center">
            <Tag color="orange" data-testid="point-cloud-profile-differs">
              {t('surveyUploads.recording.profileDiffers', { count: changed.length })}
            </Tag>
            <Typography.Text type="secondary">{changed.map((key) => key.replace(/^values\./, '')).join(', ')}</Typography.Text>
          </Flex>
        ) : groups === undefined ? null : (
          <Typography.Text type="secondary" data-testid="point-cloud-profile-default">
            {t('surveyUploads.recording.profileIsDefault')}
          </Typography.Text>
        )}
        <Typography.Text type="secondary">{t('surveyUploads.recording.profileHint')}</Typography.Text>
      </Flex>

      <Flex vertical gap={4}>
        <Checkbox
          checked={odometryElsewhere}
          disabled={disabled}
          onChange={(event) => onOdometryElsewhere(event.target.checked)}
          data-testid="point-cloud-odometry-elsewhere"
        >
          {t('surveyUploads.recording.odometryElsewhere')}
        </Checkbox>
        <Typography.Text type="secondary">{t('surveyUploads.recording.odometryElsewhereHint')}</Typography.Text>
      </Flex>
    </Flex>
  );
}
