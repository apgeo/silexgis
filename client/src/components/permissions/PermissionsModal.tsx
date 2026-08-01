// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { DeleteOutlined, PlusOutlined } from '@ant-design/icons';
import { App, Button, Checkbox, Flex, Modal, Select, Table, Tag } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useAcl,
  useReplaceAcl,
  useCavingGroups,
  useUserSearch,
  type AclEntry,
  type EntityType,
} from '../../api/hooks.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';

const allPermissions = ['read', 'write', 'delete', 'share', 'managePermissions', 'viewExactLocation'] as const;
type PermissionFlag = (typeof allPermissions)[number];

interface DraftEntry {
  subjectKind: 'user' | 'cavingGroup';
  subjectId: string;
  subjectName: string | null;
  permissions: Set<PermissionFlag>;
}

interface PermissionsModalProps {
  /** Grantable object class; anything in the feature world is granted as 'feature'. */
  entityType: EntityType;
  entityId: string;
  open: boolean;
  onClose: () => void;
}

/** Parses the server's comma-joined flags string ("read, write") into a set. */
function parseFlags(permissions: string): Set<PermissionFlag> {
  return new Set(
    permissions.split(',').map((x) => x.trim() as PermissionFlag).filter((x) => (allPermissions as readonly string[]).includes(x)),
  );
}

/**
 * Explicit-grant editor for one object: rows of user/caving-group subjects with permission
 * checkboxes, saved as a full replace. Requires ManagePermissions server-side.
 */
export default function PermissionsModal({ entityType, entityId, open, onClose }: PermissionsModalProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data: acl, isError } = useAcl(entityType, entityId, open);
  const replaceAcl = useReplaceAcl(entityType, entityId);
  const { data: cavingGroups } = useCavingGroups();
  const [entries, setEntries] = useState<DraftEntry[]>([]);
  const [subjectKind, setSubjectKind] = useState<'user' | 'cavingGroup'>('user');
  const [subjectId, setSubjectId] = useState<string>();
  const [userQuery, setUserQuery] = useState('');
  const debouncedUserQuery = useDebouncedValue(userQuery);
  const { data: users } = useUserSearch(debouncedUserQuery);

  useEffect(() => {
    if (open && acl) {
      setEntries(acl.map((entry: AclEntry) => ({
        subjectKind: entry.subjectKind,
        subjectId: entry.subjectId,
        subjectName: entry.subjectName ?? null,
        permissions: parseFlags(entry.actions),
      })));
    }
  }, [open, acl]);

  const addEntry = () => {
    if (!subjectId || entries.some((e) => e.subjectId === subjectId && e.subjectKind === subjectKind)) {
      return;
    }
    const name = subjectKind === 'cavingGroup'
      ? cavingGroups?.find((x) => x.id === subjectId)?.name ?? null
      : users?.find((x) => x.id === subjectId)?.label ?? null;
    setEntries([...entries, {
      subjectKind,
      subjectId,
      subjectName: name,
      permissions: new Set<PermissionFlag>(['read']),
    }]);
    setSubjectId(undefined);
    setUserQuery('');
  };

  const togglePermission = (index: number, flag: PermissionFlag, checked: boolean) => {
    setEntries(entries.map((entry, i) => {
      if (i !== index) {
        return entry;
      }
      const next = new Set(entry.permissions);
      if (checked) {
        next.add(flag);
      } else {
        next.delete(flag);
      }
      return { ...entry, permissions: next };
    }));
  };

  const onSave = async () => {
    try {
      await replaceAcl.mutateAsync(entries
        .filter((e) => e.permissions.size > 0)
        .map((e) => ({
          subjectKind: e.subjectKind,
          subjectId: e.subjectId,
          actions: [...e.permissions].join(', ') as AclEntry['actions'],
          // This tab edits plain grants on this one object; denies and subtree reach
          // are the richer permissions surface's business.
          effect: 'allow' as const,
          scopeKind: 'object' as const,
        })));
      message.success(t('common.saved'));
      onClose();
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  return (
    <Modal
      title={t('permissions.title')}
      open={open}
      onCancel={onClose}
      onOk={() => void onSave()}
      confirmLoading={replaceAcl.isPending}
      width={760}
      destroyOnHidden
    >
      {isError ? (
        <Tag color="warning">{t('permissions.notManager')}</Tag>
      ) : (
        <>
          <Flex gap={8} style={{ marginBottom: 12 }}>
            <Select
              value={subjectKind}
              style={{ width: 110 }}
              onChange={(kind: 'user' | 'cavingGroup') => {
                setSubjectKind(kind);
                setSubjectId(undefined);
              }}
              options={[
                { value: 'user', label: t('permissions.user') },
                { value: 'cavingGroup', label: t('permissions.cavingGroup') },
              ]}
            />
            {subjectKind === 'cavingGroup' ? (
              <Select
                style={{ flex: 1 }}
                // CavingGroups arrive in full, so this filters client-side — unlike the user
                // picker beside it, which searches the server. Named explicitly because
                // the option values are ids: filtering the default value prop would
                // match nothing a person could type.
                showSearch
                optionFilterProp="label"
                placeholder={t('permissions.pickCavingGroup')}
                value={subjectId}
                onChange={setSubjectId}
                options={cavingGroups?.map((group) => ({ value: group.id, label: group.name }))}
              />
            ) : (
              <Select
                style={{ flex: 1 }}
                showSearch
                filterOption={false}
                placeholder={t('permissions.pickUser')}
                value={subjectId}
                onSearch={setUserQuery}
                onChange={setSubjectId}
                options={users?.map((user) => ({
                  value: user.id,
                  // The address is only present when the person shares it; the label always is.
                  label: user.email ? `${user.label} (${user.email})` : user.label,
                }))}
                notFoundContent={null}
              />
            )}
            <Button icon={<PlusOutlined />} onClick={addEntry} disabled={!subjectId}>
              {t('permissions.addGrant')}
            </Button>
          </Flex>

          <Table<DraftEntry>
            scroll={{ x: 'max-content' }}
            rowKey={(entry) => `${entry.subjectKind}:${entry.subjectId}`}
            size="small"
            pagination={false}
            dataSource={entries}
            columns={[
              {
                title: t('permissions.subject'),
                key: 'subject',
                render: (_, entry) => (
                  <>
                    <Tag>{t(`permissions.${entry.subjectKind}`)}</Tag>
                    {entry.subjectName ?? entry.subjectId}
                  </>
                ),
              },
              ...allPermissions.map((flag) => ({
                title: t(`permissions.flags.${flag}`),
                key: flag,
                width: 90,
                align: 'center' as const,
                render: (_: unknown, entry: DraftEntry, index: number) => (
                  <Checkbox
                    checked={entry.permissions.has(flag)}
                    onChange={(e) => togglePermission(index, flag, e.target.checked)}
                  />
                ),
              })),
              {
                title: '',
                key: 'remove',
                width: 50,
                render: (_, __, index) => (
                  <Button
                    size="small"
                    type="text"
                    danger
                    icon={<DeleteOutlined />}
                    onClick={() => setEntries(entries.filter((_, i) => i !== index))}
                  />
                ),
              },
            ]}
          />
        </>
      )}
    </Modal>
  );
}
