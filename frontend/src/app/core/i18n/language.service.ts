import { Injectable, signal } from '@angular/core';
import { AppLanguage, EN_FRAGMENT_RULES, TR_TO_EN } from './ui-translations';

interface AttributeState {
  original: string;
  translated?: string;
}

@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly storageKey = 'sqladvisor.language';
  private readonly originalText = new WeakMap<Text, string>();
  private readonly translatedText = new WeakMap<Text, string>();
  private readonly attributeState = new WeakMap<Element, Map<string, AttributeState>>();
  private observer?: MutationObserver;
  private started = false;
  private readonly translatedAttributes = ['placeholder', 'title', 'aria-label'];
  private readonly sortedPhrases = Object.entries(TR_TO_EN)
    .filter(([source]) => source.length >= 4)
    .sort((a, b) => b[0].length - a[0].length);

  readonly language = signal<AppLanguage>(this.readInitialLanguage());

  constructor() {
    this.applyDocumentLanguage();
  }

  setLanguage(language: AppLanguage): void {
    if (language !== 'tr' && language !== 'en') return;
    this.language.set(language);
    try { localStorage.setItem(this.storageKey, language); } catch { }
    this.applyDocumentLanguage();
    this.translateTree(document.body);
  }

  start(): void {
    if (this.started || typeof document === 'undefined') return;
    this.started = true;

    const attach = () => {
      if (!document.body) return;
      this.translateTree(document.body);
      this.observer = new MutationObserver(records => this.onMutations(records));
      this.observer.observe(document.body, {
        subtree: true,
        childList: true,
        characterData: true,
        attributes: true,
        attributeFilter: this.translatedAttributes
      });
    };

    if (document.body) attach();
    else document.addEventListener('DOMContentLoaded', attach, { once: true });
  }

  translate(value: string | null | undefined): string {
    if (!value || this.language() === 'tr') return value ?? '';
    return this.toEnglish(value);
  }

  private onMutations(records: MutationRecord[]): void {
    for (const record of records) {
      if (record.type === 'characterData' && record.target instanceof Text) {
        const node = record.target;
        const current = node.nodeValue ?? '';
        if (current === this.translatedText.get(node)) continue;
        this.originalText.set(node, current);
        this.translatedText.delete(node);
        this.translateTextNode(node);
        continue;
      }

      if (record.type === 'attributes' && record.target instanceof Element && record.attributeName) {
        this.captureAndTranslateAttribute(record.target, record.attributeName);
        continue;
      }

      for (const node of Array.from(record.addedNodes))
        this.translateTree(node);
    }
  }

  private translateTree(root: Node | null): void {
    if (!root) return;

    if (root instanceof Text) {
      this.translateTextNode(root);
      return;
    }

    if (!(root instanceof Element) && root !== document.body) return;
    if (root instanceof Element && this.shouldSkip(root)) return;

    if (root instanceof Element)
      this.translateElementAttributes(root);

    const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT | NodeFilter.SHOW_ELEMENT);
    let current: Node | null;
    while ((current = walker.nextNode())) {
      if (current instanceof Element) {
        if (this.shouldSkip(current)) {
          current = this.skipSubtree(walker, current);
          if (!current) break;
        } else {
          this.translateElementAttributes(current);
        }
      } else if (current instanceof Text) {
        this.translateTextNode(current);
      }
    }
  }

  private skipSubtree(walker: TreeWalker, element: Element): Node | null {
    let next = walker.nextSibling();
    while (!next) {
      const parent = walker.parentNode();
      if (!parent || parent === document.body || !(parent instanceof Element)) return null;
      next = walker.nextSibling();
    }
    return next;
  }

  private shouldSkip(element: Element): boolean {
    return Boolean(element.closest('pre, code, script, style, [data-no-translate]'));
  }

  private translateTextNode(node: Text): void {
    const parent = node.parentElement;
    if (!parent || this.shouldSkip(parent)) return;

    const current = node.nodeValue ?? '';
    const lastTranslated = this.translatedText.get(node);
    if (!this.originalText.has(node) || (lastTranslated !== undefined && current !== lastTranslated))
      this.originalText.set(node, current);

    const original = this.originalText.get(node) ?? current;
    if (this.language() === 'tr') {
      if (current !== original) node.nodeValue = original;
      this.translatedText.delete(node);
      return;
    }

    const translated = this.toEnglish(original);
    this.translatedText.set(node, translated);
    if (current !== translated) node.nodeValue = translated;
  }

  private translateElementAttributes(element: Element): void {
    if (this.shouldSkip(element)) return;
    for (const attribute of this.translatedAttributes)
      this.captureAndTranslateAttribute(element, attribute);
  }

  private captureAndTranslateAttribute(element: Element, attribute: string): void {
    if (!element.hasAttribute(attribute) || this.shouldSkip(element)) return;

    const current = element.getAttribute(attribute) ?? '';
    let states = this.attributeState.get(element);
    if (!states) {
      states = new Map<string, AttributeState>();
      this.attributeState.set(element, states);
    }

    let state = states.get(attribute);
    if (!state || (state.translated !== undefined && current !== state.translated)) {
      state = { original: current };
      states.set(attribute, state);
    }

    if (this.language() === 'tr') {
      if (current !== state.original) element.setAttribute(attribute, state.original);
      state.translated = undefined;
      return;
    }

    const translated = this.toEnglish(state.original);
    state.translated = translated;
    if (current !== translated) element.setAttribute(attribute, translated);
  }

  private toEnglish(value: string): string {
    if (!value.trim()) return value;

    const leading = value.match(/^\s*/)?.[0] ?? '';
    const trailing = value.match(/\s*$/)?.[0] ?? '';
    const core = value.slice(leading.length, value.length - trailing.length || undefined);

    const exact = TR_TO_EN[core];
    if (exact !== undefined) return `${leading}${exact}${trailing}`;

    let translated = core;
    for (const [source, target] of this.sortedPhrases) {
      if (translated.includes(source))
        translated = translated.split(source).join(target);
    }
    for (const [pattern, replacement] of EN_FRAGMENT_RULES)
      translated = translated.replace(pattern, replacement);

    return `${leading}${translated}${trailing}`;
  }

  private readInitialLanguage(): AppLanguage {
    try {
      const stored = localStorage.getItem(this.storageKey);
      if (stored === 'en' || stored === 'tr') return stored;
    } catch { }
    return 'tr';
  }

  private applyDocumentLanguage(): void {
    if (typeof document !== 'undefined')
      document.documentElement.lang = this.language();
  }
}
