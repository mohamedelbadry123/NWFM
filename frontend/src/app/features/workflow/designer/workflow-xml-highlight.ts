export type XmlTokenKind = 'text' | 'tag' | 'attr' | 'value' | 'punct' | 'comment';

export interface XmlToken {
  kind: XmlTokenKind;
  text: string;
}

export interface XmlHighlightLine {
  number: number;
  tokens: XmlToken[];
}

/** Line-oriented highlighter for pretty-printed NWFM workflow XML. */
export function highlightXml(xml: string): XmlHighlightLine[] {
  if (!xml) return [];
  return xml.replace(/\r\n/g, '\n').split('\n').map((line, i) => ({
    number: i + 1,
    tokens: tokenizeXmlLine(line),
  }));
}

function tokenizeXmlLine(line: string): XmlToken[] {
  const tokens: XmlToken[] = [];
  let i = 0;

  while (i < line.length) {
    if (line.startsWith('<!--', i)) {
      const end = line.indexOf('-->', i);
      const close = end === -1 ? line.length : end + 3;
      tokens.push({ kind: 'comment', text: line.slice(i, close) });
      i = close;
      continue;
    }

    if (line[i] === '<') {
      const isDecl = line.startsWith('<?', i);
      const closeSeq = isDecl ? '?>' : '>';
      const end = line.indexOf(closeSeq, i);
      const close = end === -1 ? line.length : end + closeSeq.length;
      tokens.push(...tokenizeTag(line.slice(i, close)));
      i = close;
      continue;
    }

    const next = line.indexOf('<', i);
    const end = next === -1 ? line.length : next;
    tokens.push({ kind: 'text', text: line.slice(i, end) });
    i = end;
  }

  return tokens.filter(t => t.text.length > 0);
}

function tokenizeTag(tag: string): XmlToken[] {
  const tokens: XmlToken[] = [];
  let i = 0;

  const pushPunct = (n: number): void => {
    tokens.push({ kind: 'punct', text: tag.slice(i, i + n) });
    i += n;
  };

  if (tag.startsWith('<?')) pushPunct(2);
  else if (tag.startsWith('</')) pushPunct(2);
  else if (tag.startsWith('<')) pushPunct(1);

  const nameStart = i;
  while (i < tag.length && /[\w:.-]/.test(tag[i]!)) i++;
  if (i > nameStart) tokens.push({ kind: 'tag', text: tag.slice(nameStart, i) });

  while (i < tag.length) {
    const ch = tag[i]!;
    if (/\s/.test(ch)) {
      const start = i;
      while (i < tag.length && /\s/.test(tag[i]!)) i++;
      tokens.push({ kind: 'text', text: tag.slice(start, i) });
      continue;
    }
    if (tag.startsWith('?>', i) || tag.startsWith('/>', i)) {
      pushPunct(2);
      continue;
    }
    if (ch === '>' || ch === '=') {
      pushPunct(1);
      continue;
    }
    if (ch === '"' || ch === "'") {
      let j = i + 1;
      while (j < tag.length && tag[j] !== ch) j++;
      if (j < tag.length) j++;
      tokens.push({ kind: 'value', text: tag.slice(i, j) });
      i = j;
      continue;
    }

    const start = i;
    while (i < tag.length && /[\w:.-]/.test(tag[i]!)) i++;
    if (i > start) {
      tokens.push({ kind: 'attr', text: tag.slice(start, i) });
    } else {
      tokens.push({ kind: 'text', text: ch });
      i++;
    }
  }

  return tokens;
}
