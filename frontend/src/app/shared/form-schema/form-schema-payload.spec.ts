import { PREVIEW_VALUE_KINDS, fromStoredAnswers, toPreviewPayload } from './form-schema-payload';

/**
 * The payload conversion is the boundary between what a control holds and what a column stores.
 * A round trip has to survive it: a repeat fill opens on the stored answers, so anything lost here
 * is an answer the respondent has to type again.
 */
describe('form schema payload', () => {
  const kinds = {
    seen_on: PREVIEW_VALUE_KINDS.Date,
    seen_at: PREVIEW_VALUE_KINDS.Time,
    started_at: PREVIEW_VALUE_KINDS.DateTime,
    is_hazard: PREVIEW_VALUE_KINDS.YesNo,
    site_point: PREVIEW_VALUE_KINDS.Geo,
    site_photos: PREVIEW_VALUE_KINDS.Files,
  } as const;

  it('writes a date as a local calendar day, not an instant', () => {
    // 5 January, read off the control in local time — a UTC conversion could move it a day.
    const payload = toPreviewPayload({ seen_on: new Date(2026, 0, 5, 23, 30) }, kinds);

    expect(payload['seen_on']).toBe('2026-01-05');
  });

  it('writes a time as local HH:mm', () => {
    const payload = toPreviewPayload({ seen_at: new Date(2026, 0, 5, 8, 30) }, kinds);

    expect(payload['seen_at']).toBe('08:30');
  });

  it('sends a yes/no answer in the builder vocabulary, for the server to coerce', () => {
    // The rules engine matches on 'yes'/'no', so that is what travels; the submission store turns it
    // into the BIT its column holds.
    expect(toPreviewPayload({ is_hazard: 'yes' }, kinds)['is_hazard']).toBe('yes');
    expect(toPreviewPayload({ is_hazard: 'no' }, kinds)['is_hazard']).toBe('no');
  });

  it('drops the blob URL from a media answer but keeps the stored reference', () => {
    const payload = toPreviewPayload(
      {
        site_photos: [
          { fileId: 'f1', path: 'pending/f1.jpg', name: 'a.jpg', type: 'image/jpeg', size: 10, url: 'blob:x' },
        ],
      },
      kinds,
    );

    const files = payload['site_photos'] as Array<Record<string, unknown>>;

    expect(files[0]['fileId']).toBe('f1');
    expect(files[0]['url']).toBeUndefined();
  });

  it('restores stored answers back onto control values', () => {
    // What the columns actually hand back: ADO timestamps, a BIT, and JSON text.
    const restored = fromStoredAnswers(
      {
        seen_on: '2026-01-05T00:00:00',
        seen_at: '08:30:00',
        is_hazard: true,
        site_photos: '[{"fileId":"f1","path":"forms/x/f1.jpg","name":"a.jpg","type":"image/jpeg","size":10}]',
      },
      kinds,
    );

    expect((restored['seen_on'] as Date).getFullYear()).toBe(2026);
    expect((restored['seen_on'] as Date).getDate()).toBe(5);
    // The time control holds text, so an ADO `08:30:00` is trimmed rather than made a Date.
    expect(restored['seen_at']).toBe('08:30');
    expect(restored['is_hazard']).toBe('yes');
    expect((restored['site_photos'] as unknown[]).length).toBe(1);
  });

  it('round-trips a date through store and back to the same calendar day', () => {
    const original = new Date(2026, 5, 30);

    const stored = toPreviewPayload({ seen_on: original }, kinds)['seen_on'] as string;
    const restored = fromStoredAnswers({ seen_on: stored }, kinds)['seen_on'] as Date;

    expect(restored.getFullYear()).toBe(2026);
    expect(restored.getMonth()).toBe(5);
    expect(restored.getDate()).toBe(30);
  });
});
