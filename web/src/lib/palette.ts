/**
 * Applies the user's equipped app theme palette as a `data-palette` attribute on <html>. The token
 * overrides for each palette live in `index.css` (`[data-palette="..."]` scopes), composing with the
 * `.dark` class. The key is cached in localStorage so the inline script in `index.html` can pre-apply
 * it before paint and avoid a flash (mirrors the dark-mode pre-paint).
 */
const STORAGE_KEY = 'turnly-palette'

export function applyPalette(key: string | null | undefined): void {
  const el = document.documentElement
  if (key) {
    el.dataset.palette = key
    localStorage.setItem(STORAGE_KEY, key)
  } else {
    delete el.dataset.palette
    localStorage.removeItem(STORAGE_KEY)
  }
}
