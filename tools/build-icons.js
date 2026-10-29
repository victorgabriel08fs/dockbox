// Gera os PNG e o Dockbox.ico a partir dos SVG em assets/brand.
//
// Uso (na raiz do repositório):
//   npm install --no-save playwright
//   npx playwright install chromium
//   node tools/build-icons.js
//
// Tamanhos até 32 px usam o desenho simplificado (dockbox-icon-small.svg),
// que fica nítido na bandeja e no Explorador.
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const root = path.resolve(__dirname, '..');
const brand = path.join(root, 'assets', 'brand');
const full = fs.readFileSync(path.join(brand, 'dockbox-icon.svg'), 'utf8');
const small = fs.readFileSync(path.join(brand, 'dockbox-icon-small.svg'), 'utf8');

const icoSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
const pngExports = [64, 128, 256, 512, 1024];

// Desenha o SVG num canvas e devolve o PNG e os pixels RGBA.
async function render(page, svg, size) {
  const src = 'data:image/svg+xml;base64,' + Buffer.from(svg).toString('base64');
  const { png, rgba } = await page.evaluate(async ({ src, size }) => {
    const img = new Image();
    img.src = src;
    await img.decode();
    const c = document.createElement('canvas');
    c.width = c.height = size;
    const ctx = c.getContext('2d');
    ctx.imageSmoothingQuality = 'high';
    ctx.drawImage(img, 0, 0, size, size);
    const data = ctx.getImageData(0, 0, size, size).data;
    return { png: c.toDataURL('image/png').split(',')[1], rgba: Array.from(data) };
  }, { src, size });
  return { png: Buffer.from(png, 'base64'), rgba: Buffer.from(rgba) };
}

// Entrada BMP (DIB 32 bits) para os tamanhos pequenos: é o formato mais compatível
// (System.Drawing.Icon, bandeja, Explorador antigo).
function dib(size, rgba) {
  const header = Buffer.alloc(40);
  header.writeUInt32LE(40, 0);
  header.writeInt32LE(size, 4);
  header.writeInt32LE(size * 2, 8); // altura inclui a máscara AND
  header.writeUInt16LE(1, 12);
  header.writeUInt16LE(32, 14);
  const pixels = Buffer.alloc(size * size * 4);
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      const s = (y * size + x) * 4;
      const d = ((size - 1 - y) * size + x) * 4; // de baixo para cima
      pixels[d] = rgba[s + 2];
      pixels[d + 1] = rgba[s + 1];
      pixels[d + 2] = rgba[s];
      pixels[d + 3] = rgba[s + 3];
    }
  }
  const maskRow = Math.ceil(size / 32) * 4;
  const mask = Buffer.alloc(maskRow * size); // tudo zero: a transparência vem do alfa
  header.writeUInt32LE(pixels.length + mask.length, 20);
  return Buffer.concat([header, pixels, mask]);
}

// Monta o .ico: BMP até 64 px, PNG em 128 e 256 px.
function buildIco(images) {
  const header = Buffer.alloc(6);
  header.writeUInt16LE(0, 0);
  header.writeUInt16LE(1, 2);
  header.writeUInt16LE(images.length, 4);
  const dir = Buffer.alloc(16 * images.length);
  let offset = 6 + dir.length;
  images = images.map(({ size, png, rgba }) => ({ size, data: size <= 64 ? dib(size, rgba) : png }));
  images.forEach(({ size, data }, i) => {
    const o = i * 16;
    dir.writeUInt8(size >= 256 ? 0 : size, o);
    dir.writeUInt8(size >= 256 ? 0 : size, o + 1);
    dir.writeUInt8(0, o + 2);
    dir.writeUInt8(0, o + 3);
    dir.writeUInt16LE(1, o + 4);
    dir.writeUInt16LE(32, o + 6);
    dir.writeUInt32LE(data.length, o + 8);
    dir.writeUInt32LE(offset, o + 12);
    offset += data.length;
  });
  return Buffer.concat([header, dir, ...images.map(x => x.data)]);
}

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage();
  await page.setContent('<html><body></body></html>');

  const images = [];
  for (const size of icoSizes)
    images.push({ size, ...(await render(page, size <= 32 ? small : full, size)) });

  const ico = buildIco(images);
  const icoTargets = [path.join(brand, 'Dockbox.ico'), path.join(root, 'src', 'Dockbox', 'Assets', 'Dockbox.ico')];
  for (const t of icoTargets) {
    fs.mkdirSync(path.dirname(t), { recursive: true });
    fs.writeFileSync(t, ico);
  }

  const pngDir = path.join(brand, 'png');
  fs.mkdirSync(pngDir, { recursive: true });
  for (const size of pngExports)
    fs.writeFileSync(path.join(pngDir, `dockbox-${size}.png`), (await render(page, full, size)).png);

  // Usado dentro do app (barra lateral do Gerenciar Dock).
  fs.writeFileSync(path.join(root, 'src', 'Dockbox', 'Assets', 'Dockbox-128.png'), (await render(page, full, 128)).png);

  await browser.close();
  console.log(`Gerados: ${icoTargets.map(t => path.relative(root, t)).join(', ')} e ${pngExports.length + 1} PNG.`);
})();
