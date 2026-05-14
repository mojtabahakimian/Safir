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

function normalizeText(text, options, currentValue, selectionStart, selectionEnd) {
    if (!text) return "";

    const allowDecimal = options.allowDecimal === true;
    const maxDecimalPlaces = Math.max(0, options.maxDecimalPlaces ?? 0);
    const persianDate = options.persianDate === true;

    const safeCurrent = currentValue || "";
    const start = selectionStart ?? safeCurrent.length;
    const end = selectionEnd ?? safeCurrent.length;

    const valueAfterSelectionRemoved =
        safeCurrent.substring(0, start) + safeCurrent.substring(end);

    let hasDecimal = valueAfterSelectionRemoved.includes(".");
    let result = "";

    for (const ch of text) {
        const digit = toEnglishDigit(ch);

        if (digit !== "") {
            result += digit;
            continue;
        }

        if (!persianDate && allowDecimal && (ch === "." || ch === "/" || ch === "٫" || ch === ",")) {
            if (!hasDecimal) {
                result += ".";
                hasDecimal = true;
            }
        }
    }

    if (persianDate) {
        result = result.replace(/\D/g, "");
    }

    if (allowDecimal && result.includes(".")) {
        const dotIndex = result.indexOf(".");
        const intPart = result.substring(0, dotIndex);
        const decPart = result.substring(dotIndex + 1).replace(/\./g, "");
        result = maxDecimalPlaces > 0
            ? intPart + "." + decPart.substring(0, maxDecimalPlaces)
            : intPart;
    }

    return result;
}

function limitByMaxLength(current, start, end, insertText, maxLength) {
    if (maxLength <= 0) return insertText;

    const currentLengthWithoutSelection = current.length - (end - start);
    const spaceLeft = maxLength - currentLengthWithoutSelection;

    if (spaceLeft <= 0) return "";

    return insertText.substring(0, spaceLeft);
}

function fixDecimalPlaces(value, maxDecimalPlaces) {
    const dotIndex = value.indexOf(".");
    if (dotIndex < 0) return value;

    if (maxDecimalPlaces <= 0) {
        return value.substring(0, dotIndex);
    }

    const intPart = value.substring(0, dotIndex);
    const decPart = value.substring(dotIndex + 1).replace(/\./g, "");

    return intPart + "." + decPart.substring(0, maxDecimalPlaces);
}

function insertAtCaret(el, text, options) {
    if (!el || !text) return;

    const maxLength = Math.max(0, options.maxLength ?? 0);
    const maxDecimalPlaces = Math.max(0, options.maxDecimalPlaces ?? 0);
    const allowDecimal = options.allowDecimal === true;

    const current = el.value || "";
    const start = el.selectionStart ?? current.length;
    const end = el.selectionEnd ?? current.length;

    text = limitByMaxLength(current, start, end, text, maxLength);
    if (!text) return;

    let next = current.substring(0, start) + text + current.substring(end);

    if (allowDecimal) {
        next = fixDecimalPlaces(next, maxDecimalPlaces);
    }

    if (maxLength > 0 && next.length > maxLength) {
        next = next.substring(0, maxLength);
    }

    el.value = next;

    let newPos = start + text.length;
    if (newPos > next.length) newPos = next.length;

    try {
        el.setSelectionRange(newPos, newPos);
    } catch {
        // ignored
    }

    el.dispatchEvent(new Event("input", { bubbles: true }));
}

export function attachInputGuard(el, options) {
    if (!el) return;

    detachInputGuard(el);

    const safeOptions = {
        maxLength: Math.max(0, options?.maxLength ?? 0),
        allowDecimal: options?.allowDecimal === true,
        maxDecimalPlaces: Math.max(0, options?.maxDecimalPlaces ?? 0),
        threeTwoZero: options?.threeTwoZero === true,
        persianDate: options?.persianDate === true
    };

    const beforeInputHandler = (e) => {
        if (
            !e.data ||
            e.inputType?.startsWith("delete") ||
            e.inputType?.startsWith("history")
        ) {
            return;
        }

        const current = el.value || "";
        const start = el.selectionStart ?? current.length;
        const end = el.selectionEnd ?? current.length;

        const cleaned = normalizeText(e.data, safeOptions, current, start, end);

        if (!cleaned) {
            e.preventDefault();
            return;
        }

        const insertText = limitByMaxLength(current, start, end, cleaned, safeOptions.maxLength);

        if (!insertText) {
            e.preventDefault();
            return;
        }

        const predicted =
            current.substring(0, start) +
            insertText +
            current.substring(end);

        if (safeOptions.allowDecimal) {
            const fixed = fixDecimalPlaces(predicted, safeOptions.maxDecimalPlaces);

            if (fixed !== predicted || cleaned !== e.data) {
                e.preventDefault();
                insertAtCaret(el, insertText, safeOptions);
                return;
            }
        }

        if (cleaned !== e.data || insertText !== cleaned) {
            e.preventDefault();
            insertAtCaret(el, insertText, safeOptions);
        }
    };

    const keydownHandler = (e) => {
        if (!safeOptions.threeTwoZero) return;

        if (e.key === "+" || e.key === "Add" || e.code === "NumpadAdd") {
            e.preventDefault();
            insertAtCaret(el, "000", safeOptions);
            return;
        }

        if (e.key === "-" || e.key === "Subtract" || e.code === "NumpadSubtract") {
            e.preventDefault();
            insertAtCaret(el, "00", safeOptions);
        }
    };

    const pasteHandler = (e) => {
        e.preventDefault();

        const current = el.value || "";
        const start = el.selectionStart ?? current.length;
        const end = el.selectionEnd ?? current.length;

        const raw = (e.clipboardData || window.clipboardData)?.getData("text") || "";
        const cleaned = normalizeText(raw, safeOptions, current, start, end);

        if (cleaned) {
            insertAtCaret(el, cleaned, safeOptions);
        }
    };

    const dropHandler = (e) => {
        e.preventDefault();

        const current = el.value || "";
        const start = el.selectionStart ?? current.length;
        const end = el.selectionEnd ?? current.length;

        const raw = e.dataTransfer?.getData("text") || "";
        const cleaned = normalizeText(raw, safeOptions, current, start, end);

        if (cleaned) {
            insertAtCaret(el, cleaned, safeOptions);
        }
    };

    el.addEventListener("beforeinput", beforeInputHandler, { passive: false });
    el.addEventListener("keydown", keydownHandler);
    el.addEventListener("paste", pasteHandler);
    el.addEventListener("drop", dropHandler);

    guardMap.set(el, {
        beforeInputHandler,
        keydownHandler,
        pasteHandler,
        dropHandler
    });
}

export function detachInputGuard(el) {
    if (!el) return;

    const old = guardMap.get(el);
    if (!old) return;

    el.removeEventListener("beforeinput", old.beforeInputHandler);
    el.removeEventListener("keydown", old.keydownHandler);
    el.removeEventListener("paste", old.pasteHandler);
    el.removeEventListener("drop", old.dropHandler);

    guardMap.delete(el);
}