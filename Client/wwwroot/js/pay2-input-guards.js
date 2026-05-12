const guardMap = new WeakMap();

function toEnglishDigit(ch) {
    if (ch >= "0" && ch <= "9") return ch;

    const fa = "۰۱۲۳۴۵۶۷۸۹";
    const ar = "٠١٢٣٤٥٦٧٨٩";

    const faIndex = fa.indexOf(ch);
    if (faIndex >= 0) return String(faIndex);

    const arIndex = ar.indexOf(ch);
    if (arIndex >= 0) return String(arIndex);

    return "";
}

function normalizeText(text, allowDash) {
    if (!text) return "";

    let result = "";

    for (const ch of text) {
        const digit = toEnglishDigit(ch);

        if (digit !== "") {
            result += digit;
            continue;
        }

        if (allowDash && ch === "-") {
            result += "-";
        }
    }

    return result;
}

function buildNextValue(el, insertText, maxLength) {
    const current = el.value || "";
    const start = el.selectionStart ?? current.length;
    const end = el.selectionEnd ?? current.length;

    let next = current.substring(0, start) + insertText + current.substring(end);

    if (maxLength && maxLength > 0 && next.length > maxLength) {
        next = next.substring(0, maxLength);
    }

    return {
        value: next,
        caret: Math.min(start + insertText.length, next.length)
    };
}

function insertCleanText(el, text, maxLength) {
    const next = buildNextValue(el, text, maxLength);

    el.value = next.value;

    try {
        el.setSelectionRange(next.caret, next.caret);
    } catch {
        // بعضی inputها ممکن است selection را پشتیبانی نکنند
    }

    el.dispatchEvent(new Event("input", { bubbles: true }));
}

function isAllowedControlInput(inputType) {
    if (!inputType) return false;

    return inputType === "deleteContentBackward"
        || inputType === "deleteContentForward"
        || inputType === "deleteByCut"
        || inputType === "historyUndo"
        || inputType === "historyRedo";
}

export function attachInputGuard(el, options) {
    if (!el) return;

    detachInputGuard(el);

    const maxLength = options?.maxLength ?? 0;
    const allowDash = options?.allowDash === true;

    const beforeInputHandler = function (e) {
        if (isAllowedControlInput(e.inputType)) {
            return true;
        }

        if (!e.data) {
            return true;
        }

        const cleaned = normalizeText(e.data, allowDash);

        if (!cleaned) {
            e.preventDefault();
            return false;
        }

        const next = buildNextValue(el, cleaned, maxLength);

        if (maxLength && maxLength > 0 && next.value.length > maxLength) {
            e.preventDefault();
            return false;
        }

        if (cleaned !== e.data) {
            e.preventDefault();
            insertCleanText(el, cleaned, maxLength);
            return false;
        }

        if (maxLength && maxLength > 0) {
            const current = el.value || "";
            const start = el.selectionStart ?? current.length;
            const end = el.selectionEnd ?? current.length;
            const predictedLength =
                current.substring(0, start).length +
                cleaned.length +
                current.substring(end).length;

            if (predictedLength > maxLength) {
                e.preventDefault();
                return false;
            }
        }

        return true;
    };

    const pasteHandler = function (e) {
        e.preventDefault();

        const raw = (e.clipboardData || window.clipboardData)?.getData("text") || "";
        const cleaned = normalizeText(raw, allowDash);

        if (!cleaned) {
            return false;
        }

        insertCleanText(el, cleaned, maxLength);
        return false;
    };

    const dropHandler = function (e) {
        e.preventDefault();
        return false;
    };

    el.addEventListener("beforeinput", beforeInputHandler);
    el.addEventListener("paste", pasteHandler);
    el.addEventListener("drop", dropHandler);

    guardMap.set(el, {
        beforeInputHandler,
        pasteHandler,
        dropHandler
    });
}

export function detachInputGuard(el) {
    if (!el) return;

    const old = guardMap.get(el);
    if (!old) return;

    el.removeEventListener("beforeinput", old.beforeInputHandler);
    el.removeEventListener("paste", old.pasteHandler);
    el.removeEventListener("drop", old.dropHandler);

    guardMap.delete(el);
}