/*
    MatSplit - receipt capture (Belegfoto)
    ---------------------------------------------------------------------------
    Enhances every file input inside a [data-ms-receipt] container:

      * "Foto aufnehmen" button using getUserMedia with a live video preview,
        automatic fallback to the plain file dialog (input capture="environment")
        when the browser or the user denies camera access.
      * Canvas compression of every selected or captured image: longest edge
        1600 px, JPEG quality 0.8. Keeps the original file when compression
        would not help (or when the image cannot be decoded, e.g. HEIC on
        desktop browsers).
      * Live thumbnail preview with a "Entfernen" button.
      * Client side size check against data-ms-max-mb (AppConfig.MaxReceiptSizeMb),
        so an oversized photo is rejected before it travels over the network.

    Dependency free, ES5 syntax, works offline - no CDN, no bundler.
    The upload itself is a normal multipart form post to the razor page handler.
*/
(function () {
    'use strict';

    var MAX_EDGE = 1600;
    var JPEG_QUALITY = 0.8;
    var DEFAULT_MAX_MB = 10;
    var DEFAULT_TARGET_KB = 500;
    // Progressively lower JPEG quality until the target size is met.
    var QUALITY_STEPS = [0.85, 0.7, 0.55, 0.45, 0.35];
    var FIELD_SELECTOR = '[data-ms-receipt]';

    function qsa(selector, scope) {
        return Array.prototype.slice.call((scope || document).querySelectorAll(selector));
    }

    function canReplaceFiles() {
        return !!(window.DataTransfer && window.File && window.FileList);
    }

    function canCapture() {
        return !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia) && canReplaceFiles();
    }

    function maxBytes(field) {
        var raw = parseInt(field.getAttribute('data-ms-max-mb'), 10);
        var megabytes = isNaN(raw) || raw <= 0 ? DEFAULT_MAX_MB : raw;
        return megabytes * 1024 * 1024;
    }

    function targetBytes(field) {
        var raw = parseInt(field.getAttribute('data-ms-target-kb'), 10);
        var kilobytes = isNaN(raw) || raw <= 0 ? DEFAULT_TARGET_KB : raw;
        return kilobytes * 1024;
    }

    function compressEnabled(field) {
        return field.getAttribute('data-ms-compress') !== 'false';
    }

    function formatSize(bytes) {
        if (bytes >= 1024 * 1024) {
            return (bytes / (1024 * 1024)).toFixed(1).replace('.', ',') + ' MB';
        }
        if (bytes >= 1024) {
            return Math.round(bytes / 1024) + ' KB';
        }
        return bytes + ' B';
    }

    function isImage(file) {
        return !!file && typeof file.type === 'string' && file.type.indexOf('image/') === 0;
    }

    function jpegName(name) {
        var base = (name || 'beleg').replace(/\.[^.]+$/, '');
        if (!base) {
            base = 'beleg';
        }
        return base + '.jpg';
    }

    /* ------------------------------ messages ----------------------------- */

    function errorTarget(field) {
        return field.querySelector('.ms-field__error');
    }

    function setError(field, message) {
        var target = errorTarget(field);
        if (!target) {
            return;
        }
        target.classList.remove('field-validation-valid');
        target.classList.add('field-validation-error');
        target.textContent = message;
    }

    function clearError(field) {
        var target = errorTarget(field);
        if (!target) {
            return;
        }
        target.classList.add('field-validation-valid');
        target.classList.remove('field-validation-error');
        target.textContent = '';
    }

    /* ------------------------------ preview ------------------------------ */

    function previewBox(field) {
        var box = field.querySelector('[data-ms-receipt-preview]');
        if (box) {
            return box;
        }

        box = document.createElement('div');
        box.className = 'ms-row';
        box.setAttribute('data-ms-receipt-preview', 'true');
        box.hidden = true;
        field.appendChild(box);
        return box;
    }

    function clearPreview(state) {
        var box = previewBox(state.field);
        if (state.previewUrl) {
            URL.revokeObjectURL(state.previewUrl);
            state.previewUrl = null;
        }
        box.innerHTML = '';
        box.hidden = true;
    }

    function renderPreview(state, file) {
        var box = previewBox(state.field);
        clearPreview(state);

        if (!file) {
            return;
        }

        if (isImage(file) && window.URL && URL.createObjectURL) {
            state.previewUrl = URL.createObjectURL(file);
            var image = document.createElement('img');
            image.setAttribute('width', '96');
            image.setAttribute('loading', 'lazy');
            image.setAttribute('decoding', 'async');
            image.alt = 'Vorschau des Belegs';
            image.src = state.previewUrl;
            box.appendChild(image);
        } else {
            var chip = document.createElement('span');
            chip.className = 'ms-chip';
            chip.textContent = 'Datei';
            box.appendChild(chip);
        }

        var caption = document.createElement('span');
        caption.className = 'ms-muted';
        caption.textContent = (file.name || 'Beleg') + ' · ' + formatSize(file.size);
        box.appendChild(caption);

        var remove = document.createElement('button');
        remove.type = 'button';
        remove.className = 'ms-btn ms-btn--ghost ms-btn--sm';
        remove.textContent = 'Entfernen';
        remove.addEventListener('click', function () {
            state.input.value = '';
            clearError(state.field);
            clearPreview(state);
        });
        box.appendChild(remove);

        box.hidden = false;
    }

    /* ---------------------------- compression ---------------------------- */

    function compressImage(file, target, done) {
        if (!isImage(file) || !canReplaceFiles() || !window.URL || !URL.createObjectURL) {
            done(null);
            return;
        }

        var url = URL.createObjectURL(file);
        var image = new Image();

        image.onload = function () {
            var longestEdge = Math.max(image.naturalWidth || image.width, image.naturalHeight || image.height);
            if (!longestEdge) {
                URL.revokeObjectURL(url);
                done(null);
                return;
            }

            var scale = Math.min(1, MAX_EDGE / longestEdge);
            var width = Math.max(1, Math.round((image.naturalWidth || image.width) * scale));
            var height = Math.max(1, Math.round((image.naturalHeight || image.height) * scale));

            var canvas = document.createElement('canvas');
            canvas.width = width;
            canvas.height = height;

            var context = canvas.getContext('2d');
            if (!context || !canvas.toBlob) {
                URL.revokeObjectURL(url);
                done(null);
                return;
            }

            context.drawImage(image, 0, 0, width, height);
            URL.revokeObjectURL(url);

            // Encode at falling quality until the target size is met (or we run
            // out of steps). The smallest acceptable result wins; if even that is
            // no better than the original the caller keeps the original file.
            var step = 0;

            function encode() {
                canvas.toBlob(function (blob) {
                    if (!blob) {
                        done(null);
                        return;
                    }

                    var underTarget = blob.size <= target;
                    var lastStep = step >= QUALITY_STEPS.length - 1;

                    if (underTarget || lastStep) {
                        if (blob.size >= file.size) {
                            // Re-encoding did not shrink anything worthwhile.
                            done(null);
                            return;
                        }

                        done(new File([blob], jpegName(file.name), {
                            type: 'image/jpeg',
                            lastModified: Date.now()
                        }));
                        return;
                    }

                    step++;
                    encode();
                }, 'image/jpeg', QUALITY_STEPS[step]);
            }

            encode();
        };

        image.onerror = function () {
            URL.revokeObjectURL(url);
            done(null);
        };

        image.src = url;
    }

    function assignFile(input, file) {
        if (!canReplaceFiles()) {
            return false;
        }

        var transfer = new DataTransfer();
        transfer.items.add(file);
        input.files = transfer.files;
        return true;
    }

    /* ------------------------------ handling ----------------------------- */

    function accept(state, file) {
        if (file.size > maxBytes(state.field)) {
            setError(state.field, 'Die Datei ist zu groß (' + formatSize(file.size)
                + '). Erlaubt sind maximal ' + formatSize(maxBytes(state.field)) + '.');
            state.input.value = '';
            clearPreview(state);
            return;
        }

        clearError(state.field);
        renderPreview(state, file);
    }

    function handleSelection(state) {
        var file = state.input.files && state.input.files.length > 0 ? state.input.files[0] : null;

        if (!file) {
            clearError(state.field);
            clearPreview(state);
            return;
        }

        if (!isImage(file) || !compressEnabled(state.field)) {
            accept(state, file);
            return;
        }

        compressImage(file, targetBytes(state.field), function (compressed) {
            if (compressed && assignFile(state.input, compressed)) {
                accept(state, compressed);
                return;
            }

            accept(state, file);
        });
    }

    /* ------------------------------- camera ------------------------------ */

    function openCamera(state) {
        var overlay = document.createElement('div');
        overlay.className = 'ms-camera';
        overlay.innerHTML = '<div class="ms-camera__box">'
            + '<video class="ms-camera__video" autoplay playsinline muted></video>'
            + '<div class="ms-camera__actions">'
            + '<button type="button" class="ms-btn ms-btn--primary" data-shoot>Aufnehmen</button>'
            + '<button type="button" class="ms-btn ms-btn--secondary" data-flip>Kamera wechseln</button>'
            + '<button type="button" class="ms-btn ms-btn--ghost" data-cancel>Abbrechen</button>'
            + '</div></div>';

        document.body.appendChild(overlay);

        var video = overlay.querySelector('video');
        var stream = null;
        var facing = 'environment';

        function stopStream() {
            if (!stream) {
                return;
            }
            stream.getTracks().forEach(function (track) {
                track.stop();
            });
            stream = null;
        }

        function close() {
            stopStream();
            document.removeEventListener('keydown', onKeyDown);
            if (overlay.parentNode) {
                overlay.parentNode.removeChild(overlay);
            }
        }

        function onKeyDown(event) {
            if (event.key === 'Escape') {
                close();
            }
        }

        function start() {
            stopStream();
            navigator.mediaDevices.getUserMedia({ video: { facingMode: facing }, audio: false })
                .then(function (result) {
                    stream = result;
                    video.srcObject = stream;
                })
                .catch(function () {
                    close();
                    // No camera permission: let the user pick a file instead.
                    state.input.click();
                });
        }

        overlay.querySelector('[data-cancel]').addEventListener('click', close);

        overlay.querySelector('[data-flip]').addEventListener('click', function () {
            facing = facing === 'environment' ? 'user' : 'environment';
            start();
        });

        overlay.querySelector('[data-shoot]').addEventListener('click', function () {
            var sourceWidth = video.videoWidth || 1280;
            var sourceHeight = video.videoHeight || 960;
            var scale = Math.min(1, MAX_EDGE / Math.max(sourceWidth, sourceHeight));
            var width = Math.max(1, Math.round(sourceWidth * scale));
            var height = Math.max(1, Math.round(sourceHeight * scale));

            var canvas = document.createElement('canvas');
            canvas.width = width;
            canvas.height = height;
            canvas.getContext('2d').drawImage(video, 0, 0, width, height);

            if (!canvas.toBlob) {
                close();
                state.input.click();
                return;
            }

            canvas.toBlob(function (blob) {
                close();

                if (!blob) {
                    return;
                }

                var file = new File([blob], 'beleg-' + Date.now() + '.jpg', {
                    type: 'image/jpeg',
                    lastModified: Date.now()
                });

                if (assignFile(state.input, file)) {
                    accept(state, file);
                }
            }, 'image/jpeg', JPEG_QUALITY);
        });

        document.addEventListener('keydown', onKeyDown);
        start();
    }

    function addCameraButton(state) {
        var control = state.field.querySelector('.ms-field__control') || state.field;

        var button = document.createElement('button');
        button.type = 'button';
        button.id = state.input.id + '-capture';
        button.className = 'ms-btn ms-btn--ghost ms-btn--sm ms-field__camera';
        button.textContent = 'Foto aufnehmen';
        button.addEventListener('click', function () {
            if (!canCapture()) {
                state.input.click();
                return;
            }
            openCamera(state);
        });

        control.appendChild(button);
    }

    /* -------------------------------- boot ------------------------------- */

    function enhance(field) {
        if (field.getAttribute('data-ms-receipt-ready') === 'true') {
            return;
        }

        var input = field.querySelector('input[type="file"]');
        if (!input) {
            return;
        }

        field.setAttribute('data-ms-receipt-ready', 'true');

        var state = {
            field: field,
            input: input,
            previewUrl: null
        };

        input.addEventListener('change', function () {
            handleSelection(state);
        });

        addCameraButton(state);
    }

    function init(scope) {
        qsa(FIELD_SELECTOR, scope).forEach(enhance);
    }

    window.MatSplitReceipts = {
        init: function (scope) {
            init(scope || document);
        }
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () {
            init(document);
        });
    } else {
        init(document);
    }
})();
