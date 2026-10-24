// SPDX-License-Identifier: AGPL-3.0-or-later
import { Empty, Flex, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { surveyModelReadableByViewer, useSurveyModels } from '../../../api/hooks/surveys.ts';
import { useWorkspaceStore } from '../../../stores/workspaceStore.ts';
import { RowButton } from '../../RowOpeners.tsx';
import List from '../../List.tsx';

/**
 * The survey-model pane with no model in it yet: the models of the cave picked on the map, any of
 * which opens here with a press, or a sentence saying how to get some.
 *
 * Asked of the cave's own model list, the same request its page makes, so a model the reader may
 * not see is not offered — and a cave whose position is protected offers its models exactly as its
 * page does, with nothing here said about where it is.
 */
export default function SurveyModelChooser({ onOpen }: { onOpen: (modelId: string) => void }) {
  const { t } = useTranslation();
  const selection = useWorkspaceStore((s) => s.selection);
  const caveId = selection?.kind === 'cave' || selection?.kind === 'entrance' ? selection.caveId : undefined;
  const { data: models, isLoading } = useSurveyModels(caveId);
  const readable = (models ?? []).filter(surveyModelReadableByViewer);

  if (caveId === undefined) {
    return (
      <Flex align="center" justify="center" style={{ height: '100%', padding: 16 }}>
        <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('workspace.surveyPickCave')} />
      </Flex>
    );
  }

  if (!isLoading && readable.length === 0) {
    return (
      <Flex align="center" justify="center" style={{ height: '100%', padding: 16 }}>
        <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('workspace.surveyNone')} />
      </Flex>
    );
  }

  return (
    <div style={{ padding: 12, overflow: 'auto', height: '100%' }} data-testid="workspace-survey-chooser">
      <Typography.Paragraph type="secondary">{t('workspace.surveyChoose')}</Typography.Paragraph>
      <List
        size="small"
        loading={isLoading}
        dataSource={readable}
        renderItem={(model) => (
          <List.Item>
            <RowButton onClick={() => onOpen(model.id)}>{model.name}</RowButton>
          </List.Item>
        )}
      />
    </div>
  );
}
