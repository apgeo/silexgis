// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { EyeOutlined } from '@ant-design/icons';
import { App, Button, Empty, Flex, Select, Table, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useAccessPreview,
  useCavingGroups,
  useUserSearch,
  type AccessCatalog,
  type AccessPreview,
  type AccessSubjectKind,
} from '../../../api/hooks.ts';
import { useDebouncedValue } from '../../../hooks/useDebouncedValue.ts';
import AccessExplanationList from '../../../components/permissions/AccessExplanationList.tsx';
import { actionSetLabels } from '../../../components/permissions/accessDisplay.ts';

interface PreviewTabProps {
  catalog: AccessCatalog;
  /** Called when a preview result has actually been looked at — unlocks saving a deny. */
  onPreviewed: () => void;
}

/**
 * "View as…": what the model would answer for a chosen user or caving group, with the
 * server's explanation of each verdict rendered verbatim. This is the look the rules tab
 * demands before a deny may be saved — the only honest way to see what it costs.
 */
export default function PreviewTab({ catalog, onPreviewed }: PreviewTabProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const preview = useAccessPreview();
  const { data: cavingGroups } = useCavingGroups();
  const [subjectKind, setSubjectKind] = useState<AccessSubjectKind>('user');
  const [subjectId, setSubjectId] = useState<string>();
  const [userQuery, setUserQuery] = useState('');
  const debouncedUserQuery = useDebouncedValue(userQuery);
  const { data: users } = useUserSearch(debouncedUserQuery);
  const [result, setResult] = useState<AccessPreview | null>(null);

  const onRun = async () => {
    if (!subjectId) {
      return;
    }
    try {
      const answer = await preview.mutateAsync({ subjectKind, subjectId });
      setResult(answer);
      onPreviewed();
    } catch {
      message.error(t('common.loadFailed'));
    }
  };

  // The catalog's order and naming, so the preview table always matches the rules editor.
  const rows = useMemo(() => {
    if (!result) {
      return [];
    }
    return catalog.domains.map((domain) => ({
      key: domain.name,
      name: domain.name,
      actions: result.domains[domain.domain] ?? 'none',
      explanations: result.explanations.filter((e) => e.domain === domain.name),
    }));
  }, [catalog, result]);

  return (
    <Flex vertical gap={12}>
      <Flex gap={8} wrap>
        <Select
          value={subjectKind}
          style={{ width: 140 }}
          onChange={(kind: AccessSubjectKind) => {
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
            style={{ flex: 1, minWidth: 200 }}
            showSearch
            optionFilterProp="label"
            placeholder={t('permissions.pickCavingGroup')}
            value={subjectId}
            onChange={setSubjectId}
            options={cavingGroups?.map((cavingGroup) => ({ value: cavingGroup.id, label: cavingGroup.name }))}
          />
        ) : (
          <Select
            style={{ flex: 1, minWidth: 200 }}
            showSearch
            filterOption={false}
            placeholder={t('permissions.pickUser')}
            value={subjectId}
            onSearch={setUserQuery}
            onChange={setSubjectId}
            options={users?.map((user) => ({
              value: user.id,
              label: user.email ? `${user.label} (${user.email})` : user.label,
            }))}
            notFoundContent={null}
          />
        )}
        <Button
          type="primary"
          icon={<EyeOutlined />}
          onClick={() => void onRun()}
          disabled={!subjectId}
          loading={preview.isPending}
          data-testid="run-preview"
        >
          {t('permissionGroups.runPreview')}
        </Button>
      </Flex>

      {result === null ? (
        <Empty description={t('permissionGroups.previewIntro')} />
      ) : (
        <>
          <Typography.Text type="secondary">{t('permissionGroups.previewLegend')}</Typography.Text>
          <Table
            scroll={{ x: 'max-content' }}
            size="small"
            pagination={false}
            dataSource={rows}
            expandable={{
              // The per-action explanations, straight from the server.
              expandedRowRender: (row) => <AccessExplanationList explanations={row.explanations} />,
            }}
            columns={[
              {
                title: t('access.domain'),
                dataIndex: 'name',
                width: 220,
                render: (name: string) => t(`access.domains.${name}`),
              },
              {
                title: t('access.actionsColumn'),
                key: 'actions',
                render: (_, row) => {
                  const labels = actionSetLabels(t, row.actions);
                  return labels.length === 0
                    ? <Typography.Text type="secondary">{t('permissionGroups.noRights')}</Typography.Text>
                    : labels.map((label) => <Tag key={label}>{label}</Tag>);
                },
              },
            ]}
          />
        </>
      )}
    </Flex>
  );
}
