import { createElement, Fragment } from 'react'
import type { ReactNode } from 'react'
import { create } from 'zustand'
import { en } from '@/locales/en'
import type { PluralKey, TKey } from '@/locales/en'
import { hr } from '@/locales/hr'

export type { TKey }

export type Language = 'en' | 'hr'

/** Each language is named in itself, so the picker reads the same whichever one is active. */
export const LANGUAGES: { value: Language; label: string }[] = [
  { value: 'en', label: 'English' },
  { value: 'hr', label: 'Hrvatski' },
]

const STORAGE_KEY = 'turnly-language'

const DICTIONARIES: Record<Language, Record<string, string>> = { en, hr }

/** BCP 47 tag used for dates and plural rules. English stays on en-GB (day-first dates). */
const LOCALE_TAGS: Record<Language, string> = { en: 'en-GB', hr: 'hr-HR' }

function isLanguage(value: unknown): value is Language {
  return value === 'en' || value === 'hr'
}

/** Stored choice if the user has made one, otherwise the browser's language. */
function initialLanguage(): Language {
  try {
    const stored = localStorage.getItem(STORAGE_KEY)
    if (isLanguage(stored)) return stored
  } catch {
    // Storage unavailable: fall through to the browser language.
  }
  return navigator.language.toLowerCase().startsWith('hr') ? 'hr' : 'en'
}

function apply(language: Language) {
  document.documentElement.lang = language
}

interface LanguageState {
  language: Language
  setLanguage: (language: Language) => void
}

/**
 * The UI language is a per-device preference (like the light/dark theme), kept in localStorage.
 * `t` reads it imperatively, so nothing re-renders on its own: `App` keys its tree on the language
 * and remounts when it changes.
 */
export const useLanguageStore = create<LanguageState>((set) => {
  const language = initialLanguage()
  apply(language)
  return {
    language,
    setLanguage: (next) => {
      try {
        localStorage.setItem(STORAGE_KEY, next)
      } catch {
        // Not persisted, but still applies for this session.
      }
      apply(next)
      set({ language: next })
    },
  }
})

export type TParams = Record<string, string | number>

function currentLanguage(): Language {
  return useLanguageStore.getState().language
}

function interpolate(template: string, params?: TParams): string {
  if (!params) return template
  return template.replace(/\{(\w+)\}/g, (match, name: string) =>
    name in params ? String(params[name]) : match,
  )
}

function lookup(key: string): string | undefined {
  return DICTIONARIES[currentLanguage()][key] ?? en[key as TKey]
}

/** Translates a key, filling `{name}` placeholders from `params`. */
export function t(key: TKey, params?: TParams): string {
  return interpolate(lookup(key) ?? key, params)
}

const pluralRules = new Map<Language, Intl.PluralRules>()

/**
 * Translates a counted phrase. Picks `key_one` / `key_few` / `key_other` by the language's plural
 * rules (Croatian needs the `few` form: 1 dan, 2 dana, 5 dana) and exposes the number as `{count}`.
 */
export function plural(key: PluralKey, count: number, params?: TParams): string {
  const language = currentLanguage()
  let rules = pluralRules.get(language)
  if (!rules) {
    rules = new Intl.PluralRules(LOCALE_TAGS[language])
    pluralRules.set(language, rules)
  }
  const template = lookup(`${key}_${rules.select(count)}`) ?? lookup(`${key}_other`) ?? key
  return interpolate(template, { count, ...params })
}

/**
 * `t` for sentences with markup inside them: placeholders are filled with React nodes (a bolded
 * name, say) and the result is a list of nodes, so word order stays up to the translation.
 */
export function tRich(key: TKey, parts: Record<string, ReactNode>): ReactNode[] {
  return (lookup(key) ?? key)
    .split(/(\{\w+\})/)
    .filter((chunk) => chunk !== '')
    .map((chunk, index) => {
      const name = /^\{(\w+)\}$/.exec(chunk)?.[1]
      return createElement(Fragment, { key: index }, name && name in parts ? parts[name] : chunk)
    })
}

/**
 * Translates text that originates in a server catalog (achievements, cosmetics), looked up by its
 * stable key. Falls back to the server's own English text for entries the dictionaries don't know.
 */
export function tServer(key: string, fallback: string): string {
  return lookup(key) ?? fallback
}

/** The locale tag to hand to `toLocaleDateString` and friends. */
export function locale(): string {
  return LOCALE_TAGS[currentLanguage()]
}

/**
 * For module-level option tables: defines the given text fields as getters, so they are resolved
 * when read (in the current language) rather than frozen at import time.
 */
export function localized<T extends object, K extends string>(
  item: T,
  keys: Record<K, TKey>,
): T & Record<K, string> {
  for (const field of Object.keys(keys) as K[]) {
    Object.defineProperty(item, field, { get: () => t(keys[field]), enumerable: true })
  }
  return item as T & Record<K, string>
}
