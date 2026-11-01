// Tests run while all unstaged changes are hidden, so they verify the candidate commit,
// not other local work. No task takes file arguments: lint-staged repeats a function's
// tasks per chunk of files once a command is too long, which would rerun the whole suite.
export default () => ['node scripts/quality/check-files.mjs --write --staged', 'npm run test:unit'];
