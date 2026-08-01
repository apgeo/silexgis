// SPDX-License-Identifier: AGPL-3.0-or-later
import { Form } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import '../../i18n';
import FieldVisibilityToggle from './FieldVisibilityToggle.tsx';

afterEach(cleanup);

function renderInForm(onValues: (values: unknown) => void) {
  return render(
    <Form
      initialValues={{ visibility: { phone: 'private' } }}
      onValuesChange={(_, all) => onValues(all)}
    >
      <Form.Item name={['visibility', 'phone']} noStyle>
        <FieldVisibilityToggle />
      </Form.Item>
    </Form>,
  );
}

describe('FieldVisibilityToggle', () => {
  it('offers exactly the three audiences the server accepts', () => {
    renderInForm(() => {});

    fireEvent.click(screen.getByLabelText('Who can see this'));

    // There is deliberately no "anyone on the internet": personal data is never anonymous-
    // readable, so offering the option would be a promise the server refuses to keep.
    expect(screen.getByText('Only me')).toBeInTheDocument();
    expect(screen.getByText('My caving groups')).toBeInTheDocument();
    expect(screen.getByText('Signed-in members')).toBeInTheDocument();
    expect(screen.queryByText(/anyone|public/i)).toBeNull();
  });

  it('writes the choice into the form that owns the field', () => {
    let latest: unknown = null;
    renderInForm((values) => {
      latest = values;
    });

    fireEvent.click(screen.getByLabelText('Who can see this'));
    fireEvent.click(screen.getByText('My caving groups'));

    // Riding in the same form is what makes privacy save with the field it governs, rather
    // than being a second thing to remember to save.
    expect(latest).toEqual({ visibility: { phone: 'cavingGroup' } });
  });
});
