// L5 — Quét accessibility (WCAG 2.x A/AA) bằng axe-core + Playwright.
// Chạy trên các màn hình PUBLIC (không cần đăng nhập) của Tryum.
//
// Dùng:
//   npm ci && npx playwright install chromium
//   node scan.mjs
//
// Env:
//   TRYUM_WEB_URL        (mặc định https://tryum-web.nganhvan1609.workers.dev)
//   FAIL_ON_VIOLATIONS   ("true" để exit 1 khi có vi phạm critical/serious)

import { chromium } from 'playwright';
import { AxeBuilder } from '@axe-core/playwright';
import { writeFileSync, appendFileSync } from 'node:fs';

const BASE_URL = process.env.TRYUM_WEB_URL ?? 'https://tryum-web.nganhvan1609.workers.dev';
const FAIL_ON = process.env.FAIL_ON_VIOLATIONS === 'true';

const screens = [
  { path: '/', name: 'Trang chủ' },
  { path: '/auth/login', name: 'Đăng nhập' },
  { path: '/auth/register', name: 'Đăng ký' },
  { path: '/docs', name: 'Tài liệu màn hình' },
];

const viewports = [
  { name: 'desktop', width: 1280, height: 800 },
  { name: 'mobile', width: 390, height: 844 },
];

const rows = [];
let serious = 0;

const browser = await chromium.launch();
try {
  for (const vp of viewports) {
    const context = await browser.newContext({ viewport: { width: vp.width, height: vp.height } });
    for (const s of screens) {
      const page = await context.newPage();
      try {
        await page.goto(BASE_URL + s.path, { waitUntil: 'networkidle', timeout: 30000 });
        await page.waitForTimeout(1500); // chờ Angular render xong

        const results = await new AxeBuilder({ page }).analyze();
        const violations = results.violations.filter((v) =>
          ['critical', 'serious'].includes(v.impact),
        );

        if (violations.length) serious += violations.length;

        rows.push({
          screen: s.name,
          viewport: vp.name,
          url: s.path,
          violations: violations.map((v) => ({
            impact: v.impact,
            id: v.id,
            description: v.description,
            help: v.help,
            nodes: v.nodes.length,
            helpUrl: v.helpUrl,
          })),
        });
      } catch (err) {
        rows.push({ screen: s.name, viewport: vp.name, url: s.path, error: String(err) });
      } finally {
        await page.close();
      }
    }
    await context.close();
  }
} finally {
  await browser.close();
}

// ── Báo cáo Markdown ─────────────────────────────────────────────────────────
const lines = [];
lines.push('# Tryum — Accessibility scan (axe-core)');
lines.push('');
lines.push(`- URL: \`${BASE_URL}\``);
lines.push(`- Thời điểm: ${new Date().toISOString()}`);
lines.push(`- Màn hình: ${screens.length} × viewport: ${viewports.length}`);
lines.push('');
lines.push('| Màn hình | Viewport | Vi phạm (critical/serious) |');
lines.push('|---|---|---|');
for (const r of rows) {
  const count = r.error ? '⚠ lỗi' : r.violations.length;
  lines.push(`| ${r.screen} | ${r.viewport} | ${count} |`);
}
lines.push('');

if (serious === 0) {
  lines.push('## ✅ Không có vi phạm critical/serious');
} else {
  lines.push('## Chi tiết vi phạm');
  lines.push('');
  for (const r of rows) {
    if (r.error) {
      lines.push(`### ${r.screen} (${r.viewport}) — LỖI`);
      lines.push('');
      lines.push('```');
      lines.push(r.error);
      lines.push('```');
      lines.push('');
      continue;
    }
    for (const v of r.violations) {
      lines.push(`### [${r.screen} · ${r.viewport}] ${v.impact.toUpperCase()} — ${v.id}`);
      lines.push('');
      lines.push(`- ${v.help} (${v.description})`);
      lines.push(`- Ảnh hưởng ${v.nodes} node`);
      lines.push(`- Chi tiết: ${v.helpUrl}`);
      lines.push('');
    }
  }
}

const md = lines.join('\n');
writeFileSync('a11y-report.md', md, 'utf8');

if (process.env.GITHUB_STEP_SUMMARY) {
  appendFileSync(process.env.GITHUB_STEP_SUMMARY, md + '\n', 'utf8');
}

console.log(md);
console.log(`\nTổng vi phạm critical/serious: ${serious}`);

if (FAIL_ON && serious > 0) {
  console.error(`\nFAIL: có ${serious} vi phạm critical/serious.`);
  process.exit(1);
}
