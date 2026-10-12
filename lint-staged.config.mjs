// A function receives the complete file list once. Tests run while all unstaged
// changes are hidden, so they verify the candidate commit, not other local work.
export default (files) => [
  'node scripts/quality/check-files.mjs --write ' +
    files.map((file) => JSON.stringify(file)).join(' '),
  'npm run test:unit',
];
