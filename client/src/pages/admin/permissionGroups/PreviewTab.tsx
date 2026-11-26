// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { EyeOutlined } from '@ant-design/icons';
import { App, Empty, Flex, Table, Tag, Typography } from 'antd';
import Button from '../../../components/Button.tsx';
import { useTranslation } from 'react-i18next';
import {
  useAccessPreview,
  type AccessCatalog,
  type AccessPreview,
  type AccessSubjectKind,
} from '../../../api/hooks.ts';
import SubjectPicker from '../../../components/permissions/SubjectPicker.tsx';
import AccessExplanationList from '../../../components/permissions/AccessExplanationList.tsx';
import { actionSetLabels } from '../../../components/permissions/accessDisplay.ts';
import HiddenTitle from '../../../components/a11y/HiddenTitle.tsx';

interface PreviewTabProps {
  catalog: AccessCatalog;
  /** Called when a preview result has been shown — unlocks saving a ruleset that holds a deny. */
  onPreviewed: () => void;
}

/**
 * "View as…": what the model would answer for a chosen user or caving group, with the
 * server's explanation of each verdict rendered verbatim. The server answers from the rules
 * already saved: the rules tab asks for a look here before a deny is saved, to see whom the group
 * reaches today, and points back here after the save to see the deny's effect.
 */
export default function PreviewTab({ catalog, onPreviewed }: PreviewTabProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const preview = useAccessPreview();
  const [subjectKind, setSubjectKind] = useState<AccessSubjectKind>('user');
  const [subjectId, setSubjectId] = useState<string>();
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
        <SubjectPicker
          kind={subjectKind}
          subjectId={subjectId}
          // A result shown under another subject's name would read as this one's rights.
          onKindChange={(kind) => {
            setSubjectKind(kind);
            setSubjectId(undefined);
            setResult(null);
          }}
          onSubjectChange={(id) => {
            setSubjectId(id);
            setResult(null);
          }}
        />
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
              columnTitle: <HiddenTitle>{t('a11y.expandColumn')}</HiddenTitle>,
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
