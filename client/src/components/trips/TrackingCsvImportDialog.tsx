// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { DownloadOutlined } from '@ant-design/icons';
import {
  Alert,
  Button,
  Checkbox,
  Collapse,
  Descriptions,
  Flex,
  Form,
  Input,
  Modal,
  Select,
  Statistic,
  Table,
  Tag,
  Typography,
  Upload,
  message,
} from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useTrackingCsvCommit,
  useTrackingCsvFields,
  useTrackingCsvPreview,
  type TrackingCsvDiagnostic,
  type TrackingCsvOptions,
  type TrackingCsvPreview,
  type TrackingCsvPreviewRow,
} from '../../api/hooks.ts';
import { trackingProblemMessage } from './trackingProblems.ts';

interface Props {
  tripLogId: string;
  open: boolean;
  onClose: () => void;
}

/** Which of a row's fields a hand-written mapping can point at a header. */
type FieldMapping = Record<string, string>;

/**
 * Reading a coordinator's spreadsheet of reports onto the trip's log.
 *
 * <b>Two steps, and the first one writes nothing.</b> What a sheet means depends on choices the
 * reviewer makes — which header is the moment, whether a numeric date is day-first, which words
 * this club writes "went in" in — so the sheet is read again under the current choices every time,
 * and what comes back is a page of exactly what committing would do. A sheet somebody opened and
 * thought better of leaves the log as it was.
 *
 * <b>Overwriting is asked for, not defaulted.</b> The key a report is filed under is the person and
 * the instant, which is what makes re-importing a corrected sheet safe: the second run changes the
 * rows it corrected instead of doubling them. It is also the one act that silently rewrites
 * history, so the box is unticked and the reviewer ticks it.
 *
 * The sample sheet is offered rather than described. The fastest way to explain a column layout is
 * a file somebody can open in the spreadsheet they already use, and it is generated from the same
 * spellings the reader detects — so a template this screen offers is one this server can read.
 */
export default function TrackingCsvImportDialog({ tripLogId, open, onClose }: Props) {
  const { t } = useTranslation();
  const [text, setText] = useState('');
  const [fileName, setFileName] = useState<string | null>(null);
  const [replaceExisting, setReplaceExisting] = useState(false);
  const [mapping, setMapping] = useState<FieldMapping>({});
  const [wentIn, setWentIn] = useState('');
  const [cameOut, setCameOut] = useState('');
  const [preview, setPreview] = useState<TrackingCsvPreview | null>(null);

  const fields = useTrackingCsvFields();
  const read = useTrackingCsvPreview();
  const commit = useTrackingCsvCommit();

  const options: TrackingCsvOptions = useMemo(() => {
    const columns = Object.fromEntries(
      Object.entries(mapping).filter(([, header]) => header.trim().length > 0),
    );
    const words = (value: string) =>
      value
        .split(',')
        .map((word) => word.trim())
        .filter((word) => word.length > 0);

    return {
      // Left out rather than sent empty: the reader treats a named field as "look for exactly this
      // and say so if it is missing", so naming a field with nothing would report every column of
      // the file as absent.
      columns: Object.keys(columns).length > 0 ? columns : null,
      delimiter: null,
      multiValueSeparators: null,
      dateOrder: null,
      wentInWords: words(wentIn).length > 0 ? words(wentIn) : null,
      cameOutWords: words(cameOut).length > 0 ? words(cameOut) : null,
    };
  }, [mapping, wentIn, cameOut]);

  const close = () => {
    setText('');
    setFileName(null);
    setPreview(null);
    setReplaceExisting(false);
    setMapping({});
    setWentIn('');
    setCameOut('');
    onClose();
  };

  const look = async () => {
    try {
      setPreview(await read.mutateAsync({ tripLogId, text, options }));
    } catch (error) {
      message.error(trackingProblemMessage(t, error));
    }
  };

  const send = async () => {
    try {
      const done = await commit.mutateAsync({ tripLogId, text, options, replaceExisting });
      message.success(
        t('trips.tracking.csvImport.done', {
          created: done.created,
          updated: done.updated,
          skipped: done.skipped,
        }),
      );
      close();
    } catch (error) {
      message.error(trackingProblemMessage(t, error));
    }
  };

  /**
   * Reading the chosen file here rather than uploading it.
   *
   * A tracking sheet is read, reviewed and committed in one sitting, so there is no review to
   * resume and nothing to be gained by storing the file — and the reviewer can change what its
   * columns mean, which a stored parse would be wrong about the moment they did.
   */
  const takeFile = async (file: File) => {
    setText(await file.text());
    setFileName(file.name);
    setPreview(null);
    return false;
  };

  const rows = preview?.rows ?? [];
  const problems = [...(preview?.fileDiagnostics ?? []), ...(preview?.refused ?? [])];

  return (
    <Modal
      open={open}
      onCancel={close}
      width={900}
      title={t('trips.tracking.csvImport.title')}
      data-testid="trip-tracking-csv-import"
      footer={
        <Flex justify="space-between" align="center" gap={12} wrap>
          <Button
            icon={<DownloadOutlined />}
            href="/api/v1/tracking-csv-import/template"
            download="tracking-sample.csv"
            data-testid="trip-tracking-csv-template"
          >
            {t('trips.tracking.csvImport.template')}
          </Button>
          <Flex gap={8}>
            <Button onClick={close}>{t('common.cancel')}</Button>
            <Button
              onClick={look}
              loading={read.isPending}
              disabled={text.trim().length === 0}
              data-testid="trip-tracking-csv-preview"
            >
              {t('trips.tracking.csvImport.look')}
            </Button>
            <Button
              type="primary"
              onClick={send}
              loading={commit.isPending}
              disabled={!preview || rows.length === 0}
              data-testid="trip-tracking-csv-commit"
            >
              {t('trips.tracking.csvImport.commit')}
            </Button>
          </Flex>
        </Flex>
      }
    >
      <Typography.Paragraph type="secondary">
        {t('trips.tracking.csvImport.help')}
      </Typography.Paragraph>

      <Upload.Dragger
        accept=".csv,text/csv"
        maxCount={1}
        beforeUpload={takeFile}
        showUploadList={false}
        data-testid="trip-tracking-csv-file"
      >
        <Typography.Text>
          {fileName ?? t('trips.tracking.csvImport.choose')}
        </Typography.Text>
      </Upload.Dragger>

      <Collapse
        ghost
        style={{ marginTop: 8 }}
        items={[
          {
            key: 'columns',
            label: t('trips.tracking.csvImport.columns'),
            children: (
              <>
                <Typography.Paragraph type="secondary">
                  {t('trips.tracking.csvImport.columnsHelp')}
                </Typography.Paragraph>
                <Form layout="vertical" size="small">
                  <Flex gap={12} wrap>
                    {(fields.data ?? []).map((field) => (
                      <Form.Item
                        key={field.field}
                        label={t(`trips.tracking.csvImport.fields.${field.field}`)}
                        style={{ width: 200 }}
                      >
                        <Select
                          allowClear
                          showSearch
                          value={mapping[field.field] || undefined}
                          placeholder={t('trips.tracking.csvImport.detected')}
                          onChange={(header: string | undefined) =>
                            setMapping((current) => ({ ...current, [field.field]: header ?? '' }))
                          }
                          options={(preview?.header ?? []).map((header) => ({
                            value: header,
                            label: header,
                          }))}
                        />
                      </Form.Item>
                    ))}
                  </Flex>
                  <Flex gap={12} wrap>
                    <Form.Item
                      label={t('trips.tracking.csvImport.wentInWords')}
                      style={{ width: 300 }}
                    >
                      <Input
                        value={wentIn}
                        onChange={(event) => setWentIn(event.target.value)}
                        placeholder="intrare, intrat"
                      />
                    </Form.Item>
                    <Form.Item
                      label={t('trips.tracking.csvImport.cameOutWords')}
                      style={{ width: 300 }}
                    >
                      <Input
                        value={cameOut}
                        onChange={(event) => setCameOut(event.target.value)}
                        placeholder="iesire, iesit"
                      />
                    </Form.Item>
                  </Flex>
                </Form>
              </>
            ),
          },
        ]}
      />

      {preview && (
        <>
          <Flex gap={24} wrap style={{ marginTop: 8, marginBottom: 12 }}>
            <Statistic
              title={t('trips.tracking.csvImport.rowsRead')}
              value={preview.rowsRead}
            />
            <Statistic
              title={t('trips.tracking.csvImport.creates')}
              value={preview.creates}
            />
            <Statistic
              title={t('trips.tracking.csvImport.replaces')}
              value={preview.replaces}
            />
          </Flex>

          {preview.unmatchedCavers.length > 0 && (
            // Said once however many rows wrote the name, because a sheet naming somebody who is
            // not on the trip is a missing participant — one thing to go and fix — and a refusal
            // per row buries that under its own repetitions.
            <Alert
              type="warning"
              showIcon
              style={{ marginBottom: 12 }}
              data-testid="trip-tracking-csv-unmatched"
              message={t('trips.tracking.csvImport.unmatchedTitle')}
              description={
                <>
                  <div>{t('trips.tracking.csvImport.unmatchedBody')}</div>
                  <Flex gap={4} wrap style={{ marginTop: 8 }}>
                    {preview.unmatchedCavers.map((name) => (
                      <Tag key={name}>{name}</Tag>
                    ))}
                  </Flex>
                </>
              }
            />
          )}

          {preview.unmappedColumns.length > 0 && (
            <Alert
              type="info"
              showIcon
              style={{ marginBottom: 12 }}
              message={t('trips.tracking.csvImport.unmappedTitle')}
              description={preview.unmappedColumns.join(', ')}
            />
          )}

          {preview.replaces > 0 && (
            <Checkbox
              checked={replaceExisting}
              onChange={(event) => setReplaceExisting(event.target.checked)}
              style={{ marginBottom: 12 }}
              data-testid="trip-tracking-csv-replace"
            >
              {t('trips.tracking.csvImport.replaceExisting', { count: preview.replaces })}
            </Checkbox>
          )}

          <Table<TrackingCsvPreviewRow>
            size="small"
            rowKey={(row) => `${row.line}-${row.caverId}`}
            dataSource={rows}
            pagination={{ pageSize: 10, size: 'small' }}
            data-testid="trip-tracking-csv-rows"
            columns={[
              { title: t('trips.tracking.csvImport.line'), dataIndex: 'line', width: 60 },
              {
                title: t('trips.tracking.csvImport.moment'),
                dataIndex: 'recordedAt',
                render: (at: string) => new Date(at).toLocaleString(),
              },
              {
                title: t('trips.tracking.csvImport.caver'),
                key: 'caver',
                render: (_: unknown, row: TrackingCsvPreviewRow) => (
                  <Flex vertical>
                    <span>{row.caverMatched}</span>
                    {/* The written name beside the matched one wherever they differ, because that
                        is the whole of what a reviewer is checking: "Ion" matching one person is
                        right until the day two people on a trip answer to it. */}
                    {row.caverWritten !== row.caverMatched && (
                      <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                        {t('trips.tracking.csvImport.writtenAs', { written: row.caverWritten })}
                      </Typography.Text>
                    )}
                  </Flex>
                ),
              },
              {
                title: t('trips.tracking.csvImport.place'),
                key: 'place',
                render: (_: unknown, row: TrackingCsvPreviewRow) =>
                  row.stationName ??
                  (row.depthM !== null && row.depthM !== undefined
                    ? t('caves.depthPlaces.metres', { depth: row.depthM })
                    : t(`trips.tracking.kinds.${row.kind}`)),
              },
              {
                title: t('trips.tracking.csvImport.outcome'),
                key: 'outcome',
                width: 120,
                render: (_: unknown, row: TrackingCsvPreviewRow) =>
                  row.replaces ? (
                    <Tag color="orange">{t('trips.tracking.csvImport.willReplace')}</Tag>
                  ) : (
                    <Tag color="green">{t('trips.tracking.csvImport.willCreate')}</Tag>
                  ),
              },
            ]}
          />

          {problems.length > 0 && (
            <Collapse
              ghost
              style={{ marginTop: 8 }}
              items={[
                {
                  key: 'problems',
                  label: t('trips.tracking.csvImport.problems', { count: problems.length }),
                  children: (
                    <Descriptions
                      size="small"
                      column={1}
                      bordered
                      data-testid="trip-tracking-csv-problems"
                      items={problems.map((problem: TrackingCsvDiagnostic, index) => ({
                        key: `${problem.line}-${problem.problem}-${index}`,
                        label: t('trips.tracking.csvImport.atLine', { line: problem.line }),
                        children: (
                          <>
                            {t(`trips.tracking.csvImport.problemNames.${problem.problem}`, {
                              defaultValue: problem.problem,
                            })}
                            {problem.detail ? ` — ${problem.detail}` : ''}
                          </>
                        ),
                      }))}
                    />
                  ),
                },
              ]}
            />
          )}
        </>
      )}
    </Modal>
  );
}
