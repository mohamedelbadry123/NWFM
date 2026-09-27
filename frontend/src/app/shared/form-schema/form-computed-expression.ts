/**
 * The computed-column expression language, checked in the designer so an author sees a mistake
 * while typing rather than when saving. Mirrors `FormExpression.cs` on the server, which stays the
 * authority — it parses and evaluates; this only parses.
 *
 * ```
 * expr    := term (('+' | '-') term)*
 * term    := unary (('*' | '/') unary)*
 * unary   := ('-' | '+') unary | primary
 * primary := number | 'text' | field | function '(' [expr (',' expr)*] ')' | '(' expr ')'
 * ```
 */

export const COMPUTED_EXPRESSION_MAX_LENGTH = 500;
const MAX_TOKENS = 200;
const MAX_DEPTH = 32 * 4;

/** Functions and how many values each takes. */
export const COMPUTED_FUNCTIONS: Readonly<Record<string, readonly [number, number]>> = {
  sum: [1, Infinity],
  min: [1, Infinity],
  max: [1, Infinity],
  round: [1, 2],
  abs: [1, 1],
  coalesce: [1, Infinity],
  concat: [1, Infinity],
};

export interface ExpressionCheck {
  /** Null when the expression parses. */
  readonly error: string | null;
  /** Zero-based character the error points at. */
  readonly position: number | null;
  /** Data names the expression reads, first-seen first. */
  readonly fields: string[];
}

type TokenKind = 'number' | 'text' | 'name' | '+' | '-' | '*' | '/' | '(' | ')' | ',' | 'end';

interface Token {
  readonly kind: TokenKind;
  readonly text: string;
  readonly position: number;
}

class ExpressionError extends Error {
  constructor(readonly position: number, message: string) {
    super(message);
  }
}

function tokenize(text: string): Token[] {
  const tokens: Token[] = [];
  let i = 0;

  while (i < text.length) {
    const c = text[i];

    if (/\s/.test(c)) {
      i++;
      continue;
    }

    if (tokens.length >= MAX_TOKENS) {
      throw new ExpressionError(i, `The expression has more than ${MAX_TOKENS} parts.`);
    }

    const start = i;

    if (/[0-9]/.test(c) || (c === '.' && /[0-9]/.test(text[i + 1] ?? ''))) {
      while (i < text.length && /[0-9.]/.test(text[i])) {
        i++;
      }
      const literal = text.slice(start, i);
      if (!/^(\d+\.?\d*|\.\d+)$/.test(literal)) {
        throw new ExpressionError(start, `'${literal}' is not a number.`);
      }
      tokens.push({ kind: 'number', text: literal, position: start });
      continue;
    }

    if (c === "'" || c === '"') {
      i++;
      let value = '';
      for (;;) {
        if (i >= text.length) {
          throw new ExpressionError(start, 'A text is not closed.');
        }
        if (text[i] === c) {
          if (text[i + 1] === c) {
            value += c;
            i += 2;
            continue;
          }
          i++;
          break;
        }
        value += text[i];
        i++;
      }
      tokens.push({ kind: 'text', text: value, position: start });
      continue;
    }

    if (/[A-Za-z_]/.test(c)) {
      while (i < text.length && /[A-Za-z0-9_]/.test(text[i])) {
        i++;
      }
      tokens.push({ kind: 'name', text: text.slice(start, i), position: start });
      continue;
    }

    if ('+-*/(),'.includes(c)) {
      tokens.push({ kind: c as TokenKind, text: c, position: i });
      i++;
      continue;
    }

    throw new ExpressionError(i, `'${c}' is not allowed here.`);
  }

  tokens.push({ kind: 'end', text: '', position: text.length });
  return tokens;
}

class Parser {
  private index = 0;
  readonly fields: string[] = [];

  constructor(private readonly tokens: Token[]) {}

  private get current(): Token {
    return this.tokens[this.index];
  }

  expect(kind: TokenKind): void {
    if (this.current.kind !== kind) {
      throw this.unexpected();
    }
    this.index++;
  }

  expression(depth: number): void {
    this.guard(depth);
    this.term(depth + 1);
    while (this.current.kind === '+' || this.current.kind === '-') {
      this.index++;
      this.term(depth + 1);
    }
  }

  private term(depth: number): void {
    this.guard(depth);
    this.unary(depth + 1);
    while (this.current.kind === '*' || this.current.kind === '/') {
      this.index++;
      this.unary(depth + 1);
    }
  }

  private unary(depth: number): void {
    this.guard(depth);
    if (this.current.kind === '-' || this.current.kind === '+') {
      this.index++;
      this.unary(depth + 1);
      return;
    }
    this.primary(depth + 1);
  }

  private primary(depth: number): void {
    this.guard(depth);
    const token = this.current;

    switch (token.kind) {
      case 'number':
      case 'text':
        this.index++;
        return;
      case '(':
        this.index++;
        this.expression(depth + 1);
        this.expect(')');
        return;
      case 'name':
        if (this.tokens[this.index + 1]?.kind === '(') {
          this.call(depth + 1);
          return;
        }
        this.index++;
        if (!this.fields.some((f) => f.toLowerCase() === token.text.toLowerCase())) {
          this.fields.push(token.text);
        }
        return;
      default:
        throw this.unexpected();
    }
  }

  private call(depth: number): void {
    const name = this.current;
    const arity = COMPUTED_FUNCTIONS[name.text.toLowerCase()];
    if (!arity) {
      throw new ExpressionError(name.position, `'${name.text}' is not a known function.`);
    }

    this.index += 2;
    let count = 0;

    if (this.current.kind !== ')') {
      this.expression(depth + 1);
      count++;
      while (this.current.kind === ',') {
        this.index++;
        this.expression(depth + 1);
        count++;
      }
    }

    this.expect(')');

    if (count < arity[0] || count > arity[1]) {
      throw new ExpressionError(name.position, `'${name.text}' does not take ${count} value(s).`);
    }
  }

  private guard(depth: number): void {
    if (depth > MAX_DEPTH) {
      throw new ExpressionError(this.current.position, 'The expression nests too deep.');
    }
  }

  private unexpected(): ExpressionError {
    return this.current.kind === 'end'
      ? new ExpressionError(this.current.position, 'The expression ends too early.')
      : new ExpressionError(this.current.position, `'${this.current.text}' is not expected here.`);
  }
}

/** Parses an expression and reports the first problem, if any, and the fields it reads. */
export function checkExpression(text: string | null | undefined): ExpressionCheck {
  if (!text || !text.trim()) {
    return { error: 'The expression is empty.', position: 0, fields: [] };
  }

  if (text.length > COMPUTED_EXPRESSION_MAX_LENGTH) {
    return {
      error: `The expression is longer than ${COMPUTED_EXPRESSION_MAX_LENGTH} characters.`,
      position: COMPUTED_EXPRESSION_MAX_LENGTH,
      fields: [],
    };
  }

  try {
    const parser = new Parser(tokenize(text));
    parser.expression(0);
    parser.expect('end');
    return { error: null, position: null, fields: parser.fields };
  } catch (error) {
    if (error instanceof ExpressionError) {
      return { error: error.message, position: error.position, fields: [] };
    }
    throw error;
  }
}
