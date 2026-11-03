// Kopiowanie podpisu do schowka: wersja HTML (z formatowaniem) albo zwykły tekst.
window.samoobsluga = {
  async copyHtml(html, text) {
    try {
      if (window.ClipboardItem && navigator.clipboard.write) {
        await navigator.clipboard.write([new ClipboardItem({
          'text/html': new Blob([html], { type: 'text/html' }),
          'text/plain': new Blob([text], { type: 'text/plain' }) })]);
        return true;
      }
      await navigator.clipboard.writeText(text);
      return true;
    } catch { return false; }
  },
  async copyText(text) {
    try { await navigator.clipboard.writeText(text); return true; } catch { return false; }
  }
};
