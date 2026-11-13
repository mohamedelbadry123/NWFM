import { mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { spawnSync } from 'node:child_process';

// Preserve established import names with generator-produced aliases only. Never edit generated TS.
const origin = new URL(process.env.NWFM_E2E_API_URL ?? '');
const id = process.env.NWFM_E2E_RUN_ID ?? '';
if (origin.hostname !== '127.0.0.1' || origin.protocol !== 'http:' || !/^[0-9a-f-]{36}$/.test(id))
  throw new Error('Owned disposable API required');
const artifact = resolve('.work/api-preparation', id);
mkdirSync(artifact, { recursive: true });
const response = await fetch(new URL('/swagger/v1/swagger.json', origin));
if (!response.ok) throw new Error('Disposable Swagger unavailable: ' + response.status);
const raw = await response.json();
writeFileSync(join(artifact, 'swagger.raw.json'), JSON.stringify(raw, null, 2));
function walk(dir) {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const path = join(dir, entry.name);
    if (entry.isDirectory())
      return path.replaceAll('\\', '/').endsWith('/shared/models') ? [] : walk(path);
    return path.endsWith('.ts') ? [path] : [];
  });
}
const names = new Set(
  walk('frontend/src/app').flatMap((path) =>
    [
      ...readFileSync(path, 'utf8').matchAll(
        /import\s+type\s*\{\s*(PaginatedResultOf\w+)\s*\}\s*from\s*['"]@shared\/models\/models\/NWFM\/Shared\/Results\//g,
      ),
    ].map((match) => match[1]),
  ),
);
const spec = structuredClone(raw);
const prefix = 'NWFM.Shared.Results.';
const aliases = [];
for (const name of [...names].sort()) {
  const alias = prefix + name;
  if (raw.components.schemas[alias]) continue;
  const candidates = Object.keys(raw.components.schemas).filter(
    (key) =>
      key.startsWith(prefix + 'PaginatedResultOf') &&
      'PaginatedResultOf' + key.split('.').at(-1) === name,
  );
  if (candidates.length !== 1)
    throw new Error('Cannot uniquely resolve legacy schema alias ' + alias);
  const target = candidates[0];
  // OpenAPI allOf with one reference changes neither fields, nullability nor wire format.
  spec.components.schemas[alias] = {
    description: 'Compatibility name for the current qualified Swagger schema ' + target + '.',
    allOf: [{ $ref: '#/components/schemas/' + target }],
  };
  aliases.push({ alias, target });
}
const input = join(artifact, 'swagger.compat.json');
writeFileSync(input, JSON.stringify(spec, null, 2));
writeFileSync(join(artifact, 'schema-compatibility.json'), JSON.stringify(aliases, null, 2));
console.log(
  'Generating ' +
    aliases.length +
    ' source-derived legacy aliases from the disposable Swagger; no response schema changed.',
);
const generator = resolve('frontend/node_modules/ng-openapi-gen/lib/index.js');
const result = spawnSync(
  process.execPath,
  [generator, '--config', 'ng-openapi-gen.json', '--input', input],
  { cwd: resolve('frontend'), stdio: 'inherit', windowsHide: true, env: process.env },
);
if (result.error) throw result.error;
process.exitCode = result.status ?? 1;
