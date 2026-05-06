// Theme interop for Cress Studio Web
// Manages [data-theme] on <html> and persists to localStorage under 'cress.theme'.

const STORAGE_KEY = 'cress.theme';

/**
 * Update the <meta name="color-scheme"> tag to match the resolved theme.
 * This ensures OS-native form controls (date pickers, scrollbars) use the right palette.
 * @param {string} effective - 'light' or 'dark'
 */
function _updateColorSchemeMeta(effective) {
    var meta = document.querySelector('meta[name="color-scheme"]');
    if (meta) {
        meta.content = effective;
    }
}

/**
 * Apply and persist a theme value.
 * @param {string} value - 'light', 'dark', or 'system'
 */
window.setTheme = function (value) {
    if (value === 'system') {
        document.documentElement.removeAttribute('data-theme');
        localStorage.removeItem(STORAGE_KEY);
        // System mode: declare support for both, let OS decide
        _updateColorSchemeMeta('light dark');
    } else {
        document.documentElement.setAttribute('data-theme', value);
        localStorage.setItem(STORAGE_KEY, value);
        _updateColorSchemeMeta(value);
    }
};

/**
 * Read the stored theme preference.
 * @returns {string} 'light', 'dark', or 'system' (default when absent)
 */
window.getTheme = function () {
    return localStorage.getItem(STORAGE_KEY) ?? 'system';
};

/**
 * Resolve the currently rendered theme regardless of mode.
 * @returns {string} 'light' or 'dark'
 */
window.getEffectiveTheme = function () {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored === 'light') return 'light';
    if (stored === 'dark') return 'dark';
    return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
};
