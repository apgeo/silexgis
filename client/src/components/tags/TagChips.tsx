// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { PlusOutlined } from '@ant-design/icons';
import { App, AutoComplete, Tag as AntTag } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useCreateTagging,
  useDeleteTagging,
  useTaggings,
  useTags,
  type AttachedEntityType,
} from '../../api/hooks.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';

interface TagChipsProps {
  entityType: AttachedEntityType;
  entityId: string;
  canEdit: boolean;
}

/** Tag chips for one entity: existing tags (removable) + inline add with suggestions. */
export default function TagChips({ entityType, entityId, canEdit }: TagChipsProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data: taggings } = useTaggings(entityType, entityId);
  const [adding, setAdding] = useState(false);
  const [input, setInput] = useState('');
  const debounced = useDebouncedValue(input);
  const { data: suggestions } = useTags(debounced);
  const createTagging = useCreateTagging();
  const deleteTagging = useDeleteTagging();

  const submit = async (name: string) => {
    const trimmed = name.trim();
    setAdding(false);
    setInput('');
    if (!trimmed) {
      return;
    }
    try {
      await createTagging.mutateAsync({ tagName: trimmed, entityType, entityId });
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  return (
    <span style={{ display: 'inline-flex', flexWrap: 'wrap', gap: 4, alignItems: 'center' }}>
      {(taggings ?? []).map((tagging) => (
        <AntTag
          key={tagging.id}
          closable={canEdit}
          onClose={(e) => {
            e.preventDefault();
            deleteTagging.mutateAsync(tagging.id).catch(() => message.error(t('common.saveFailed')));
          }}
        >
          {tagging.tag.name}
        </AntTag>
      ))}
      {canEdit && !adding && (
        <AntTag style={{ borderStyle: 'dashed', cursor: 'pointer' }} onClick={() => setAdding(true)}>
          <PlusOutlined /> {t('tags.add')}
        </AntTag>
      )}
      {canEdit && adding && (
        <AutoComplete
          autoFocus
          size="small"
          style={{ width: 160 }}
          value={input}
          options={(suggestions ?? []).map((tag) => ({ value: tag.name }))}
          onChange={setInput}
          onSelect={(value: string) => void submit(value)}
          onBlur={() => void submit(input)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') {
              void submit(input);
            }
            if (e.key === 'Escape') {
              setAdding(false);
              setInput('');
            }
          }}
        />
      )}
    </span>
  );
}
