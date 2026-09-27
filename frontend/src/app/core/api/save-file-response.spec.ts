import { fileNameFromDisposition } from './save-file-response';

describe('fileNameFromDisposition', () => {
  it('reads a quoted file name', () => {
    expect(fileNameFromDisposition('attachment; filename="Task_TSK-1_20260921.pdf"', 'x.pdf')).toBe('Task_TSK-1_20260921.pdf');
  });

  it('stops at the semicolon rather than swallowing the UTF-8 copy', () => {
    const header = "attachment; filename=Task_TSK-1.pdf; filename*=UTF-8''Task_TSK-1.pdf";

    expect(fileNameFromDisposition(header, 'x.pdf')).toBe('Task_TSK-1.pdf');
  });

  it('decodes an encoded name', () => {
    expect(fileNameFromDisposition("attachment; filename*=UTF-8''%D9%85%D9%87%D9%85%D8%A9.pdf", 'x.pdf')).toBe('مهمة.pdf');
  });

  it('falls back when the header is missing', () => {
    expect(fileNameFromDisposition(null, 'Task_TSK-1.pdf')).toBe('Task_TSK-1.pdf');
  });
});
