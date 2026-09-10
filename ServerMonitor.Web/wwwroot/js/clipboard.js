// Copies text to the clipboard, including over plain HTTP.
//
// navigator.clipboard exists only in a secure context: HTTPS, or localhost. A dashboard on a home
// network is neither — it is opened as http://192.168.x.x — so there navigator.clipboard is
// undefined, and a copy button built on it alone silently does nothing. The fallback selects the
// text in a temporary textarea and uses execCommand, which is deprecated but still works exactly
// where the modern API is withheld.
window.serverMonitorCopy = async function (text) {
    if (navigator.clipboard && window.isSecureContext) {
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch {
            // Permission denied: fall through to the older way.
        }
    }

    const area = document.createElement("textarea");
    area.value = text;
    area.setAttribute("readonly", "");
    area.style.position = "fixed";
    area.style.top = "-1000px";
    document.body.appendChild(area);
    area.select();

    let copied = false;

    try {
        copied = document.execCommand("copy");
    } catch {
        copied = false;
    }

    document.body.removeChild(area);

    return copied;
};
