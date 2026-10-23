// Image paste and drop for MarkdownEditor. Images are streamed to .NET, which uploads them and then
// calls insertAtCursor with the Markdown to add.

const isImage = file => file.type.startsWith("image/");

export function init(textarea, dotNet) {
    const send = files => {
        for (const file of files) {
            const name = file.name && file.name !== "image.png" ? file.name : `screenshot-${new Date().toISOString().slice(0, 10)}.png`;
            dotNet.invokeMethodAsync("OnImagePasted", DotNet.createJSStreamReference(file), name, file.size);
        }
    };

    const onPaste = event => {
        const files = [...(event.clipboardData?.files ?? [])].filter(isImage);
        if (files.length > 0) {
            event.preventDefault();
            send(files);
        }
    };

    const onDragOver = event => {
        if ([...(event.dataTransfer?.items ?? [])].some(item => item.kind === "file")) {
            event.preventDefault();
            textarea.classList.add("is-dropping");
        }
    };

    const onDragLeave = () => textarea.classList.remove("is-dropping");

    const onDrop = event => {
        textarea.classList.remove("is-dropping");
        const files = [...(event.dataTransfer?.files ?? [])].filter(isImage);
        if (files.length > 0) {
            event.preventDefault();
            send(files);
        }
    };

    textarea.addEventListener("paste", onPaste);
    textarea.addEventListener("dragover", onDragOver);
    textarea.addEventListener("dragleave", onDragLeave);
    textarea.addEventListener("drop", onDrop);

    return {
        dispose: () => {
            textarea.removeEventListener("paste", onPaste);
            textarea.removeEventListener("dragover", onDragOver);
            textarea.removeEventListener("dragleave", onDragLeave);
            textarea.removeEventListener("drop", onDrop);
        },
    };
}

// Puts text on its own line at the cursor (or the end), then tells Blazor the value changed.
export function insertAtCursor(textarea, text) {
    const start = textarea.selectionStart ?? textarea.value.length;
    const end = textarea.selectionEnd ?? start;
    const before = textarea.value.slice(0, start);
    const after = textarea.value.slice(end);
    const insert = `${before && !before.endsWith("\n") ? "\n" : ""}${text}\n`;

    textarea.value = before + insert + after;
    const caret = before.length + insert.length;
    textarea.setSelectionRange(caret, caret);
    textarea.dispatchEvent(new Event("input", { bubbles: true }));
    textarea.focus();
}
