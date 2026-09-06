// SPDX-License-Identifier: AGPL-3.0-or-later
import { Card, Empty, List, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { TripImportProblem } from '../../api/hooks.ts';

interface Props {
  problems: readonly TripImportProblem[];
  /** Rows the reader could not read at all, which is what this list is the whole account of. */
  failedRowCount: number;
}

/**
 * Everything the reader could not do, by physical file line.
 *
 * A row carrying an error never reaches the table of rows, so this list is the only place it
 * appears — which is why it is beside the table rather than folded away behind a count, and why
 * it says so plainly when there is nothing wrong instead of rendering as an empty box that could
 * equally mean the question was never asked.
 *
 * Line 0 means the problem is about the file rather than about any one row.
 */
export default function TripImportProblemList({ problems, failedRowCount }: Props) {
  const { t } = useTranslation();

  return (
    <Card
      size="small"
      title={t('tripImport.problemsTitle', { count: failedRowCount })}
      data-testid="trip-import-problems"
    >
      {problems.length === 0 ? (
        <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('tripImport.noProblems')} />
      ) : (
        <List
          size="small"
          dataSource={[...problems].slice(0, 200)}
          renderItem={(problem) => (
            <List.Item>
              <List.Item.Meta
                title={
                  <span>
                    <Tag color={problem.severity === 'error' ? 'red' : 'orange'}>
                      {problem.line === 0
                        ? t('tripImport.wholeFile')
                        : t('tripImport.atLine', { line: problem.line })}
                    </Tag>
                    {t(`tripImport.problems.${problem.code}`)}
                  </span>
                }
                description={
                  <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                    {[problem.column, problem.detail].filter(Boolean).join(' — ')}
                  </Typography.Text>
                }
              />
            </List.Item>
          )}
        />
      )}
    </Card>
  );
}
