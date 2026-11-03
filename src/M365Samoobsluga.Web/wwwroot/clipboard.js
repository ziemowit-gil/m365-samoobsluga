// Kopiowanie podpisu do schowka: wersja HTML (z formatowaniem) albo zwykły tekst.
window.samoobsluga = {
  // Pobranie tekstu jako pliku (UTF-8), np. szablonu HTML stopki.
  download(filename, text, mime) {
    const url = URL.createObjectURL(new Blob([text], { type: (mime || 'text/plain') + ';charset=utf-8' }));
    const a = document.createElement('a');
    a.href = url; a.download = filename;
    document.body.appendChild(a); a.click(); a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  },
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
