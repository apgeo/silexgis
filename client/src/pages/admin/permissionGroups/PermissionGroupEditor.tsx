// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { LockOutlined } from '@ant-design/icons';
import { App, Button, Drawer, Flex, Form, Input, Tabs, Tag } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useUpdatePermissionGroup,
  type AccessCatalog,
  type PermissionGroup,
} from '../../../api/hooks.ts';
import { ApiError } from '../../../api/client.ts';
import MembersTab from './MembersTab.tsx';
import PreviewTab from './PreviewTab.tsx';
import RulesTab from './RulesTab.tsx';

interface PermissionGroupEditorProps {
  group: PermissionGroup;
  catalog: AccessCatalog;
  open: boolean;
  onClose: () => void;
}

/**
 * One permission group's whole surface: its name and description, its rules, its
 * trustees, and the "view as…" preview the rules tab requires before a deny is saved.
 * Protected groups refuse rename and deletion server-side; the editor mirrors that
 * instead of letting the attempt fail.
 */
export default function PermissionGroupEditor({ group, catalog, open, onClose }: PermissionGroupEditorProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const updateGroup = useUpdatePermissionGroup(group.id);
  const [form] = Form.useForm<{ name: string; description?: string }>();
  const [activeTab, setActiveTab] = useState('rules');
  // Whether a preview has been looked at since the rules last changed — saving a
  // ruleset that contains a deny is gated on it.
  const [previewSeen, setPreviewSeen] = useState(false);

  const onRename = async (values: { name: string; description?: string }) => {
    try {
      await updateGroup.mutateAsync({ name: values.name, description: values.description ?? null });
      message.success(t('common.saved'));
    } catch (error) {
      message.error(
        error instanceof ApiError && error.code === 'permission_group.name_taken'
          ? t('permissionGroups.nameTaken')
          : t('common.saveFailed'),
      );
    }
  };

  return (
    <Drawer
      title={
        <Flex gap={8} align="center">
          {group.name}
          {group.isProtected && <Tag icon={<LockOutlined />}>{t('permissionGroups.protected')}</Tag>}
          {group.isSeeded && <Tag color="blue">{t('permissionGroups.seeded')}</Tag>}
        </Flex>
      }
      open={open}
      onClose={onClose}
      size={1000}
      destroyOnHidden
    >
      <Form
        form={form}
        layout="inline"
        initialValues={{ name: group.name, description: group.description ?? undefined }}
        onFinish={(values) => void onRename(values)}
        style={{ marginBottom: 16 }}
      >
        <Form.Item name="name" rules={[{ required: true }, { max: 100 }]}>
          {/* Renaming a protected group would move the anchor the guards key on. */}
          <Input
            placeholder={t('permissionGroups.name')}
            disabled={group.isProtected}
            style={{ width: 220 }}
          />
        </Form.Item>
        <Form.Item name="description" rules={[{ max: 1000 }]} style={{ flex: 1 }}>
          <Input placeholder={t('permissionGroups.description')} />
        </Form.Item>
        <Form.Item>
          <Button htmlType="submit" loading={updateGroup.isPending}>
            {t('common.save')}
          </Button>
        </Form.Item>
      </Form>

      <Tabs
        activeKey={activeTab}
        onChange={setActiveTab}
        items={[
          {
            key: 'rules',
            label: t('permissionGroups.rulesTab'),
            children: (
              <RulesTab
                group={group}
                catalog={catalog}
                previewSeen={previewSeen}
                onRulesChanged={() => setPreviewSeen(false)}
                onRequestPreview={() => setActiveTab('preview')}
              />
            ),
          },
          {
            key: 'members',
            label: t('permissionGroups.membersTab'),
            children: <MembersTab group={group} />,
          },
          {
            key: 'preview',
            label: t('permissionGroups.previewTab'),
            children: <PreviewTab catalog={catalog} onPreviewed={() => setPreviewSeen(true)} />,
          },
        ]}
      />
    </Drawer>
  );
}
