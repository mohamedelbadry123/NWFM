import { highlightXml } from './workflow-xml-highlight';

describe('highlightXml', () => {
  it('returns no lines for empty input', () => {
    expect(highlightXml('')).toEqual([]);
  });

  it('tokenizes tags, attributes, and values', () => {
    const [line] = highlightXml('<Activity nodeKey="start" type="Start" />');
    const kinds = line.tokens.map(t => `${t.kind}:${t.text}`);
    expect(kinds).toContain('tag:Activity');
    expect(kinds).toContain('attr:nodeKey');
    expect(kinds).toContain('value:"start"');
    expect(kinds).toContain('attr:type');
    expect(kinds).toContain('value:"Start"');
  });

  it('keeps comments distinct from tags', () => {
    const [line] = highlightXml('<!-- compiled -->');
    expect(line.tokens).toEqual([{ kind: 'comment', text: '<!-- compiled -->' }]);
  });

  it('numbers each pretty-printed line', () => {
    const xml = '<WorkflowDefinition>\n  <Activities />\n</WorkflowDefinition>';
    const lines = highlightXml(xml);
    expect(lines.map(l => l.number)).toEqual([1, 2, 3]);
    expect(lines[1].tokens.some(t => t.kind === 'tag' && t.text === 'Activities')).toBe(true);
  });
});
