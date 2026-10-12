import js from '@eslint/js';
import ts from 'typescript-eslint';
import angular from 'angular-eslint';
import globals from 'globals';

export default ts.config(
  {
    ignores: [
      '**/node_modules/**',
      '**/dist/**',
      '**/bin/**',
      '**/obj/**',
      '.work/**',
      '.e2e/**',
      'frontend/src/app/shared/models/**',
      'frontend/src/app/shared/services/**',
    ],
  },
  {
    ...js.configs.recommended,
    files: ['**/*.{js,mjs,cjs}'],
    languageOptions: { globals: { ...globals.node, ...globals.browser } },
  },
  {
    files: ['**/*.ts'],
    extends: [...ts.configs.recommended],
    languageOptions: { globals: { ...globals.node, ...globals.browser, ...globals.jasmine } },
  },
  {
    files: ['frontend/**/*.ts'],
    extends: [...angular.configs.tsRecommended],
    processor: angular.processInlineTemplates,
  },
  { files: ['frontend/**/*.html'], extends: [...angular.configs.templateRecommended] },
);
