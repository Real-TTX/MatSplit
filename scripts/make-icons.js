// Renders the PWA icons and the iOS launch images of MatSplit from the flat logo (accent tile + split disc).
//
//   cd scripts && npm install playwright-core   (a browser: the installed Edge or Chrome)
//   node make-icons.js
//
// Writes src/MatSplit.Web/wwwroot/img/{icon-192,icon-512,icon-maskable-192,icon-maskable-512,apple-touch-icon,favicon-32}.png and
// img/splash/{light,dark}-<w>x<h>.png, and src/MatSplit.Web/Pages/Shared/_SplashLinks.cshtml (the apple-touch-startup-image links,
// one per device size, light and dark; the media queries must match device-width, device-height, pixel ratio, orientation and scheme).
// The logo also lives in wwwroot/img/favicon.svg and in the symbol ms-i-logo of Pages/Shared/_AppIcons.cshtml: change all three together.
const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright-core');

const img = path.join(__dirname, '..', 'src', 'MatSplit.Web', 'wwwroot', 'img');
const partial = path.join(__dirname, '..', 'src', 'MatSplit.Web', 'Pages', 'Shared', '_SplashLinks.cshtml');
const ACCENT = '#bf5610'; // [data-app="matsplit"] in MatUi
const GLYPH = (scale) => `<g transform="translate(32 32) scale(${scale}) translate(-32 -32)"><path d="M29 15A17 17 0 0 0 29 49Z" fill="#fff"/><path d="M35 15A17 17 0 0 1 35 49Z" fill="#fff"/></g>`;

// a tile of the logo: rounded (purpose any) or full bleed (maskable, iOS rounds the corners itself)
const tile = (size, { rounded, scale }) => `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 64 64">` +
  `<rect width="64" height="64" ${rounded ? 'rx="14"' : ''} fill="${ACCENT}"/>${GLYPH(scale)}</svg>`;

const icons = [
  { file: 'icon-192.png', size: 192, rounded: true, scale: 1 },
  { file: 'icon-512.png', size: 512, rounded: true, scale: 1 },
  { file: 'icon-maskable-192.png', size: 192, rounded: false, scale: 1.08 }, // the mark stays inside the safe zone (a circle of 80 %)
  { file: 'icon-maskable-512.png', size: 512, rounded: false, scale: 1.08 },
  { file: 'apple-touch-icon.png', size: 180, rounded: false, scale: 1.12 },
  { file: 'favicon-32.png', size: 32, rounded: true, scale: 1 },
];

// iOS launch images: [css width, css height, device pixel ratio]
const phones = [
  [440, 956, 3], [402, 874, 3], [430, 932, 3], [393, 852, 3], [390, 844, 3], [375, 812, 3],
  [428, 926, 3], [414, 896, 3], [414, 896, 2], [375, 667, 2], [414, 736, 3], [320, 568, 2],
];
const pads = [
  [744, 1133, 2], [810, 1080, 2], [820, 1180, 2], [834, 1194, 2], [834, 1112, 2], [834, 1210, 2], [1024, 1366, 2], [1032, 1376, 2],
];
const themes = { light: '#f5f4f1', dark: '#151514' }; // --mat-bg of MatUi

(async () => {
  const browser = await chromium.launch({ channel: 'msedge', headless: true }).catch(() => chromium.launch({ channel: 'chrome', headless: true }));
  const shot = async (w, h, html, file) => {
    const page = await browser.newPage({ viewport: { width: w, height: h }, deviceScaleFactor: 1 });
    await page.setContent(`<html><body style="margin:0;background:transparent">${html}</body></html>`);
    await page.screenshot({ path: path.join(img, file), omitBackground: true, type: 'png' });
    await page.close();
  };

  for (const i of icons) { await shot(i.size, i.size, tile(i.size, i), i.file); console.log(i.file); }

  fs.mkdirSync(path.join(img, 'splash'), { recursive: true });
  const links = [];
  const launch = async (w, h, dpr, orientation) => {
    for (const [name, bg] of Object.entries(themes)) {
      const W = w * dpr, H = h * dpr;
      const side = Math.round(Math.min(W, H) * 0.24);
      const file = `splash/${name}-${W}x${H}.png`;
      await shot(W, H, `<div style="width:${W}px;height:${H}px;background:${bg};display:grid;place-items:center">${tile(side, { rounded: true, scale: 1 })}</div>`, file);
      // device-width and device-height do not turn with the orientation on iOS: the portrait size is used for both
      const dw = orientation === 'portrait' ? w : h, dh = orientation === 'portrait' ? h : w;
      links.push(`<link rel="apple-touch-startup-image" href="~/img/${file}" media="(device-width: ${dw}px) and (device-height: ${dh}px) and (-webkit-device-pixel-ratio: ${dpr}) and (orientation: ${orientation}) and (prefers-color-scheme: ${name})" />`);
    }
  };
  for (const [w, h, d] of phones) { await launch(w, h, d, 'portrait'); }
  for (const [w, h, d] of pads) { await launch(w, h, d, 'portrait'); await launch(h, w, d, 'landscape'); }
  await browser.close();

  const head = '@*\n    Launch images of the iOS home screen app (apple-touch-startup-image): what iOS shows between the tap on the icon and the\n' +
    '    first paint, so that there is no white flash. One per device size, light and dark; iOS picks the one whose media query\n' +
    '    fits (device size, pixel ratio, orientation, colour scheme). Written by scripts/make-icons.js from the logo: the flat colour\n' +
    '    of the page and the tile of the logo. Sizes in css px / pixel ratio: iPhone 16 Pro Max .. SE, iPad mini .. Pro 13.\n*@\n';
  fs.writeFileSync(partial, head + links.join('\n') + '\n');
  console.log('launch images', links.length);
})();
