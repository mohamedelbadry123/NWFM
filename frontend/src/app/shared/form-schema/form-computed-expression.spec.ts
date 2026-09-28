import { checkExpression } from './form-computed-expression';

describe('checkExpression', () => {
  it('accepts arithmetic, text, fields and functions, and lists the fields read', () => {
    const check = checkExpression("round(sum(width, height) / 2, 1) + abs(-depth)");

    expect(check.error).toBeNull();
    expect(check.fields).toEqual(['width', 'height', 'depth']);
    expect(checkExpression("concat(name, ' - ', 'it''s')").error).toBeNull();
  });

  it('treats a name followed by a parenthesis as a function, otherwise a field', () => {
    expect(checkExpression('sum(sum, 1)').fields).toEqual(['sum']);
  });

  it('says where the expression goes wrong', () => {
    expect(checkExpression('1 +').position).toBe(3);
    expect(checkExpression('(1 + 2').position).toBe(6);
    expect(checkExpression('1 $ 2').position).toBe(2);
    expect(checkExpression("'open").position).toBe(0);
    expect(checkExpression('nope(1)').error).toContain('not a known function');
    expect(checkExpression('abs(1, 2)').error).toContain('does not take 2');
    expect(checkExpression('   ').error).toContain('empty');
  });
});
