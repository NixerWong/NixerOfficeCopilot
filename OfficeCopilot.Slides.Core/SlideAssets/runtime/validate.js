/* P3 L1 DOM validation. Loaded only by the local validation host. */
(() => {
  const BLOCK_SELECTOR = 'h1,h2,h3,h4,h5,h6,p,li,td,.nx-text';
  const PLACEHOLDER = /\b(?:lorem ipsum|placeholder|todo|tbd|xx+|option [abc])\b|\[(?:topic|year|studio|title|name)\]/i;
  const SKIP_SELECTOR = 'script,style,noscript,svg,[aria-hidden="true"],[data-nx-hidden]';
  const DESIGN_WIDTH = 1920;
  const DESIGN_HEIGHT = 1080;
  const DEFAULT_SAFE_X = 128;
  const DEFAULT_SAFE_Y = 96;
  const DEFAULT_ACTION_X = 64;
  const DEFAULT_ACTION_Y = 48;
  const CJK_TEXT = /[\u2e80-\u2fff\u3000-\u303f\u3040-\u30ff\u3100-\u312f\u3130-\u318f\u31a0-\u31bf\u31c0-\u31ef\u3200-\u32ff\u3400-\u4dbf\u4e00-\u9fff\uac00-\ud7af\uf900-\ufaff]/;
  const EMOJI_CHARACTER = /[\p{Extended_Pictographic}\p{Regional_Indicator}\u20E3]/u;
  let combiningMarkPattern = null;
  try {
    combiningMarkPattern = new RegExp('\\p{M}', 'u');
  } catch (error) {
    combiningMarkPattern = null;
  }
  const GRAPHEME_SEGMENTER = typeof Intl !== 'undefined' && typeof Intl.Segmenter === 'function'
    ? new Intl.Segmenter(undefined, { granularity: 'grapheme' })
    : null;
  let glyphCoverageMilliseconds = 0;

  function normalizeFamily(name) {
    return (name || '').trim().replace(/^['"]|['"]$/g, '').toLowerCase();
  }

  function parseFamilyStack(value) {
    return (value || '').split(',').map((name) => name.trim().replace(/^['"]|['"]$/g, '')).filter(Boolean);
  }

  function normalizeFontPolicy(policy) {
    if (!policy || !policy.families || !Array.isArray(policy.system) || !Array.isArray(policy.generic)) {
      throw new Error('fontPolicy must contain families, system, and generic lists.');
    }

    const families = new Map();
    Object.keys(policy.families).forEach((name) => {
      const family = policy.families[name];
      if (!family || !Array.isArray(family.weights) || typeof family.italic !== 'string'
        || typeof family.bundled !== 'boolean' || typeof family.cjk !== 'boolean' || typeof family.emojiOnly !== 'boolean'
        || !validCoverage(family.coverage) || !validCoverage(family.emojiCoverage)) {
        throw new Error('fontPolicy family metadata is invalid for ' + name + '.');
      }
      families.set(normalizeFamily(name), family);
    });

    return {
      families,
      system: new Set(policy.system.map(normalizeFamily)),
      generic: new Set(policy.generic.map(normalizeFamily))
    };
  }

  function validCoverage(ranges) {
    if (!Array.isArray(ranges)) return false;
    let previousEnd = -1;
    for (const range of ranges) {
      if (!range || !Number.isInteger(range.start) || !Number.isInteger(range.end)
        || range.start < 0 || range.end > 0x10FFFF || range.start > range.end
        || range.start <= previousEnd) {
        return false;
      }
      previousEnd = range.end;
    }
    return true;
  }

  function isVariationSelector(codePoint) {
    return (codePoint >= 0xFE00 && codePoint <= 0xFE0F)
      || (codePoint >= 0xE0100 && codePoint <= 0xE01EF);
  }

  function isGraphemeExtend(codePoint, character) {
    return (combiningMarkPattern && combiningMarkPattern.test(character))
      || (codePoint >= 0x1F3FB && codePoint <= 0x1F3FF)
      || isVariationSelector(codePoint)
      || (codePoint >= 0xE0020 && codePoint <= 0xE007F)
      || codePoint === 0x20E3;
  }

  function isRegionalIndicator(codePoint) {
    return codePoint >= 0x1F1E6 && codePoint <= 0x1F1FF;
  }

  function graphemesOf(text) {
    if (GRAPHEME_SEGMENTER) {
      return Array.from(GRAPHEME_SEGMENTER.segment(text), (item) => item.segment);
    }

    const graphemes = [];
    let cluster = '';
    let previousCodePoint = -1;
    let regionalIndicatorCount = 0;
    for (const character of Array.from(text)) {
      const codePoint = character.codePointAt(0);
      const joins = cluster.length > 0 && (isGraphemeExtend(codePoint, character)
        || codePoint === 0x200D
        || previousCodePoint === 0x200D
        || (isRegionalIndicator(codePoint) && isRegionalIndicator(previousCodePoint) && regionalIndicatorCount % 2 === 1));
      if (!joins && cluster) {
        graphemes.push(cluster);
        cluster = '';
        regionalIndicatorCount = 0;
      }
      cluster += character;
      if (isRegionalIndicator(codePoint)) regionalIndicatorCount++;
      else if (!isGraphemeExtend(codePoint, character) && codePoint !== 0x200D) regionalIndicatorCount = 0;
      previousCodePoint = codePoint;
    }
    if (cluster) graphemes.push(cluster);
    return graphemes;
  }

  function isNonRenderingCodePoint(codePoint) {
    return codePoint === 0x200C || codePoint === 0x200D || isVariationSelector(codePoint)
      || (codePoint >= 0xE0020 && codePoint <= 0xE007F);
  }

  function coversCodePoint(ranges, codePoint) {
    let low = 0;
    let high = ranges.length - 1;
    while (low <= high) {
      const middle = (low + high) >> 1;
      const range = ranges[middle];
      if (codePoint < range.start) high = middle - 1;
      else if (codePoint > range.end) low = middle + 1;
      else return true;
    }
    return false;
  }

  function hasFontCoverage(family, codePoint) {
    return coversCodePoint(family.coverage, codePoint)
      || coversCodePoint(family.emojiCoverage, codePoint);
  }

  function formatCodePoint(codePoint) {
    const hexadecimal = codePoint.toString(16).toUpperCase();
    return 'U+' + ('0000' + hexadecimal).slice(-Math.max(4, hexadecimal.length));
  }

  function pseudoContentText(content) {
    const raw = (content || '').trim();
    if (!raw || raw === 'none' || raw === 'normal' || /\burl\s*\(/i.test(raw)) return '';

    let quote = '';
    let text = '';
    let foundString = false;
    for (let index = 0; index < raw.length; index++) {
      const character = raw[index];
      if (!quote) {
        if (character === '"' || character === "'") {
          quote = character;
          foundString = true;
        }
        continue;
      }
      if (character === quote) {
        quote = '';
        continue;
      }
      if (character !== '\\') {
        text += character;
        continue;
      }

      index++;
      if (index >= raw.length) break;
      if (/[0-9a-f]/i.test(raw[index])) {
        let hexadecimal = '';
        while (index < raw.length && hexadecimal.length < 6 && /[0-9a-f]/i.test(raw[index])) {
          hexadecimal += raw[index++];
        }
        if (index < raw.length && /\s/.test(raw[index])) {
          if (raw[index] === '\r' && raw[index + 1] === '\n') index++;
        } else {
          index--;
        }
        let codePoint = parseInt(hexadecimal, 16);
        if (codePoint === 0 || codePoint > 0x10FFFF || (codePoint >= 0xD800 && codePoint <= 0xDFFF)) {
          codePoint = 0xFFFD;
        }
        text += String.fromCodePoint(codePoint);
      } else if (raw[index] !== '\n' && raw[index] !== '\r') {
        text += raw[index];
      }
    }
    return foundString ? text : '';
  }

  function rectOf(element) {
    const rect = element.getBoundingClientRect();
    return { left: rect.left, top: rect.top, right: rect.right, bottom: rect.bottom, width: rect.width, height: rect.height };
  }

  function cssLengthPx(element, property, fallback) {
    const raw = getComputedStyle(element).getPropertyValue(property).trim();
    if (!raw) return { value: fallback, valid: true };
    const probe = document.createElement('span');
    probe.style.cssText = 'position:absolute;left:-10000px;top:-10000px;visibility:hidden;height:0;width:var(' + property + ')';
    element.appendChild(probe);
    const value = probe.getBoundingClientRect().width;
    probe.remove();
    const valid = Number.isFinite(value) && (value > 0 || /^0(?:px)?$/.test(raw));
    return { value: valid ? value : fallback, valid };
  }

  function layoutLayerOf(element, slide) {
    for (let current = element; current; current = current.parentElement) {
      const declared = (current.getAttribute('data-nx-layer') || '').toLowerCase();
      if (declared === 'bg' || declared === 'background') return { name: 'background', explicit: true };
      if (declared === 'chrome') return { name: 'chrome', explicit: true };
      if (declared === 'content') return { name: 'content', explicit: true };
      if (current.classList.contains('nx-bg')) return { name: 'background', explicit: true };
      if (current.classList.contains('nx-chrome')) return { name: 'chrome', explicit: true };
      if (current.classList.contains('nx-content') || current.classList.contains('nx-safe')) return { name: 'content', explicit: true };
      const layer = getComputedStyle(current).getPropertyValue('--nx-layer').trim().toLowerCase();
      const parentLayer = current.parentElement
        ? getComputedStyle(current.parentElement).getPropertyValue('--nx-layer').trim().toLowerCase()
        : '';
      if (layer && layer !== parentLayer) return { name: layer, explicit: true };
      if (current === slide) break;
    }
    return { name: 'content', explicit: false };
  }

  function safeAreaFor(slide, policy, add) {
    const safeX = cssLengthPx(slide, '--nx-safe-x', DEFAULT_SAFE_X);
    const safeY = cssLengthPx(slide, '--nx-safe-y', DEFAULT_SAFE_Y);
    const actionX = cssLengthPx(slide, '--nx-action-x', DEFAULT_ACTION_X);
    const actionY = cssLengthPx(slide, '--nx-action-y', DEFAULT_ACTION_Y);
    const exception = (slide.getAttribute('data-nx-safe-exception')
      || getComputedStyle(slide).getPropertyValue('--nx-safe-exception')).trim().toLowerCase();
    const validException = exception === 'absolute-80'
      && Math.abs(safeX.value - policy.exceptionX) < 0.5
      && Math.abs(safeY.value - policy.exceptionY) < 0.5;
    const minX = validException ? policy.exceptionX : policy.minX;
    const minY = validException ? policy.exceptionY : policy.minY;
    const overridden = !safeX.valid || !safeY.valid || !actionX.valid || !actionY.valid
      || (exception && !validException) || safeX.value < minX || safeY.value < minY;
    if (overridden) {
      add('layout.safe_override', 'warn', '安全区变量无效或低于允许下限，校验按下限执行。', slide);
    }
    return {
      targetX: Math.max(safeX.value, minX),
      targetY: Math.max(safeY.value, minY),
      minX,
      minY,
      actionX: actionX.value,
      actionY: actionY.value
    };
  }

  function visible(element) {
    if (!element || element.closest(SKIP_SELECTOR)) return false;
    const style = getComputedStyle(element);
    const rect = element.getBoundingClientRect();
    return style.display !== 'none' && style.visibility !== 'hidden' && Number(style.opacity) !== 0
      && rect.width > 0 && rect.height > 0;
  }

  function visibleFontText(element) {
    if (!element || element.closest('script,style,noscript,[data-nx-hidden]')) return false;
    for (let current = element; current; current = current.parentElement) {
      const style = getComputedStyle(current);
      if (style.display === 'none' || style.visibility === 'hidden' || Number(style.opacity) === 0) return false;
    }
    return true;
  }

  function selectorOf(element) {
    const classes = Array.from(element.classList || []).slice(0, 2).map((name) => '.' + name).join('');
    return element.tagName.toLowerCase() + classes;
  }

  function textOf(element) {
    return (element.innerText || element.textContent || '').replace(/\s+/g, ' ').trim();
  }

  function lineRects(element) {
    const range = document.createRange();
    range.selectNodeContents(element);
    return Array.from(range.getClientRects()).filter((rect) => rect.width > 0 && rect.height > 0);
  }

  function intersects(a, b) {
    return Math.min(a.right, b.right) - Math.max(a.left, b.left) > 1
      && Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top) > 1;
  }

  function collectBlocks(slide) {
    return Array.from(slide.querySelectorAll(BLOCK_SELECTOR)).filter((element) => {
      if (!visible(element) || !textOf(element)) return false;
      return !Array.from(element.children).some((child) => child.matches(BLOCK_SELECTOR));
    });
  }

  function classNamesInRules(rules, names) {
    for (const rule of Array.from(rules || [])) {
      if (typeof rule.selectorText === 'string') {
        const matches = rule.selectorText.matchAll(/\.([A-Za-z_][A-Za-z0-9_-]*)/g);
        for (const match of matches) names.add(match[1]);
      }
      if (rule.cssRules) classNamesInRules(rule.cssRules, names);
    }
  }

  function authoredClassRules(slides) {
    const definitions = new Map();
    const styles = Array.from(document.querySelectorAll('style'))
      .filter((style) => style.id === 'nx-theme' || style.closest('.slide'));

    for (const style of styles) {
      if (!style.sheet) continue;
      const ownerSlide = style.id === 'nx-theme'
        ? 0
        : slides.indexOf(style.closest('.slide')) + 1;
      if (ownerSlide < 0 || (ownerSlide === 0 && style.id !== 'nx-theme')) continue;
      const names = new Set();
      classNamesInRules(style.sheet.cssRules, names);
      const source = style.id === 'nx-theme' ? 'theme.css' : '第 ' + ownerSlide + ' 页样式';
      for (const name of names) {
        if (!definitions.has(name)) definitions.set(name, []);
        definitions.get(name).push({ ownerSlide, source });
      }
    }

    return definitions;
  }

  function classAllowedOnSlide(className, slideNumber, ownerSlide) {
    if (className === 'slide' || className.startsWith('nx-') || className.startsWith('theme-')) return true;
    if (className.startsWith('cover-')) return slideNumber === 1;
    const pagePrefix = /^s(\d{2,})-/.exec(className);
    if (pagePrefix) return slideNumber === Number(pagePrefix[1]);
    // theme.css 本来就作用于整稿，其中的普通类可在任意页使用。
    if (ownerSlide === 0) return true;
    return slideNumber === ownerSlide;
  }

  function validateSlide(slide, slideNumber, density, maxIssues, fontSampling, fontPolicy, safePolicy, classRules) {
    const issues = [];
    const slideRect = rectOf(slide);
    const area = safeAreaFor(slide, safePolicy, (code, severity, message, element) => {
      if (issues.length < maxIssues) issues.push({ code, severity, slide: slideNumber, message, selector: selectorOf(element) });
    });
    const scaleX = slideRect.width / DESIGN_WIDTH;
    const scaleY = slideRect.height / DESIGN_HEIGHT;
    const blocks = collectBlocks(slide);
    const textRects = [];
    const sampledFamilies = new Set();
    let reportedUnlayered = false;
    const add = (code, severity, message, element, excerpt, selectorOverride) => {
      if (issues.length >= maxIssues) return;
      issues.push({
        code,
        severity,
        slide: slideNumber,
        message,
        selector: selectorOverride || (element ? selectorOf(element) : undefined),
        text: excerpt ? excerpt.slice(0, 120) : undefined
      });
    };
    if (slideNumber > 1) {
      const reportedClassConflicts = new Set();
      const elements = [slide, ...Array.from(slide.querySelectorAll('[class]'))];
      for (const element of elements) {
        for (const className of element.classList) {
          const definitions = classRules.get(className) || [];
          for (const definition of definitions) {
            if (classAllowedOnSlide(className, slideNumber, definition.ownerSlide)) continue;
            const selector = selectorOf(element);
            const key = className + '|' + selector;
            if (reportedClassConflicts.has(key)) continue;
            reportedClassConflicts.add(key);
            add(
              'theme.class_collision',
              'error',
              '类 .' + className + ' 在' + definition.source + '中定义，却用于第 ' + slideNumber
                + ' 页；跨页共享类请用 theme-*，封面类用 cover-*，页面私有类用对应的 sNN-*。',
              element,
              undefined,
              selector);
          }
        }
      }
    }
    slide.querySelectorAll('.nx-chrome,[data-nx-layer="chrome"]').forEach((chrome) => {
      if (!visible(chrome) || !textOf(chrome)) return;
      const rect = rectOf(chrome);
      if (rect.height <= slideRect.height * 0.5) return;
      const bandTop = slideRect.top + slideRect.height * 0.2;
      const bandBottom = slideRect.bottom - slideRect.height * 0.2;
      const midLine = lineRects(chrome).some((line) => {
        const center = (line.top + line.bottom) / 2;
        return center > bandTop && center < bandBottom;
      });
      if (midLine) {
        add(
          'layout.chrome_stretched',
          'error',
          'chrome 层高度占满页面，页码/页脚文字落在页面中部。运行时 .nx-chrome 是 top/bottom 各 48px 的全页覆盖层；'
            + '做页脚条请同时写 bottom 与 top:auto（页眉写 top 与 bottom:auto），或把页脚放进 .nx-chrome 内的绝对定位子元素。',
          chrome,
          textOf(chrome));
      }
    });
    const reportedFontIssues = new Set();
    const reportedGlyphIssues = new Set();
    const addFontIssue = (code, message, element, text, tuple) => {
      const key = code + '|' + tuple;
      if (reportedFontIssues.has(key)) return;
      reportedFontIssues.add(key);
      add(code, 'warn', message, element, text);
    };
    const checkGlyphCoverage = (text, element, style, selector) => {
      const startedAt = performance.now();
      const names = parseFamilyStack(style.fontFamily).map(normalizeFamily);
      const bundledFamilies = names.map((name) => fontPolicy.families.get(name)).filter(Boolean);
      if (!bundledFamilies.length) return;

      for (const grapheme of graphemesOf(text || '')) {
        if (!grapheme.trim()) continue;
        const missing = Array.from(grapheme)
          .map((character) => character.codePointAt(0))
          .filter((codePoint) => !isNonRenderingCodePoint(codePoint)
            && !bundledFamilies.some((family) => hasFontCoverage(family, codePoint)));
        const issueSelector = selector || selectorOf(element);
        const issueKey = issueSelector + '|' + grapheme;
        if (missing.length && !reportedGlyphIssues.has(issueKey)) {
          reportedGlyphIssues.add(issueKey);
          const replacement = EMOJI_CHARACTER.test(grapheme)
            ? '；请替换为语义相近且随包 emoji 字体覆盖的标准 emoji。'
            : '；请替换字符或选择有覆盖的字体。';
          add(
            'glyph.missing',
            'error',
            '随包字体栈没有覆盖字符“' + grapheme + '”（'
              + missing.map(formatCodePoint).join(', ') + '）' + replacement,
            element,
            grapheme,
            issueSelector);
        }
      }
      glyphCoverageMilliseconds += performance.now() - startedAt;
    };
    const checkFontSampling = (element, style, sampledFamilies, fontSampling, slideNumber) => {
      const declaredFamily = style.fontFamily;
      if (sampledFamilies.has(declaredFamily)) return;
      if (fontSampling.targets.length >= 120 || sampledFamilies.size >= 3) {
        fontSampling.truncated = true;
        return;
      }

      sampledFamilies.add(declaredFamily);
      const id = 'nx-font-' + slideNumber + '-' + sampledFamilies.size;
      element.setAttribute('data-nx-validation-font', id);
      fontSampling.targets.push({
        id,
        slide: slideNumber,
        declaredFamily,
        selector: selectorOf(element),
        text: textOf(element).slice(0, 120)
      });
    };
    const checkLayout = (element, rects, excerpt) => {
      const layer = layoutLayerOf(element, slide);
      if (layer.name === 'background') return;
      if (!layer.explicit && !reportedUnlayered) {
        reportedUnlayered = true;
        add('layout.unlayered', 'warn', '内容未声明 content、chrome 或 background 层，按 content 安全区校验。', element, excerpt);
      }
      const chrome = layer.name === 'chrome';
      const targetX = (chrome ? area.actionX : area.targetX) * scaleX;
      const targetY = (chrome ? area.actionY : area.targetY) * scaleY;
      const minX = area.minX * scaleX;
      const minY = area.minY * scaleY;
      const crosses = (rect, insetX, insetY) => rect.left < slideRect.left + insetX
        || rect.top < slideRect.top + insetY
        || rect.right > slideRect.right - insetX
        || rect.bottom > slideRect.bottom - insetY;
      const describeMargins = () => {
        const margins = rects.reduce((result, rect) => ({
          left: Math.min(result.left, (rect.left - slideRect.left) / scaleX),
          right: Math.min(result.right, (slideRect.right - rect.right) / scaleX),
          top: Math.min(result.top, (rect.top - slideRect.top) / scaleY),
          bottom: Math.min(result.bottom, (slideRect.bottom - rect.bottom) / scaleY)
        }), { left: Infinity, right: Infinity, top: Infinity, bottom: Infinity });
        return '（实测边距 L/R/T/B '
          + [margins.left, margins.right, margins.top, margins.bottom]
            .map(value => Number.isFinite(value) ? Math.round(value * 10) / 10 : 'n/a')
            .join('/')
          + ' 设计像素）';
      };
      if (chrome) {
        if (rects.some((rect) => crosses(rect, targetX, targetY))) {
          add('layout.chrome_unsafe', 'warn', '页眉页脚或角标超出 action 安全区。' + describeMargins(), element, excerpt);
        }
        return;
      }
      const unsafe = rects.some((rect) => crosses(rect, minX, minY));
      if (unsafe) {
        add(
          'layout.unsafe',
          'warn',
          '内容超出最小安全区（下限 ' + area.minX + '/' + area.minY + ' 设计像素）。' + describeMargins(),
          element,
          excerpt);
      } else if (rects.some((rect) => crosses(rect, targetX, targetY))) {
        add(
          'layout.tight',
          'warn',
          '内容处于最小安全区内，但未达到目标安全区 ' + area.targetX + '/' + area.targetY + ' 设计像素。'
            + describeMargins(),
          element,
          excerpt);
      }
    };

    const walker = document.createTreeWalker(slide, NodeFilter.SHOW_TEXT);
    let textNode;
    while ((textNode = walker.nextNode())) {
      const text = (textNode.nodeValue || '').replace(/\s+/g, ' ').trim();
      const element = textNode.parentElement;
      if (!text || !visibleFontText(element)) continue;

      const range = document.createRange();
      range.selectNodeContents(textNode);
      if (!Array.from(range.getClientRects()).some((rect) => rect.width > 0 && rect.height > 0)) continue;

      const style = getComputedStyle(element);
      const stack = parseFamilyStack(style.fontFamily);
      const names = stack.map(normalizeFamily);
      const tuple = names.join(',') + '|' + style.fontWeight + '|' + style.fontStyle;
      checkGlyphCoverage(textNode.nodeValue, element, style);
      checkFontSampling(element, style, sampledFamilies, fontSampling, slideNumber);

      const firstDeclared = names.find((name) => !fontPolicy.generic.has(name));
      if (firstDeclared && !fontPolicy.families.has(firstDeclared) && !fontPolicy.system.has(firstDeclared)) {
        addFontIssue(
          'font.not_bundled',
          '字体栈首个非通用字体“' + stack[names.indexOf(firstDeclared)] + '”不在随包或系统白名单中。',
          element,
          text,
          tuple);
      }

      const primaryFamily = firstDeclared && fontPolicy.families.get(firstDeclared);
      if (primaryFamily && primaryFamily.emojiOnly) {
        addFontIssue(
          'font.emoji_primary',
          '字体栈首位是仅用于 emoji 回退的字体，应改用正文随包字体。',
          element,
          text,
          tuple);
      }

      const weight = style.fontWeight === 'bold' ? 700
        : style.fontWeight === 'normal' ? 400
          : parseInt(style.fontWeight, 10);
      const renderingCodePoints = (grapheme) => Array.from(grapheme)
        .map((character) => character.codePointAt(0))
        .filter((codePoint) => !isNonRenderingCodePoint(codePoint));
      const usedFamilies = new Set();
      for (const grapheme of graphemesOf(text)) {
        if (!grapheme.trim() || EMOJI_CHARACTER.test(grapheme)) continue;
        const codePoints = renderingCodePoints(grapheme);
        const family = names
          .map((name) => ({ name, family: fontPolicy.families.get(name) }))
          .find((item) => item.family && !item.family.emojiOnly
            && codePoints.length > 0
            && codePoints.every((codePoint) => coversCodePoint(item.family.coverage, codePoint)));
        if (family) usedFamilies.add(family.name);
      }

      for (const familyName of usedFamilies) {
        const family = fontPolicy.families.get(familyName);
        if (!family.bundled) continue;
        const familyTuple = tuple + '|' + familyName;
        const familyDisplayName = stack[names.indexOf(familyName)];
        if (!family.weights.includes(weight)) {
          addFontIssue(
            'font.weight_unavailable',
            '字体“' + familyDisplayName + '”的字重 ' + style.fontWeight
              + ' 不在随包矩阵 [' + family.weights.join(', ') + '] 中。',
            element,
            text,
            familyTuple);
        }

        if ((style.fontStyle === 'italic' || style.fontStyle === 'oblique')
          && family.italic !== 'true') {
          addFontIssue(
            'font.style_synthesized',
            '字体“' + familyDisplayName + '”的斜体策略为 “'
              + family.italic + '”，不应请求 ' + style.fontStyle + '。',
            element,
            text,
            familyTuple);
        }
      }

      if (CJK_TEXT.test(text)
        && !names.some((name) => {
          const family = fontPolicy.families.get(name);
          return family && family.cjk;
        })) {
        addFontIssue(
          'font.no_cjk_fallback',
          '文本含中文，但字体栈里没有任何中文字体（随包或系统均可），中文会被随机回退成宋体等。请在栈中加入一个中文字体。',
          element,
          text,
          tuple);
      }
    }

    slide.querySelectorAll('*').forEach((element) => {
      if (!visibleFontText(element)) return;
      ['::before', '::after'].forEach((pseudo) => {
        const style = getComputedStyle(element, pseudo);
        if (style.display === 'none' || style.visibility === 'hidden' || Number(style.opacity) === 0) return;
        const text = pseudoContentText(style.content);
        if (text.trim()) checkGlyphCoverage(text, element, style, selectorOf(element) + pseudo);
      });
    });

    blocks.forEach((block) => {
      const text = textOf(block);
      const blockRects = lineRects(block);
      if (!blockRects.length) return;
      for (let ancestor = block; ancestor && ancestor !== slide; ancestor = ancestor.parentElement) {
        const overflowStyle = getComputedStyle(ancestor);
        const clipX = overflowStyle.overflowX !== 'visible';
        const clipY = overflowStyle.overflowY !== 'visible';
        if (!clipX && !clipY) continue;
        // 只看文字行是否真的落在裁剪框外；装饰元素（如出血的圆形色块）撑大 scrollWidth 不算文字被裁。
        const box = rectOf(ancestor);
        const clipped = blockRects.some((rect) => (clipX && (rect.left < box.left - 1 || rect.right > box.right + 1))
          || (clipY && (rect.top < box.top - 1 || rect.bottom > box.bottom + 1)));
        if (clipped) {
          add('text.clipped', 'error', '文本容器内容超出可视区域（溢出祖先：' + selectorOf(ancestor) + '）。', block, text);
          break;
        }
      }

      if (blockRects.some((rect) => rect.left < slideRect.left - 1 || rect.top < slideRect.top - 1
        || rect.right > slideRect.right + 1 || rect.bottom > slideRect.bottom + 1)) {
        add('text.outside', 'error', '文本超出幻灯片舞台边界。', block, text);
      }

      checkLayout(block, blockRects, text);

      const fontSize = parseFloat(getComputedStyle(block).fontSize) || 0;
      const minimum = density === 'low' ? 28 : 20;
      if (fontSize > 0 && fontSize < minimum) {
        add('text.too_small', 'warn', '正文小于 ' + minimum + 'px。', block, text);
      }

      if (PLACEHOLDER.test(text)) {
        add('content.placeholder', 'error', '检测到占位文字或流程标签。', block, text);
      }

      blockRects.forEach((rect) => textRects.push({ element: block, rect, text }));
    });

    for (let i = 0; i < textRects.length && issues.length < maxIssues; i++) {
      for (let j = i + 1; j < textRects.length && issues.length < maxIssues; j++) {
        const first = textRects[i];
        const second = textRects[j];
        if (first.element.contains(second.element) || second.element.contains(first.element)) continue;
        if (intersects(first.rect, second.rect)) {
          add('text.overlap', 'error', '两个文字行的矩形区域重叠。', second.element, second.text);
        }
      }

      const line = textRects[i];
      const x = Math.max(slideRect.left, Math.min(slideRect.right - 1, line.rect.left + Math.min(8, line.rect.width / 2)));
      const y = Math.max(slideRect.top, Math.min(slideRect.bottom - 1, line.rect.top + line.rect.height / 2));
      const top = document.elementFromPoint(x, y);
      if (top && top !== line.element && !line.element.contains(top) && !top.contains(line.element)) {
        add('text.covered', 'error', '文字行采样点被其他元素遮挡。', line.element, line.text);
      }
    }

    slide.querySelectorAll('img').forEach((image) => {
      if (visible(image) && (!image.complete || image.naturalWidth === 0)) {
        add('image.broken', 'error', '图片未能加载。', image, image.alt || image.getAttribute('src') || '');
      }
      const box = rectOf(image);
      if (visible(image)) checkLayout(image, [box], image.alt || '');
    });

    const bulletCount = slide.querySelectorAll('li').length;
    const maxBullets = density === 'low' ? 3 : 8;
    if (bulletCount > maxBullets) {
      add('density.exceeded', 'warn', '要点数 ' + bulletCount + ' 超过 ' + density + ' 密度建议上限 ' + maxBullets + '。', slide);
    }

    return issues;
  }

  function getSlides() {
    const stage = document.querySelector('deck-stage');
    if (stage) {
      return Array.from(stage.children).filter((element) => !['STYLE', 'SCRIPT', 'TEMPLATE'].includes(element.tagName));
    }
    return Array.from(document.querySelectorAll('.slide')).filter((element) => !(element.parentElement && element.parentElement.closest('.slide')));
  }

  window.nixerValidate = async (options) => {
    glyphCoverageMilliseconds = 0;
    const settings = options || {};
    const fontPolicy = normalizeFontPolicy(settings.fontPolicy);
    const safePolicy = Object.assign({ minX: 112, minY: 80, exceptionX: 80, exceptionY: 80 }, settings.safeArea || {});
    if (![safePolicy.minX, safePolicy.minY, safePolicy.exceptionX, safePolicy.exceptionY]
      .every((value) => Number.isFinite(value) && value >= 0)) {
      throw new Error('safeArea policy must contain finite, nonnegative inset values.');
    }
    const density = settings.density === 'low' ? 'low' : 'high';
    const maxIssuesPerSlide = Math.max(1, Math.min(100, settings.maxIssuesPerSlide || 30));
    const slides = getSlides();
    const issues = [];
    const classRules = authoredClassRules(slides);
    const fontSampling = { targets: [], truncated: false };
    const stageApi = window.nixerStage;
    await document.fonts.ready;

    for (let index = 0; index < slides.length; index++) {
      if (stageApi && typeof stageApi.go === 'function') stageApi.go(index + 1);
      await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
      issues.push(...validateSlide(
        slides[index],
        index + 1,
        density,
        maxIssuesPerSlide,
        fontSampling,
        fontPolicy,
        safePolicy,
        classRules));
    }

    return {
      slideCount: slides.length,
      issues,
      fontTargets: fontSampling.targets,
      fontTargetsTruncated: fontSampling.truncated,
      glyphCoverageMilliseconds,
      truncated: issues.length >= slides.length * maxIssuesPerSlide
    };
  };
})();