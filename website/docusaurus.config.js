module.exports = {
  title: 'RaiseArc',
  tagline: 'Build raising games in Unity',
  url: 'https://mhwangbo.github.io',
  baseUrl: '/RaiseArc/',
  organizationName: 'mhwangbo',
  projectName: 'RaiseArc',
  trailingSlash: false,
  onBrokenLinks: 'throw',
  markdown: { hooks: { onBrokenMarkdownLinks: 'throw' } },
  favicon: 'img/favicon.svg',
  i18n: {
    defaultLocale: 'en',
    locales: ['en', 'ko'],
    localeConfigs: { en: { label: 'English' }, ko: { label: '한국어' } },
  },
  presets: [
    ['classic', {
      docs: {
        path: '../docs',
        routeBasePath: '/',
        sidebarPath: require.resolve('./sidebars.js'),
      },
      blog: false,
      theme: { customCss: require.resolve('./src/css/custom.css') },
    }],
  ],
  themes: [[require.resolve('@easyops-cn/docusaurus-search-local'), {
    hashed: true,
    language: ['en', 'ko'],
    docsRouteBasePath: '/',
    docsDir: '../docs',
    indexBlog: false,
  }]],
  themeConfig: {
    navbar: {
      title: 'RaiseArc',
      items: [
        { type: 'docSidebar', sidebarId: 'guide', position: 'left', label: 'Docs' },
        { type: 'localeDropdown', position: 'right' },
      ],
    },
    footer: { style: 'dark', copyright: '© 2026 Mi Hwangbo' },
  },
};
