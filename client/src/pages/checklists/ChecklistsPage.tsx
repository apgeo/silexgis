// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { DeleteOutlined, EditOutlined, PlusOutlined } from '@ant-design/icons';
import {
  App,
  Button,
  Card,
  Empty,
  Flex,
  Form,
  Input,
  Modal,
  Popconfirm,
  Select,
  Skeleton,
  Space,
  Tag,
  Typography,
} from 'antd';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';
import {
  useCavingGroups,
  useChecklists,
  useCreateChecklist,
  useDeleteChecklist,
  useUpdateChecklist,
  type ChecklistInfo,
  type ChecklistWrite,
} from '../../api/hooks.ts';
import List from '../../components/List.tsx';

/** The audience a list is given, in the order they widen. */
const visibilities = ['private', 'cavingGroup', 'authenticated', 'public'] as const;

type FormValues = {
  title: string;
  description?: string;
  visibility: ChecklistWrite['visibility'];
  cavingGroupId?: string | null;
  items: { id?: string; text: string }[];
};

/**
 * Where a person writes the lists trips work through.
 *
 * There is one kind of list on this page, deliberately. What an administrator publishes for the
 * whole installation is a row written here like any other and given an audience everyone falls
 * inside — so the page has no "publish" act, no separate section, and no notion of a default. A
 * wider audience is the only difference, and it is chosen from the same control a caver uses to
 * share their own list with their club.
 *
 * Nothing on this page knows what any trip has settled. How much of a list a party has worked
 * through is read on that trip, and it changes nothing about the list.
 */
export default function ChecklistsPage() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data, isLoading } = useChecklists();
  const { data: cavingGroups } = useCavingGroups();
  const create = useCreateChecklist();
  const update = useUpdateChecklist();
  const remove = useDeleteChecklist();
  const [editing, setEditing] = useState<ChecklistInfo | null>(null);
  const [open, setOpen] = useState(false);
  const [form] = Form.useForm<FormValues>();

  const openFor = (list: ChecklistInfo | null) => {
    setEditing(list);
    form.setFieldsValue({
      title: list?.title ?? '',
      description: list?.description ?? undefined,
      visibility: list?.visibility ?? 'private',
      cavingGroupId: list?.cavingGroupId ?? undefined,
      // The line's id travels with it so that rewording keeps every confirmation made against
      // it — a line rewritten the morning of the trip is the same line, not a new one.
      items: list?.items.map((item) => ({ id: item.id, text: item.text })) ?? [{ text: '' }],
    });
    setOpen(true);
  };

  const submit = async (values: FormValues) => {
    const body: ChecklistWrite = {
      title: values.title,
      description: values.description ?? null,
      visibility: values.visibility,
      cavingGroupId: values.cavingGroupId ?? null,
      items: (values.items ?? []).map((item) => ({ id: item.id ?? null, text: item.text })),
    };
    try {
      if (editing) {
        await update.mutateAsync({ id: editing.id, body });
      } else {
        await create.mutateAsync(body);
      }
      setOpen(false);
      void message.success(t('checklists.saved'));
    } catch (error) {
      void message.error(error instanceof ApiError ? error.detail : t('checklists.saveFailed'));
    }
  };

  return (
    <>
      <Flex justify="space-between" align="center" gap="middle" wrap style={{ marginBottom: 12 }}>
        <Space direction="vertical" size={0}>
          <Typography.Title level={3} style={{ margin: 0 }}>
            {t('checklists.title')}
          </Typography.Title>
          <Typography.Text type="secondary">{t('checklists.subtitle')}</Typography.Text>
        </Space>
        <Button type="primary" icon={<PlusOutlined />} onClick={() => openFor(null)}>
          {t('checklists.new')}
        </Button>
      </Flex>

      {isLoading ? (
        <Skeleton active />
      ) : !data?.length ? (
        <Empty description={t('checklists.empty')} />
      ) : (
        <List
          dataSource={data}
          renderItem={(list) => (
            <Card size="small" style={{ marginBottom: 12 }}>
              <Flex justify="space-between" align="flex-start" gap="middle" wrap>
                <Space direction="vertical" size={2}>
                  <Typography.Text strong>{list.title}</Typography.Text>
                  {list.description ? (
                    <Typography.Text type="secondary">{list.description}</Typography.Text>
                  ) : null}
                  <Space size={4} wrap>
                    <Tag>{t(`checklists.visibilityValues.${list.visibility}`)}</Tag>
                    <Tag>{t('checklists.lineCount', { count: list.items.length })}</Tag>
                  </Space>
                </Space>
                <Space>
                  <Button
                    icon={<EditOutlined />}
                    onClick={() => openFor(list)}
                    aria-label={t('common.edit')}
                  />
                  <Popconfirm
                    title={t('checklists.deleteConfirm')}
                    onConfirm={() => {
                      remove.mutate(list.id, {
                        onError: (error) =>
                          void message.error(
                            error instanceof ApiError ? error.detail : t('checklists.saveFailed'),
                          ),
                      });
                    }}
                  >
                    <Button danger icon={<DeleteOutlined />} aria-label={t('checklists.delete')} />
                  </Popconfirm>
                </Space>
              </Flex>
            </Card>
          )}
        />
      )}

      <Modal
        open={open}
        title={editing ? t('checklists.edit') : t('checklists.new')}
        onCancel={() => setOpen(false)}
        onOk={() => void form.submit()}
        confirmLoading={create.isPending || update.isPending}
        destroyOnHidden
      >
        <Form form={form} layout="vertical" onFinish={(values) => void submit(values)}>
          <Form.Item
            name="title"
            label={t('checklists.fieldTitle')}
            rules={[{ required: true, max: 200 }]}
          >
            <Input />
          </Form.Item>
          <Form.Item name="description" label={t('checklists.fieldDescription')} rules={[{ max: 2000 }]}>
            <Input.TextArea rows={2} />
          </Form.Item>
          <Form.Item
            name="visibility"
            label={t('checklists.fieldVisibility')}
            extra={t('checklists.visibilityHint')}
            rules={[{ required: true }]}
          >
            <Select
              options={visibilities.map((value) => ({
                value,
                label: t(`checklists.visibilityValues.${value}`),
              }))}
            />
          </Form.Item>
          <Form.Item name="cavingGroupId" label={t('checklists.fieldCavingGroup')}>
            <Select
              allowClear
              options={(cavingGroups ?? []).map((group) => ({
                value: group.id,
                label: group.name,
              }))}
            />
          </Form.Item>

          <Form.List name="items">
            {(fields, { add, remove: removeLine }) => (
              <Space direction="vertical" size={4} style={{ width: '100%' }}>
                <Typography.Text strong>{t('checklists.fieldItems')}</Typography.Text>
                {fields.map((field) => (
                  <Flex key={field.key} gap={8} align="baseline">
                    {/* The line's id rides along hidden: it is what a confirmation names, so
                        losing it here would strand every tick made against the line. */}
                    <Form.Item name={[field.name, 'id']} hidden noStyle>
                      <Input type="hidden" />
                    </Form.Item>
                    <Form.Item
                      name={[field.name, 'text']}
                      style={{ flex: 1, marginBottom: 8 }}
                      rules={[{ required: true, max: 500 }]}
                    >
                      <Input placeholder={t('checklists.itemPlaceholder')} />
                    </Form.Item>
                    <Button
                      type="text"
                      danger
                      icon={<DeleteOutlined />}
                      aria-label={t('checklists.removeLine')}
                      onClick={() => removeLine(field.name)}
                    />
                  </Flex>
                ))}
                <Button type="dashed" icon={<PlusOutlined />} onClick={() => add({ text: '' })} block>
                  {t('checklists.addLine')}
                </Button>
              </Space>
            )}
          </Form.List>
        </Form>
      </Modal>
    </>
  );
}
