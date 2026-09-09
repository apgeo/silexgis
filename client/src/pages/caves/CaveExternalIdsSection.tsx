// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { App, Button, Card, Descriptions, Empty, Input, List, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useCaveExternalIds,
  useGrottocenterLookup,
  useSetCaveExternalId,
  type GrottocenterLookup,
} from '../../api/hooks.ts';

/** The registers this application records an identifier for, in the order they are shown. */
const systems = ['grottocenter', 'national_cadastre'] as const;

/**
 * The numbers other registers know this cave by.
 *
 * The point of keeping them is that they are the only stable way to say "this cave and that one
 * are the same cave" to somebody outside this installation — a name is not, and a position is
 * exactly what may not be shared. So the identifier is stored and nothing else that comes back
 * from a register ever is: this application is not a mirror of somebody else's data, and a copy
 * taken at an unknown moment goes quietly stale while still looking authoritative.
 *
 * The lookup button appears only where the installation has turned the integration on, which it
 * is not by default. The server answers "not configured" rather than failing, so what happens
 * here on an installation that never opted in is a sentence saying so — not a button that always
 * breaks, and not a request that leaves.
 */
export default function CaveExternalIdsSection({
  caveId,
  canEdit,
}: {
  caveId: string;
  canEdit: boolean;
}) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data: ids } = useCaveExternalIds(caveId);
  const setId = useSetCaveExternalId(caveId);
  const lookup = useGrottocenterLookup(caveId);
  const [result, setResult] = useState<GrottocenterLookup | null>(null);

  const valueOf = (system: string) => ids?.find((x) => x.system === system)?.value ?? '';

  const save = (system: string, value: string) => {
    const trimmed = value.trim();
    if (trimmed === valueOf(system)) {
      return;
    }

    setId.mutate(
      { system, value: trimmed === '' ? null : trimmed },
      { onError: () => message.error(t('common.saveFailed')) },
    );
  };

  const onLookup = () => {
    lookup.mutate(undefined, {
      onSuccess: (data) => setResult(data),
      onError: () => message.error(t('externalIds.lookupFailed')),
    });
  };

  return (
    <Card title={t('externalIds.title')} style={{ marginTop: 16 }} data-testid="cave-external-ids">
      <Typography.Paragraph type="secondary" style={{ fontSize: 12 }}>
        {t('externalIds.explanation')}
      </Typography.Paragraph>

      <Descriptions column={1} size="small" bordered>
        {systems.map((system) => (
          <Descriptions.Item key={system} label={t(`externalIds.systems.${system}`)}>
            {canEdit ? (
              <Input
                data-testid={`external-id-${system}`}
                defaultValue={valueOf(system)}
                key={valueOf(system)}
                allowClear
                placeholder={t('externalIds.placeholder')}
                onBlur={(e) => save(system, e.target.value)}
                onPressEnter={(e) => save(system, e.currentTarget.value)}
              />
            ) : (
              (valueOf(system) || '—')
            )}
          </Descriptions.Item>
        ))}
      </Descriptions>

      {canEdit && (
        <div style={{ marginTop: 12 }}>
          <Button
            data-testid="external-id-lookup"
            loading={lookup.isPending}
            onClick={onLookup}
            style={{ marginBottom: 8 }}
          >
            {t('externalIds.lookup')}
          </Button>

          {result !== null && !result.configured && (
            <Typography.Text type="secondary">{t('externalIds.notConfigured')}</Typography.Text>
          )}

          {result?.configured && result.candidates.length === 0 && (
            <Empty
              image={Empty.PRESENTED_IMAGE_SIMPLE}
              description={t('externalIds.noCandidates')}
            />
          )}

          {result?.configured && result.candidates.length > 0 && (
            <List
              size="small"
              data-testid="external-id-candidates"
              dataSource={result.candidates}
              renderItem={(candidate) => (
                <List.Item
                  actions={[
                    // Accepted by a person, one at a time. A name match is a guess, and a
                    // guess recorded automatically is an identity nobody checked.
                    <Button
                      key="accept"
                      size="small"
                      type="link"
                      onClick={() => save('grottocenter', candidate.externalId)}
                    >
                      {t('externalIds.accept')}
                    </Button>,
                  ]}
                >
                  <List.Item.Meta
                    title={
                      candidate.url ? (
                        <a href={candidate.url} target="_blank" rel="noreferrer">
                          {candidate.name ?? candidate.externalId}
                        </a>
                      ) : (
                        (candidate.name ?? candidate.externalId)
                      )
                    }
                    description={[candidate.externalId, candidate.country]
                      .filter(Boolean)
                      .join(' · ')}
                  />
                </List.Item>
              )}
            />
          )}
        </div>
      )}
    </Card>
  );
}
