window.reklamacijePriloge = {
    preparedImages: new Map(),

    storePreparedImage: function (blob, contentType) {
        const id = `${Date.now()}-${Math.random().toString(16).slice(2)}`;
        const extension = contentType === 'image/jpeg' ? 'jpg' : 'png';
        const fileName = `paste-${new Date().toISOString().replace(/[:.]/g, '-')}.${extension}`;
        this.preparedImages.set(id, { blob, fileName, contentType: contentType || 'image/png', size: blob.size || 0 });
        return { id, fileName, contentType: contentType || 'image/png', size: blob.size || 0, error: null };
    },

    getPreparedImage: function (id) {
        const item = window.reklamacijePriloge.preparedImages.get(id);
        return item ? item.blob : null;
    },

    releasePreparedImage: function (id) {
        window.reklamacijePriloge.preparedImages.delete(id);
    },

    focusPasteArea: function (element) {
        if (element && element.focus) {
            element.focus();
        }
    },

    prepareImageFromClipboard: async function () {
        if (!navigator.clipboard || !navigator.clipboard.read) {
            return { error: 'clipboard-read-not-supported' };
        }

        if (!window.isSecureContext) {
            return { error: 'Branje odložišča deluje samo v varnem kontekstu (localhost ali https).' };
        }

        try {
            const clipboardItems = await navigator.clipboard.read();
            for (const item of clipboardItems) {
                const imageType = item.types.find(type => type && type.startsWith('image/'));
                if (!imageType) {
                    continue;
                }

                const blob = await item.getType(imageType);
                return window.reklamacijePriloge.storePreparedImage(blob, imageType);
            }

            return { error: 'V odložišču ni slike.' };
        } catch (error) {
            return {
                error: error && error.message
                    ? `Odložišča ni mogoče prebrati: ${error.message}`
                    : 'Odložišča ni mogoče prebrati. Najprej kopiraj sliko in pritisni gumb Prilepi.'
            };
        }
    },

    readImageFromClipboard: async function (dotNetRef) {
        const image = await window.reklamacijePriloge.prepareImageFromClipboard();
        if (image.error) {
            return image.error;
        }

        await dotNetRef.invokeMethodAsync('OnPasteImagePreparedAsync', image.id, image.fileName, image.contentType, image.size);
        return null;
    },

    sendBlobToDotNet: function (dotNetRef, blob, contentType) {
        return new Promise((resolve) => {
            const reader = new FileReader();
            reader.onload = async function () {
                try {
                    const dataUrl = reader.result || '';
                    const base64 = dataUrl.toString().split(',')[1] || '';
                    const extension = contentType === 'image/jpeg' ? 'jpg' : 'png';
                    const fileName = `paste-${new Date().toISOString().replace(/[:.]/g, '-')}.${extension}`;
                    await dotNetRef.invokeMethodAsync('OnPasteImageAsync', fileName, contentType || 'image/png', base64);
                    resolve(null);
                } catch (error) {
                    resolve(error && error.message ? error.message : 'Slike ni mogoče shraniti.');
                }
            };
            reader.onerror = function () {
                resolve('Slike iz odložišča ni mogoče prebrati.');
            };
            reader.readAsDataURL(blob);
        });
    },

    registerPaste: function (element, dotNetRef) {
        if (!element) {
            return null;
        }

        const handler = async function (event) {
            const items = event.clipboardData && event.clipboardData.items ? Array.from(event.clipboardData.items) : [];
            const imageItem = items.find(item => item.type && item.type.startsWith('image/'));
            if (!imageItem) {
                return;
            }

            const file = imageItem.getAsFile();
            if (!file) {
                return;
            }

            event.preventDefault();
            try {
                const image = window.reklamacijePriloge.storePreparedImage(file, file.type || 'image/png');
                await dotNetRef.invokeMethodAsync('OnPasteImagePreparedAsync', image.id, image.fileName, image.contentType, image.size);
            } catch (error) {
                await dotNetRef.invokeMethodAsync('SetPasteErrorAsync', error && error.message ? error.message : 'Slike ni mogoče prilepiti.');
            }
        };

        element.addEventListener('paste', handler);
        return {
            dispose: function () {
                element.removeEventListener('paste', handler);
            }
        };
    }
};
