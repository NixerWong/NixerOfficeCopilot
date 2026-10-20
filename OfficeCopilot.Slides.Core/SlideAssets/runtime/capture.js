/* P4 DOM capture. Loaded only by the local build host. */
(() => {
  const NATIVE_SELECTOR = '[data-nx-capture-native]';
  const TEXT_BLOCK_SELECTOR = '[data-nx-capture-text]';
  const FILL_SELECTOR = '[data-nx-capture-fill]';
  const NON_TEXT_TAGS = ['STYLE', 'SCRIPT', 'TEMPLATE', 'NOSCRIPT'];
  const BORDER_SIDES = ['Top', 'Right', 'Bottom', 'Left'];
  const CORNERS = ['TopLeft', 'TopRight', 'BottomRight', 'BottomLeft'];
  const EMOJI_CHARACTER = /[\p{Extended_Pictographic}\p{Regional_Indicator}\u20E3]/u;

  function rectOf(element, origin) {
    const rect = element.getBoundingClientRect();
    return {
      left: rect.left - origin.left,
      top: rect.top - origin.top,
      width: rect.width,
      height: rect.height
    };
  }

  function cssLengthPx(element, property, fallback) {
    const raw = getComputedStyle(element).getPropertyValue(property).trim();
    if (!raw) return fallback;
    const probe = document.createElement('span');
    probe.style.cssText = 'position:absolute;left:-10000px;top:-10000px;visibility:hidden;height:0;width:var(' + property + ')';
    element.appendChild(probe);
    const value = probe.getBoundingClientRect().width;
    probe.remove();
    return Number.isFinite(value) && value > 0 ? value : fallback;
  }

  function layoutLayerOf(element, slide) {
    for (let current = element; current; current = current.parentElement) {
      const declared = (current.getAttribute('data-nx-layer') || '').toLowerCase();
      if (declared === 'bg' || declared === 'background') return 'background';
      if (declared === 'chrome') return 'chrome';
      if (declared === 'content') return 'content';
      if (current.classList.contains('nx-bg')) return 'background';
      if (current.classList.contains('nx-chrome')) return 'chrome';
      if (current.classList.contains('nx-content') || current.classList.contains('nx-safe')) return 'content';
      if (current === slide) break;
    }
    return 'content';
  }

  // 文字框对齐内容区：背景图里的 padding/边框留给底图，文字框不能从边框外沿开始。
  function contentRectOf(element, origin) {
    const rect = rectOf(element, origin);
    const style = getComputedStyle(element);
    const left = (parseFloat(style.paddingLeft) || 0) + (parseFloat(style.borderLeftWidth) || 0);
    const right = (parseFloat(style.paddingRight) || 0) + (parseFloat(style.borderRightWidth) || 0);
    const top = (parseFloat(style.paddingTop) || 0) + (parseFloat(style.borderTopWidth) || 0);
    const bottom = (parseFloat(style.paddingBottom) || 0) + (parseFloat(style.borderBottomWidth) || 0);
    return {
      left: rect.left + left,
      top: rect.top + top,
      width: Math.max(0, rect.width - left - right),
      height: Math.max(0, rect.height - top - bottom)
    };
  }

  function textAlignmentOf(style) {
    if (style.display !== 'flex' && style.display !== 'inline-flex') return style.textAlign;
    const isRow = style.flexDirection.indexOf('row') === 0;
    const horizontalAlignment = isRow ? style.justifyContent : style.alignItems;
    return horizontalAlignment === 'center' ? 'center' : style.textAlign;
  }

  function verticalAlignmentOf(style) {
    if (style.display !== 'flex' && style.display !== 'inline-flex') return 'top';
    const isRow = style.flexDirection.indexOf('row') === 0;
    const verticalAlignment = isRow ? style.alignItems : style.justifyContent;
    if (verticalAlignment === 'center') return 'middle';
    if (verticalAlignment === 'flex-end' || verticalAlignment === 'end') return 'bottom';
    return 'top';
  }

  function textOf(element) {
    return (element.innerText || element.textContent || '').replace(/\s+/g, ' ').trim();
  }

  function isInlineLevel(element) {
    const display = getComputedStyle(element).display;
    return element.hasAttribute('data-nx-icon') || display === 'inline' || display === 'contents';
  }

  function hasOwnText(element) {
    return Array.from(element.childNodes).some((node) => node.nodeType === Node.TEXT_NODE && node.data.trim().length > 0);
  }

  // 位于另一个文字块的内联流中的元素由该文字块负责；块级后代各自独立采集。
  function insideTextBlock(element) {
    if (!isInlineLevel(element)) return false;
    for (let parent = element.parentElement; parent; parent = parent.parentElement) {
      if (parent.hasAttribute('data-nx-capture-text')) return true;
      if (!isInlineLevel(parent)) return false;
    }
    return false;
  }

  function inlineTextNodes(element) {
    const nodes = [];
    const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
    let node;
    while ((node = walker.nextNode())) {
      let inline = true;
      for (let parent = node.parentElement; parent && parent !== element; parent = parent.parentElement) {
        if (!isInlineLevel(parent)) {
          inline = false;
          break;
        }
      }
      if (inline) nodes.push(node);
    }
    return nodes;
  }

  // PowerPoint 不会应用 CSS text-transform，这里按浏览器的渲染结果改写字符。
  function transformCharacter(character, transform, previous) {
    if (transform === 'uppercase') return character.toUpperCase();
    if (transform === 'lowercase') return character.toLowerCase();
    if (transform === 'capitalize' && (!previous || /\s/.test(previous))) return character.toUpperCase();
    return character;
  }

  function graphemes(text) {
    if (typeof Intl.Segmenter === 'function') {
      return Array.from(new Intl.Segmenter(undefined, { granularity: 'grapheme' }).segment(text), (part) => part.segment);
    }

    const result = [];
    let current = '';
    let joinNext = false;
    let regionalCount = 0;
    for (const character of Array.from(text)) {
      const codePoint = character.codePointAt(0);
      const regional = codePoint >= 0x1F1E6 && codePoint <= 0x1F1FF;
      const modifier = (codePoint >= 0x1F3FB && codePoint <= 0x1F3FF)
        || (codePoint >= 0xFE00 && codePoint <= 0xFE0F)
        || codePoint === 0x20E3
        || (codePoint >= 0x0300 && codePoint <= 0x036F);
      if (current && (joinNext || modifier || codePoint === 0x200D || (regional && regionalCount % 2 === 1))) {
        current += character;
      } else {
        if (current) result.push(current);
        current = character;
      }
      joinNext = codePoint === 0x200D;
      regionalCount = regional ? regionalCount + 1 : 0;
    }
    if (current) result.push(current);
    return result;
  }

  function emojiImage(text, style, rectangle) {
    const scale = 2;
    const fontSize = parseFloat(style.fontSize) || 16;
    const canvas = document.createElement('canvas');
    const probe = canvas.getContext('2d');
    if (!probe) throw new Error('Canvas 2D context is unavailable while capturing emoji.');
    const font = 'normal 400 ' + fontSize + 'px "Noto Color Emoji"';
    probe.font = font;
    const metrics = probe.measureText(text);
    const width = Math.max(rectangle.width, metrics.width, fontSize * 0.75);
    const height = Math.max(rectangle.height, fontSize);
    canvas.width = Math.max(1, Math.ceil(width * scale));
    canvas.height = Math.max(1, Math.ceil(height * scale));
    const context = canvas.getContext('2d');
    if (!context) throw new Error('Canvas 2D context is unavailable while drawing emoji.');
    context.scale(scale, scale);
    context.font = font;
    context.fillStyle = style.color;
    context.textBaseline = 'alphabetic';
    const ascent = metrics.actualBoundingBoxAscent || fontSize * 0.9;
    const descent = metrics.actualBoundingBoxDescent || fontSize * 0.1;
    const baseline = Math.max(ascent, (height + ascent - descent) / 2);
    context.fillText(text, Math.max(0, (width - metrics.width) / 2), baseline);
    return canvas.toDataURL('image/png');
  }

  async function svgImage(svg, origin) {
    const rect = svg.getBoundingClientRect();
    const clone = svg.cloneNode(true);
    const color = getComputedStyle(svg).color;
    clone.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
    clone.setAttribute('width', Math.max(1, rect.width) + 'px');
    clone.setAttribute('height', Math.max(1, rect.height) + 'px');
    clone.setAttribute('style', 'color:' + color);
    const image = new Image();
    image.src = 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(new XMLSerializer().serializeToString(clone));
    await image.decode();

    const scale = 2;
    const canvas = document.createElement('canvas');
    canvas.width = Math.max(1, Math.ceil(rect.width * scale));
    canvas.height = Math.max(1, Math.ceil(rect.height * scale));
    const context = canvas.getContext('2d');
    if (!context) throw new Error('Canvas 2D context is unavailable while capturing a Lucide icon.');
    context.drawImage(image, 0, 0, canvas.width, canvas.height);
    return {
      rectangle: rectOf(svg, origin),
      source: canvas.toDataURL('image/png'),
      overlayOnText: true
    };
  }

  function captureTextLines(element, origin) {
    const lines = [];
    const emojiImages = [];
    const range = document.createRange();
    let previous = '';
    for (const node of inlineTextNodes(element)) {
      const transform = getComputedStyle(node.parentElement).textTransform;
      let offset = 0;
      for (const grapheme of graphemes(node.data)) {
        const emoji = !grapheme.includes('\uFE0E') && EMOJI_CHARACTER.test(grapheme);
        range.setStart(node, offset);
        range.setEnd(node, offset + grapheme.length);
        const rect = range.getBoundingClientRect();
        if (rect.width > 0 && rect.height > 0) {
          const top = rect.top - origin.top;
          let line = lines.find((item) => Math.abs(item.top - top) <= 2);
          if (!line) {
            line = { text: '', top, left: rect.left - origin.left, right: rect.right - origin.left, bottom: rect.bottom - origin.top };
            lines.push(line);
          }
          if (emoji) {
            line.text += '\u2003';
            const style = getComputedStyle(node.parentElement);
            emojiImages.push({
              rectangle: {
                left: rect.left - origin.left,
                top: rect.top - origin.top,
                width: rect.width,
                height: rect.height
              },
              source: emojiImage(grapheme, style, rect),
              overlayOnText: true
            });
          } else {
            let transformed = '';
            for (const character of grapheme) {
              transformed += transformCharacter(character, transform, previous);
              previous = character;
            }
            line.text += transformed;
          }
          line.left = Math.min(line.left, rect.left - origin.left);
          line.right = Math.max(line.right, rect.right - origin.left);
          line.bottom = Math.max(line.bottom, rect.bottom - origin.top);
        }
        offset += grapheme.length;
      }
    }

    const lineDetails = lines.sort((a, b) => a.top - b.top || a.left - b.left).map((line) => ({
      text: line.text.replace(/[ \t\r\n]+/g, ' ').replace(/^ +| +$/g, ''),
      rectangle: { left: line.left, top: line.top, width: line.right - line.left, height: line.bottom - line.top }
    })).filter((line) => line.text.length > 0);
    return {
      text: lineDetails.map((line) => line.text).join(' '),
      lineDetails,
      emojiImages
    };
  }

  function colorOf(value) {
    return value === 'rgba(0, 0, 0, 0)' ? 'transparent' : value;
  }

  function alphaOf(value) {
    const match = /^rgba\(([^)]+)\)$/.exec(value || '');
    if (!match) return value && value !== 'transparent' ? 1 : 0;
    const parts = match[1].split(',');
    return parts.length > 3 ? Math.max(0, Math.min(1, parseFloat(parts[3]))) : 1;
  }

  // 色块、边框和行内高亮没有对应的 PowerPoint 文字属性，改为基本形状；渐变、阴影、圆角、虚线和变换留在背景图里。
  function decorationsOf(element, origin) {
    if (element.tagName === 'IMG' || element.closest('table,svg,canvas,[data-nx-chart],aside.notes,[data-nx-capture-native]')) return [];
    const style = getComputedStyle(element);
    if (style.display === 'contents' || style.backgroundImage !== 'none' || style.boxShadow !== 'none'
      || style.transform !== 'none' || Number(style.opacity) < 1 || style.visibility === 'hidden') return [];
    if (CORNERS.some((corner) => parseFloat(style[`border${corner}Radius`]) > 0)) return [];

    const fillAlpha = alphaOf(style.backgroundColor);
    const fill = fillAlpha > 0 ? colorOf(style.backgroundColor) : 'transparent';
    const sides = BORDER_SIDES.map((side) => {
      const lineStyle = style[`border${side}Style`];
      if (!['none', 'hidden', 'solid'].includes(lineStyle)) return null;
      return {
        side,
        width: lineStyle === 'solid' ? parseFloat(style[`border${side}Width`]) || 0 : 0,
        color: colorOf(style[`border${side}Color`]),
        opacity: alphaOf(style[`border${side}Color`])
      };
    });
    if (sides.some((entry) => entry === null)) return [];

    const shape = (rectangle, overrides) => Object.assign({
      rectangle,
      kind: 'rect',
      fill: 'transparent',
      fillOpacity: 1,
      stroke: 'transparent',
      strokeOpacity: 1,
      strokeWidth: 0,
      radius: 0
    }, overrides);

    if (isInlineLevel(element)) {
      if (fill === 'transparent' || sides.some((entry) => entry.width > 0)) return [];
      return Array.from(element.getClientRects())
        .filter((box) => box.width > 0 && box.height > 0)
        .map((box) => shape({
          left: box.left - origin.left,
          top: box.top - origin.top,
          width: box.width,
          height: box.height
        }, { fill, fillOpacity: fillAlpha }));
    }

    if (!visible(element)) return [];
    const rect = rectOf(element, origin);
    const first = sides[0];
    const uniform = sides.every((entry) => entry.width === first.width && entry.color === first.color && entry.opacity === first.opacity);
    if (uniform && first.width > 0) {
      // PowerPoint 描边居中于路径，内收半个线宽后外缘与 CSS 边框一致。
      const half = first.width / 2;
      return [shape({
        left: rect.left + half,
        top: rect.top + half,
        width: Math.max(0, rect.width - first.width),
        height: Math.max(0, rect.height - first.width)
      }, {
        fill,
        fillOpacity: fillAlpha,
        stroke: first.color,
        strokeOpacity: first.opacity,
        strokeWidth: first.width
      })];
    }

    const result = [];
    if (fill !== 'transparent') result.push(shape(rect, { fill, fillOpacity: fillAlpha }));
    sides.forEach((entry) => {
      if (entry.width <= 0) return;
      const w = entry.width;
      const edge = {
        Top: { left: rect.left, top: rect.top, width: rect.width, height: w },
        Right: { left: rect.left + rect.width - w, top: rect.top, width: w, height: rect.height },
        Bottom: { left: rect.left, top: rect.top + rect.height - w, width: rect.width, height: w },
        Left: { left: rect.left, top: rect.top, width: w, height: rect.height }
      }[entry.side];
      result.push(shape(edge, { fill: entry.color, fillOpacity: entry.opacity }));
    });
    return result;
  }

  function cropFor(image, box, style) {
    if (style.objectFit !== 'cover' || !image.naturalWidth || !image.naturalHeight) {
      return { cropLeft: 0, cropTop: 0, cropRight: 0, cropBottom: 0 };
    }

    const scale = Math.max(box.width / image.naturalWidth, box.height / image.naturalHeight);
    const renderedWidth = image.naturalWidth * scale;
    const renderedHeight = image.naturalHeight * scale;
    const horizontal = Math.max(0, (renderedWidth - box.width) / renderedWidth);
    const vertical = Math.max(0, (renderedHeight - box.height) / renderedHeight);
    const positions = (style.objectPosition || '50% 50%').split(/\s+/);
    let horizontalPosition = positions[0] || '50%';
    let verticalPosition = positions[1] || '50%';
    if (/^(top|bottom)$/i.test(horizontalPosition)) {
      const swap = horizontalPosition;
      horizontalPosition = verticalPosition;
      verticalPosition = swap;
    }
    const fraction = (value) => {
      if (/^(left|top)$/i.test(value)) return 0;
      if (/^(right|bottom)$/i.test(value)) return 1;
      const percent = parseFloat(value);
      return value.indexOf('%') >= 0 && Number.isFinite(percent) ? Math.max(0, Math.min(1, percent / 100)) : 0.5;
    };
    const left = horizontal * fraction(horizontalPosition);
    const top = vertical * fraction(verticalPosition);
    return {
      cropLeft: left,
      cropTop: top,
      cropRight: horizontal - left,
      cropBottom: vertical - top
    };
  }

  function visible(element) {
    const style = getComputedStyle(element);
    const rect = element.getBoundingClientRect();
    return style.display !== 'none' && style.visibility !== 'hidden' && Number(style.opacity) !== 0
      && rect.width > 0 && rect.height > 0;
  }

  function intersects(a, b) {
    return Math.min(a.right, b.right) - Math.max(a.left, b.left) > 1
      && Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top) > 1;
  }

  function boxPaints(style) {
    if (!style) return false;
    if (alphaOf(style.backgroundColor) > 0 || style.backgroundImage !== 'none' || style.boxShadow !== 'none') return true;
    if (style.outlineStyle !== 'none' && parseFloat(style.outlineWidth) > 0) return true;
    return BORDER_SIDES.some((side) => style[`border${side}Style`] !== 'none' && parseFloat(style[`border${side}Width`]) > 0);
  }

  // 元素在背景模式下仍会画进背景图的非文字内容（色块、边框、阴影、未原生化的 SVG/canvas、伪元素）。
  function paintsRaster(element) {
    if (element.closest(NATIVE_SELECTOR) || element.closest('aside.notes')) return false;
    const svg = element.closest('svg');
    if (svg && svg !== element) return false;
    if (!visible(element)) return false;
    const tag = element.tagName.toUpperCase();
    if (tag === 'SVG' || tag === 'CANVAS' || tag === 'VIDEO') return true;
    if (boxPaints(getComputedStyle(element))) return true;
    return ['::before', '::after'].some((pseudo) => {
      const style = getComputedStyle(element, pseudo);
      return style.content && style.content !== 'none' && style.content !== 'normal' && style.display !== 'none' && boxPaints(style);
    });
  }

  // 单元格背景沿 tr → tbody → table 逐级回溯，斑马纹和分类色带常设在行或分组上。
  function backgroundOf(cell, table) {
    for (let node = cell; node; node = node.parentElement) {
      const value = colorOf(getComputedStyle(node).backgroundColor);
      if (value !== 'transparent') return value;
      if (node === table) break;
    }
    return 'transparent';
  }

  function borderOf(style, side) {
    const lineStyle = style[`border${side}Style`];
    const width = lineStyle === 'none' || lineStyle === 'hidden' ? 0 : parseFloat(style[`border${side}Width`]) || 0;
    return { width, color: colorOf(style[`border${side}Color`]) };
  }

  function captureTable(table, origin) {
    if (Array.from(table.querySelectorAll('td,th')).some((cell) => cell.rowSpan !== 1 || cell.colSpan !== 1)) return null;
    const domRows = Array.from(table.rows || []);
    const rows = domRows.map((row) => ({
      height: row.getBoundingClientRect().height,
      cells: Array.from(row.cells || []).map((cell) => {
        const style = getComputedStyle(cell);
        const textStyle = getComputedStyle(cell.querySelector('p,h1,h2,h3,h4,h5,h6') || cell);
        return {
          text: textOf(cell),
          fill: backgroundOf(cell, table),
          color: colorOf(textStyle.color),
          fontFamily: textStyle.fontFamily,
          fontSize: parseFloat(textStyle.fontSize) || 0,
          fontWeight: textStyle.fontWeight,
          fontStyle: textStyle.fontStyle,
          bold: Number(textStyle.fontWeight) >= 600 || textStyle.fontWeight === 'bold',
          textAlign: textStyle.textAlign,
          lineHeight: textStyle.lineHeight === 'normal' ? (parseFloat(textStyle.fontSize) || 0) * 1.2 : parseFloat(textStyle.lineHeight) || 0,
          verticalAlign: style.verticalAlign,
          padding: {
            top: parseFloat(style.paddingTop) || 0,
            right: parseFloat(style.paddingRight) || 0,
            bottom: parseFloat(style.paddingBottom) || 0,
            left: parseFloat(style.paddingLeft) || 0
          },
          borders: {
            top: borderOf(style, 'Top'),
            right: borderOf(style, 'Right'),
            bottom: borderOf(style, 'Bottom'),
            left: borderOf(style, 'Left')
          }
        };
      })
    }));
    const columns = rows.length ? rows[0].cells.length : 0;
    if (!columns || rows.length > 20 || columns > 12 || rows.some((row) => row.cells.length !== columns)) return null;
    const columnWidths = Array.from(domRows[0].cells).map((cell) => cell.getBoundingClientRect().width);
    return { rectangle: rectOf(table, origin), columnWidths, rows };
  }

  function captureChart(element, origin) {
    try {
      const data = JSON.parse(element.getAttribute('data-nx-chart') || '{}');
      const type = String(data.type || '').toLowerCase();
      if (!['bar', 'line', 'pie', 'doughnut'].includes(type)
        || !Array.isArray(data.labels) || data.labels.length < 1 || data.labels.length > 24
        || !Array.isArray(data.series) || data.series.length < 1 || data.series.length > 8
        || ((type === 'pie' || type === 'doughnut') && data.series.length !== 1)
        || !data.series.every((series) => series && Array.isArray(series.values)
          && series.values.length === data.labels.length
          && series.values.every((value) => typeof value === 'number' && Number.isFinite(value)))) return null;
      return {
        rectangle: rectOf(element, origin),
        type,
        title: typeof data.title === 'string' ? data.title : null,
        labels: data.labels.map((label) => String(label).slice(0, 200)),
        series: data.series.map((series) => ({
          name: typeof series.name === 'string' ? series.name.slice(0, 100) : null,
          values: series.values,
          color: typeof series.color === 'string' ? series.color.slice(0, 32) : null
        }))
      };
    } catch (_) {
      return null;
    }
  }

  async function waitForLayout() {
    const emojiFonts = await document.fonts.load(
      'normal 400 16px "Noto Color Emoji"',
      '😀🫠🩷🫶👨‍👩‍👧‍👦👍🏽🏳️‍🌈');
    if (!emojiFonts.some((font) => font.status === 'loaded')) {
      throw new Error('Bundled Noto Color Emoji font could not be loaded for capture.');
    }
    await document.fonts.ready;
    await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
  }

  window.nixerCapture = {
    async read(slideNumber, mode) {
      const stage = document.querySelector('deck-stage');
      const slides = stage
        ? Array.from(stage.children).filter((element) => !['STYLE', 'SCRIPT', 'TEMPLATE'].includes(element.tagName))
        : Array.from(document.querySelectorAll('.slide')).filter((element) => !(element.parentElement && element.parentElement.closest('.slide')));
      const index = Math.max(0, Math.min(slides.length - 1, (Number(slideNumber) || 1) - 1));
      const slide = slides[index];
      if (!slide) throw new Error('没有可采集的幻灯片。');
      if (window.nixerStage && typeof window.nixerStage.go === 'function') window.nixerStage.go(index + 1);
      await waitForLayout();

      const slideRect = slide.getBoundingClientRect();
      const safeAreaX = cssLengthPx(slide, '--nx-safe-x', 128);
      const safeAreaY = cssLengthPx(slide, '--nx-safe-y', 96);
      const actionAreaX = cssLengthPx(slide, '--nx-action-x', 64);
      const actionAreaY = cssLengthPx(slide, '--nx-action-y', 48);
      const captured = new Set();
      const tables = Array.from(slide.querySelectorAll('table')).filter(visible).map((table) => {
        const value = captureTable(table, slideRect);
        if (value) {
          table.setAttribute('data-nx-capture-native', '');
          captured.add(table);
        }
        return value;
      }).filter(Boolean);
      const charts = Array.from(slide.querySelectorAll('[data-nx-chart]')).filter(visible).map((element) => {
        const value = captureChart(element, slideRect);
        if (value) {
          element.setAttribute('data-nx-capture-native', '');
          captured.add(element);
        }
        return value;
      }).filter(Boolean);
      slide.querySelectorAll(TEXT_BLOCK_SELECTOR).forEach((element) => element.removeAttribute('data-nx-capture-text'));
      const textBlocks = [];
      const emojiImages = [];
      Array.from(slide.querySelectorAll('*')).forEach((element) => {
        if (NON_TEXT_TAGS.includes(element.tagName) || !hasOwnText(element)) return;
        if (element.closest('aside.notes') || !visible(element) || insideTextBlock(element)) return;
        const native = element.closest('table,[data-nx-chart]');
        if (native && captured.has(native)) return;
        const style = getComputedStyle(element);
        const lineDetails = captureTextLines(element, slideRect);
        emojiImages.push(...lineDetails.emojiImages);
        if (!lineDetails.text) return;
        const chrome = layoutLayerOf(element, slide) === 'chrome';
        element.setAttribute('data-nx-capture-text', '');
        textBlocks.push({
          rectangle: contentRectOf(element, slideRect),
          text: lineDetails.text,
          lines: lineDetails.lineDetails.map((line) => line.text),
          lineDetails: lineDetails.lineDetails,
          fontFamily: style.fontFamily,
          fontSize: parseFloat(style.fontSize) || 0,
          fontWeight: style.fontWeight,
          fontStyle: style.fontStyle,
          color: colorOf(style.color),
          lineHeight: style.lineHeight === 'normal' ? parseFloat(style.fontSize) * 1.2 : parseFloat(style.lineHeight) || 0,
          letterSpacing: parseFloat(style.letterSpacing) || 0,
          textAlign: textAlignmentOf(style),
          verticalAlign: verticalAlignmentOf(style),
          safeAreaX: chrome ? actionAreaX : safeAreaX,
          safeAreaY: chrome ? actionAreaY : safeAreaY
        });
      });

      const images = Array.from(slide.querySelectorAll('img')).filter(visible).map((image) => {
        const box = rectOf(image, slideRect);
        const style = getComputedStyle(image);
        image.setAttribute('data-nx-capture-native', '');
        captured.add(image);
        return Object.assign({ rectangle: box, source: image.currentSrc || image.src }, cropFor(image, box, style));
      });
      images.push(...emojiImages);
      for (const svg of slide.querySelectorAll('[data-nx-icon] > svg')) {
        if (!visible(svg)) continue;
        images.push(await svgImage(svg, slideRect));
        svg.setAttribute('data-nx-capture-native', '');
        captured.add(svg);
      }

      // 原生形状总是叠在背景图之上；若某个候选形状之后（DOM 绘制顺序更晚）还有与其重叠、且留在背景图里的绘制内容，
      // 提取为原生形状会把这些内容盖住（例如铺满整页的底色容器盖住其中的圆形色块），此时整块留在背景图里。
      const elements = Array.from(slide.querySelectorAll('*'));
      const candidates = [];
      const candidateSet = new Set();
      elements.forEach((element, order) => {
        if (element === slide || element.closest('aside.notes')) return;
        const kind = element.getAttribute('data-nx-shape');
        if (kind) {
          if (!['rect', 'rounded-rect', 'line'].includes(kind) || !visible(element)) return;
          const style = getComputedStyle(element);
          candidates.push({
            element,
            order,
            explicit: true,
            shapes: [{
              rectangle: rectOf(element, slideRect),
              kind,
              fill: colorOf(style.backgroundColor),
              fillOpacity: alphaOf(style.backgroundColor),
              stroke: colorOf(style.borderTopColor),
              strokeOpacity: alphaOf(style.borderTopColor),
              strokeWidth: parseFloat(style.borderTopWidth) || 0,
              radius: parseFloat(style.borderTopLeftRadius) || 0
            }]
          });
          candidateSet.add(element);
          return;
        }
        if (mode !== 'editable') return;
        const decorations = decorationsOf(element, slideRect);
        if (decorations.length === 0) return;
        candidates.push({ element, order, explicit: false, shapes: decorations });
        candidateSet.add(element);
      });

      if (mode === 'editable') {
        const raster = [];
        elements.forEach((element, order) => {
          if (!candidateSet.has(element) && paintsRaster(element)) raster.push({ order, rect: element.getBoundingClientRect() });
        });
        for (let i = candidates.length - 1; i >= 0; i--) {
          const candidate = candidates[i];
          const rect = candidate.element.getBoundingClientRect();
          if (raster.some((entry) => entry.order > candidate.order && intersects(entry.rect, rect))) {
            candidate.demoted = true;
            raster.push({ order: candidate.order, rect });
          }
        }
      }

      const shapes = [];
      candidates.forEach((candidate) => {
        if (candidate.demoted) return;
        if (candidate.explicit) {
          if (mode === 'editable') {
            candidate.element.setAttribute('data-nx-capture-native', '');
            captured.add(candidate.element);
          }
        } else {
          candidate.element.setAttribute('data-nx-capture-fill', '');
        }
        shapes.push(...candidate.shapes);
      });

      const notes = Array.from(slide.querySelectorAll('aside.notes'))
        .map((element) => (element.innerText || element.textContent || '').trim())
        .filter((text) => text.length > 0)
        .join('\n\n');

      return {
        slide: index + 1,
        width: slideRect.width,
        height: slideRect.height,
        safeAreaX,
        safeAreaY,
        textBlocks,
        images,
        shapes,
        tables,
        charts,
        notes
      };
    },

    setBackgroundMode(enabled) {
      let style = document.getElementById('nx-capture-hide-native');
      if (!style) {
        style = document.createElement('style');
        style.id = 'nx-capture-hide-native';
        document.head.appendChild(style);
      }
      // 文字块只把文字涂成透明，保留其背景和内联高亮，文字由 PowerPoint 原生文本框承载。
      style.textContent = enabled
        ? NATIVE_SELECTOR + '{visibility:hidden!important}'
          + FILL_SELECTOR + '{background-color:transparent!important;border-color:transparent!important}'
          + TEXT_BLOCK_SELECTOR + ',' + TEXT_BLOCK_SELECTOR + ' *{color:transparent!important;-webkit-text-fill-color:transparent!important;'
          + '-webkit-text-stroke-color:transparent!important;text-shadow:none!important;text-decoration-color:transparent!important;caret-color:transparent!important}'
        : '';
      return true;
    }
  };
})();
