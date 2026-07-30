// SPDX-License-Identifier: AGPL-3.0-or-later
import { DeleteOutlined, PictureOutlined, UploadOutlined, UserOutlined } from '@ant-design/icons';
import { useState } from 'react';
import { App, Avatar, Button, Flex, Modal, Upload, theme } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useAvatarPresets,
  useRemoveAvatar,
  useSetAvatarPreset,
  useUploadAvatar,
  type Me,
} from '../../api/hooks.ts';
import { avatarPresetUrl } from './avatarPresets.ts';

/** The server refuses more, but rejecting locally saves sending a large file to be told so. */
const MAX_AVATAR_BYTES = 5 * 1024 * 1024;

interface Props {
  me: Me;
}

/**
 * The account's avatar: upload an image, or pick one of the built-in ones. The two are exclusive
 * — choosing a preset drops an uploaded image, and vice versa — which the server also enforces.
 */
export default function AvatarPicker({ me }: Props) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { token } = theme.useToken();
  const { data: presets } = useAvatarPresets();
  const upload = useUploadAvatar();
  const setPreset = useSetAvatarPreset();
  const remove = useRemoveAvatar();
  const [choosing, setChoosing] = useState(false);

  const src = me.avatarUrl ?? (me.avatarPreset ? avatarPresetUrl(me.avatarPreset) : undefined);

  return (
    <Flex gap={16} align="center" wrap>
      <Avatar size={96} src={src} icon={<UserOutlined />} alt={t('settings.profile.avatar')} />
      <Flex vertical gap={8}>
        <Upload
          showUploadList={false}
          accept="image/png,image/jpeg,image/webp,image/gif"
          maxCount={1}
          beforeUpload={(file) => {
            if (file.size > MAX_AVATAR_BYTES) {
              message.error(t('settings.avatar.tooLarge'));
              return Upload.LIST_IGNORE;
            }
            return true;
          }}
          customRequest={({ file, onSuccess, onError }) => {
            upload
              .mutateAsync(file as File)
              .then(() => {
                message.success(t('common.saved'));
                onSuccess?.(null);
              })
              .catch((e: Error) => {
                message.error(t('settings.avatar.uploadFailed'));
                onError?.(e);
              });
          }}
        >
          <Button icon={<UploadOutlined />} loading={upload.isPending}>
            {t('settings.avatar.upload')}
          </Button>
        </Upload>
        <Button icon={<PictureOutlined />} onClick={() => setChoosing(true)}>
          {t('settings.avatar.preset')}
        </Button>
        {src && (
          <Button
            type="text"
            danger
            icon={<DeleteOutlined />}
            loading={remove.isPending}
            onClick={() => {
              remove.mutate(undefined, {
                onSuccess: () => message.success(t('common.saved')),
                onError: () => message.error(t('common.saveFailed')),
              });
            }}
          >
            {t('settings.avatar.remove')}
          </Button>
        )}
      </Flex>

      <Modal
        open={choosing}
        title={t('settings.avatar.choosePreset')}
        destroyOnHidden
        footer={null}
        onCancel={() => setChoosing(false)}
      >
        <Flex wrap gap={12}>
          {presets?.presets.map((id) => (
            <Button
              key={id}
              type="text"
              style={{ height: 'auto', padding: 4 }}
              aria-label={t(`settings.avatar.presetNames.${id}`)}
              onClick={() => {
                setPreset.mutate(id, {
                  onSuccess: () => {
                    setChoosing(false);
                    message.success(t('common.saved'));
                  },
                  onError: () => message.error(t('common.saveFailed')),
                });
              }}
            >
              <Avatar
                size={64}
                src={avatarPresetUrl(id)}
                alt={t(`settings.avatar.presetNames.${id}`)}
                style={
                  me.avatarPreset === id
                    ? { outline: `2px solid ${token.colorPrimary}`, outlineOffset: 2 }
                    : undefined
                }
              />
            </Button>
          ))}
        </Flex>
      </Modal>
    </Flex>
  );
}
