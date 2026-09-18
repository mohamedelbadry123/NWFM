/** @type {import('tailwindcss').Config} */
module.exports = {
  darkMode: 'class',
  content: ['./src/**/*.{html,ts}'],
  theme: {
    extend: {
      colors: {
        // ── Clarity Blue — primary action (brand identity: "Clarity & Transparency") ──
        primary: {
          DEFAULT: '#2450DE',
          50:  '#EEF2FE',
          100: '#DCE3FD',
          200: '#B9C7FB',
          300: '#8FA6F5',
          400: '#5B78EC',
          500: '#2450DE',
          600: '#1D42C4',
          700: '#1A37A0',
          800: '#182F80',
          900: '#171A56',
        },
        // `brand` is an alias of the Clarity Blue scale so existing bg-brand-*/text-brand-*
        // classes remap to the new primary without touching every template.
        brand: {
          DEFAULT: '#2450DE',
          50:  '#EEF2FE',
          100: '#DCE3FD',
          200: '#B9C7FB',
          300: '#8FA6F5',
          400: '#5B78EC',
          500: '#2450DE',
          600: '#1D42C4',
          700: '#1A37A0',
          800: '#182F80',
          900: '#171A56',
        },
        // ── Trust Navy — text, dark surfaces ("Trust & Governance") ──
        navy: {
          DEFAULT: '#171A56',
          50:  '#EDEEF4',
          100: '#D3D5E4',
          200: '#A7ABC9',
          300: '#7B81AE',
          400: '#4F5793',
          500: '#2A3072',
          600: '#212659',
          700: '#1C204E',
          800: '#171A56',
          900: '#10133D',
        },
        // ── Assurance Teal — success / secondary accent ("Assurance & Protection") ──
        teal: {
          DEFAULT: '#2BC3AC',
          50:  '#E9FBF6',
          100: '#C9F5EA',
          200: '#97EAD8',
          300: '#5FD9C2',
          400: '#2BC3AC',
          500: '#1DA592',
          600: '#178575',
          700: '#15685D',
          800: '#12534B',
          900: '#0F433D',
        },
        // ── Intelligence Blue — accent / info ("Intelligence & Precision") ──
        accent: {
          DEFAULT: '#5A9FD8',
          50:  '#EEF5FB',
          100: '#D7E8F5',
          200: '#B4D3EC',
          300: '#8ABBE1',
          400: '#5A9FD8',
          500: '#3D84C4',
          600: '#316BA0',
          700: '#2A5680',
          800: '#254768',
          900: '#213B56',
        },
        // ── Semantic status scales (used by prv-badge / prv-status-pill / prv-alert) ──
        success: {
          DEFAULT: '#12B76A',
          50:  '#E7F8F0',
          100: '#C6EFDA',
          400: '#32D583',
          500: '#12B76A',
          600: '#039855',
          700: '#027A48',
        },
        warning: {
          DEFAULT: '#F79009',
          50:  '#FEF6E7',
          100: '#FDE9C4',
          400: '#FDB022',
          500: '#F79009',
          600: '#DC6803',
          700: '#B54708',
        },
        danger: {
          DEFAULT: '#F04438',
          50:  '#FEECEB',
          100: '#FCD4D0',
          400: '#F97066',
          500: '#F04438',
          600: '#D92D20',
          700: '#B42318',
        },
        info: {
          DEFAULT: '#5A9FD8',
          50:  '#EEF5FB',
          100: '#D7E8F5',
          400: '#5A9FD8',
          500: '#3D84C4',
          600: '#316BA0',
          700: '#2A5680',
        },
        // ── Neutral ink ramp — cool, navy-tinted; ink-900 = Trust Navy (primary text) ──
        ink: {
          DEFAULT: '#171A56',
          50:  '#F4F6FB',
          100: '#E7EBF3',
          200: '#D3D9E6',
          300: '#B0B8CB',
          400: '#7E879D',
          500: '#5A6377',
          600: '#3E465A',
          700: '#2E3550',
          800: '#20263F',
          900: '#171A56',
        },
        // ── Dark-mode surface ramp — navy-tinted, with clear luminance steps so
        //    panels/inputs/borders separate (page → card → raised → border). ──
        surface: {
          DEFAULT: '#0A0E22',
          950: '#070A18', // deepest (behind everything)
          900: '#0A0E22', // page / body background
          850: '#0F1430', // recessed (inputs, wells)
          800: '#161D40', // card / elevated panel
          700: '#212A54', // raised / hover / header
          600: '#2E3868', // visible border
          500: '#3E4A87', // strong border / muted
        },
      },
      fontFamily: {
        sans:    ['Poppins', '"DIN NEXT"', '"LT ARABIC"', 'system-ui', 'sans-serif'],
        en:      ['Poppins', '"DIN NEXT"', 'system-ui', 'sans-serif'],
        ar:      ['"LT ARABIC"', '"IBM Plex Sans Arabic"', 'system-ui', 'sans-serif'],
        display: ['Intrade', 'Poppins', 'system-ui', 'sans-serif'],
        mono:    ['"JetBrains Mono"', 'ui-monospace', 'monospace'],
      },
      letterSpacing: {
        tightish: '-0.02em',
        tighter2: '-0.035em',
      },
      boxShadow: {
        'brand':    '0 12px 28px -10px rgba(36, 80, 222, 0.45)',
        'brand-sm': '0 2px 8px -2px rgba(36, 80, 222, 0.30)',
      },
    },
  },
  plugins: [],
};
