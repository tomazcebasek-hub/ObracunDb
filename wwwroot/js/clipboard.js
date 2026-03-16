window.copyHtmlToClipboard = async function (html) {
    if (typeof ClipboardItem !== 'undefined' && navigator.clipboard && navigator.clipboard.write) {
        const blob = new Blob([html], { type: 'text/html' });
        const item = new ClipboardItem({ 'text/html': blob });
        await navigator.clipboard.write([item]);
    } else {
        // Fallback za HTTP (non-secure) kontekst
        const listener = function (e) {
            e.clipboardData.setData('text/html', html);
            e.clipboardData.setData('text/plain', html.replace(/<[^>]*>/g, ''));
            e.preventDefault();
        };
        document.addEventListener('copy', listener);
        document.execCommand('copy');
        document.removeEventListener('copy', listener);
    }
};

window.downloadFileFromBytes = function (fileName, base64) {
    var bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0));
    var blob = new Blob([bytes], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
    var url = URL.createObjectURL(blob);
    var link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
};

window.scrollFocusedRowIntoView = function () {
    setTimeout(function () {
        var row = document.querySelector('.dxbl-focused');
        if (row) row.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    }, 50);
};
