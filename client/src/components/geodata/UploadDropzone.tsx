// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback, useRef, useState } from 'react';
import { Flex, Progress, Typography, Upload } from 'antd';
import { CloseOutlined, InboxOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import Button from '../Button.tsx';
import type { UploadProgress } from '../../api/uploadWithProgress.ts';
import { formatSize } from '../attachments/fileFormat.ts';
import { uploadErrorText } from './uploadErrorText.ts';

type UploadState = 'uploading' | 'checking' | 'done' | 'failed';

interface UploadItem {
  uid: string;
  name: string;
  size: number;
  loaded: number;
  state: UploadState;
  message?: string;
}

interface UploadDropzoneProps {
  accept: string;
  prompt: string;
  hint: string;
  /** Sends one file; resolves with what the server called it, or rejects with why it did not take it. */
  upload: (file: File, onProgress: (progress: UploadProgress) => void) => Promise<{ name: string }>;
  /** What the row says once the server has the file — typically that its processing is queued. */
  doneText: (name: string) => string;
}

/**
 * A drop zone that shows what happens to each file dropped on it: how much of it has gone up,
 * that the server is looking at it once every byte is sent, and then that it was taken — or why
 * not, in a sentence that stays on screen until it is dismissed.
 *
 * A toast alone was not enough: dropping a large map sheet showed nothing at all for the minutes
 * it took to send, and a refusal was a generic line that vanished in a few seconds.
 */
export default function UploadDropzone({ accept, prompt, hint, upload, doneText }: UploadDropzoneProps) {
  const { t } = useTranslation();
  const [items, setItems] = useState<UploadItem[]>([]);
  const sequence = useRef(0);

  const change = useCallback((uid: string, patch: Partial<UploadItem>) => {
    setItems((current) => current.map((item) => (item.uid === uid ? { ...item, ...patch } : item)));
  }, []);

  const start = (file: File) => {
    const uid = `upload-${++sequence.current}`;
    setItems((current) => [...current, { uid, name: file.name, size: file.size, loaded: 0, state: 'uploading' }]);
    upload(file, ({ loaded, total }) =>
      change(uid, { loaded, size: total, state: loaded >= total ? 'checking' : 'uploading' }),
    )
      .then((created) => change(uid, { state: 'done', loaded: file.size, message: doneText(created.name) }))
      .catch((error: unknown) => change(uid, { state: 'failed', message: uploadErrorText(error, t) }));
  };

  const finished = items.filter((item) => item.state === 'done' || item.state === 'failed');

  return (
    <div style={{ marginBottom: 16 }}>
      <Upload.Dragger
        multiple
        accept={accept}
        showUploadList={false}
        // Every file is sent here, by hand, so its progress can be followed; nothing else uploads.
        beforeUpload={(file) => {
          start(file);
          return false;
        }}
      >
        <p className="ant-upload-drag-icon">
          <InboxOutlined />
        </p>
        <p className="ant-upload-text">{prompt}</p>
        <p className="ant-upload-hint">{hint}</p>
      </Upload.Dragger>

      {items.length > 0 && (
        <Flex vertical gap={4} style={{ marginTop: 8 }} data-testid="upload-list" aria-live="polite">
          {items.map((item) => (
            <UploadRow
              key={item.uid}
              item={item}
              onDismiss={() => setItems((current) => current.filter((other) => other.uid !== item.uid))}
            />
          ))}
          {finished.length > 1 && (
            <Button
              size="small"
              type="link"
              style={{ alignSelf: 'flex-end' }}
              onClick={() =>
                setItems((current) => current.filter((item) => item.state === 'uploading' || item.state === 'checking'))
              }
            >
              {t('geodata.upload.clearFinished')}
            </Button>
          )}
        </Flex>
      )}
    </div>
  );
}

function UploadRow({ item, onDismiss }: { item: UploadItem; onDismiss: () => void }) {
  const { t } = useTranslation();
  const percent = item.size > 0 ? Math.floor((item.loaded / item.size) * 100) : 0;
  const status =
    item.state === 'failed' ? 'exception' : item.state === 'done' ? 'success' : ('active' as const);
  const line =
    item.state === 'uploading'
      ? t('geodata.upload.sending', { sent: formatSize(item.loaded), total: formatSize(item.size) })
      : item.state === 'checking'
        ? t('geodata.upload.checking')
        : item.message;

  return (
    <Flex
      align="center"
      gap={8}
      data-testid="upload-item"
      data-state={item.state}
      data-name={item.name}
      style={{ padding: '4px 8px', borderRadius: 4, background: 'var(--ant-color-fill-quaternary, rgba(0,0,0,0.02))' }}
    >
      <Flex vertical style={{ flex: 1, minWidth: 0 }}>
        <Typography.Text ellipsis={{ tooltip: item.name }} strong>
          {item.name}
        </Typography.Text>
        <Progress percent={item.state === 'done' ? 100 : percent} status={status} size="small" showInfo={false} />
        <Typography.Text type={item.state === 'failed' ? 'danger' : 'secondary'} style={{ fontSize: 12 }}>
          {line}
        </Typography.Text>
      </Flex>
      {(item.state === 'done' || item.state === 'failed') && (
        <Button
          size="small"
          type="text"
          icon={<CloseOutlined />}
          aria-label={t('geodata.upload.dismiss', { name: item.name })}
          onClick={onDismiss}
        />
      )}
    </Flex>
  );
}
