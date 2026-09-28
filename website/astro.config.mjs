// @ts-check
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';
import starlightLinksValidator from 'starlight-links-validator';

// Served from GitHub Pages at https://jimbuck.github.io/Ion/. Override with DOCS_SITE / DOCS_BASE for forks.
const site = process.env.DOCS_SITE ?? 'https://jimbuck.github.io';
const base = process.env.DOCS_BASE ?? '/Ion';

const section = (label, directory, collapsed = false) => ({ label, collapsed, items: [{ autogenerate: { directory } }] });

export default defineConfig({
	site,
	base,
	integrations: [
		starlight({
			title: 'Ion',
			description: 'A performant, agent-friendly 2D/3D game engine for .NET built on middleware and ECS.',
			logo: { src: './src/assets/logo.svg' },
			social: [{ icon: 'github', label: 'GitHub', href: 'https://github.com/jimbuck/Ion' }],
			editLink: { baseUrl: 'https://github.com/jimbuck/Ion/edit/main/website/' },
			lastUpdated: true,
			customCss: ['./src/styles/custom.css'],
			// Pagefind full-text search over every page (Starlight's built-in integration).
			pagefind: true,
			plugins: [starlightLinksValidator({ errorOnRelativeLinks: false })],
			sidebar: [
				section('Getting started', 'getting-started'),
				section('Core concepts', 'concepts'),
				section('Rendering', 'rendering'),
				section('ECS and scenes', 'ecs'),
				section('Audio, input and UI', 'interaction'),
				section('Physics', 'physics'),
				section('Web and networking', 'networking'),
				section('Tooling and testing', 'tooling'),
				section('Platforms and publishing', 'platforms'),
				section('Examples', 'examples'),
				section('Reference', 'reference', true),
			],
		}),
	],
});
