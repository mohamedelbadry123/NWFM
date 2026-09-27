import { toLocalDateTime, toUtcDateTime } from './workflow-date.util';

describe('Workflow due dates', () => {
  it('converts a local wall time to its actual UTC instant', () => {
    const local = '2026-09-19T15:30:45';
    const expected = new Date(2026, 8, 19, 15, 30, 45).toISOString();
    expect(toUtcDateTime(local)).toBe(expected);
  });

  it('round trips a saved date through the local date input without changing the instant', () => {
    const instant = '2026-09-19T12:30:45.000Z';
    expect(toUtcDateTime(toLocalDateTime(instant))).toBe(instant);
  });

  it('honors a supplied timezone offset', () => {
    expect(toUtcDateTime('2026-09-19T15:30:00+03:00')).toBe('2026-09-19T12:30:00.000Z');
  });

  it('preserves fractional seconds when reopening and saving', () => {
    const instant = '2026-09-19T12:30:45.123Z';
    expect(toUtcDateTime(toLocalDateTime(instant))).toBe(instant);
  });

  it('leaves invalid dates available for validation instead of throwing on save', () => {
    expect(toUtcDateTime('invalid')).toBe('invalid');
    expect(toLocalDateTime('invalid')).toBe('invalid');
  });
});
