// Keep each command below lint-staged's Windows limit (8191), including quoted
// paths. Otherwise it re-evaluates this function per chunk, repeating the tests.
const maxCommandLength = 7000;
const quality = 'node scripts/quality/check-files.mjs --write';

export default (files) => {
  const commands = [];
  let command = quality;
  for (const file of files) {
    const token = ' ' + JSON.stringify(file);
    if (quality.length + token.length > maxCommandLength)
      throw new Error('Staged path exceeds the quality command length limit: ' + file);
    // lint-staged supplies normalized paths. Refuse names its command parser
    // cannot round-trip rather than silently checking a different/missing file.
    if (/["\\\r\n]/.test(file))
      throw new Error('Staged unsupported path for the quality command parser: ' + file);
    if (command.length + token.length > maxCommandLength) {
      commands.push(command);
      command = quality;
    }
    command += token;
  }
  if (command !== quality) commands.push(command);
  // Sequential quality batches must all pass before the complete suite runs
  // once, with unstaged changes still hidden by the existing pre-commit hook.
  return [...commands, 'npm run test:unit'];
};
