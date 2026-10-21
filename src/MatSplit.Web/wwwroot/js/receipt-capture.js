/*
    MatSplit - receipt capture (Belegfoto)
    ---------------------------------------------------------------------------
    The field is the file upload of MatUi (.mat-upload, FORM-21) inside a [data-ms-receipt] field: the toolkit
    draws the drop zone, the chosen file with its thumbnail and remove button, and refuses a wrong type or a file
    above data-max-size in words. What is left here is what only MatSplit does:

      * Canvas compression of every chosen image: longest edge 1600 px, JPEG at falling quality until
        data-ms-target-kb is met. The smaller file replaces the original in the upload (MatUi.upload.add).
        An image the upload refused only for its size is compressed too, and added when it fits then.
        The original stays when compression does not help (or the image cannot be decoded, e.g. HEIC on
        desktop browsers). data-ms-compress="false" turns it off.
      * "Foto aufnehmen": the camera with a live picture in a MatUi dialog (getUserMedia); without a
        camera, or when access is denied, the file dialog opens instead (input capture="environment").

    Dependency free, ES5 syntax, works offline - no CDN, no bundler.
    The upload itself is a normal multipart form post to the razor page handler.
*/
(function () {
    'use strict';

    var MAX_EDGE = 1600;
    var JPEG_QUALITY = 0.8;
    var DEFAULT_TARGET_KB = 500;
    // Progressively lower JPEG quality until the target size is met.
    var QUALITY_STEPS = [0.85, 0.7, 0.55, 0.45, 0.35];
    var FIELD_SELECTOR = '[data-ms-receipt]';

    function qsa(selector, scope) {
        return Array.prototype.slice.call((scope || document).querySelectorAll(selector));
    }

    function upload() {
        return window.MatUi && window.MatUi.upload ? window.MatUi.upload : null;
    }

    function canCapture() {
        return !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia && window.File && upload());
    }

    function targetBytes(field) {
        var raw = parseInt(field.getAttribute('data-ms-target-kb'), 10);
        var kilobytes = isNaN(raw) || raw <= 0 ? DEFAULT_TARGET_KB : raw;
        return kilobytes * 1024;
    }

    function compressEnabled(field) {
        return field.getAttribute('data-ms-compress') !== 'false';
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

    /* ---------------------------- compression ---------------------------- */

    function compressImage(file, target, done) {
        if (!isImage(file) || !window.URL || !URL.createObjectURL) {
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

    /* ------------------------------ handling ----------------------------- */

    // The upload holds one file: adding the smaller one replaces the original (source "api", so it is not compressed again).
    function shrink(state, file) {
        if (!isImage(file) || !compressEnabled(state.field)) {
            return;
        }

        compressImage(file, targetBytes(state.field), function (compressed) {
            if (compressed) {
                upload().add(state.wrap, [compressed]);
            }
        });
    }

    /* ------------------------------- camera ------------------------------ */

    function button(kind, text, attribute) {
        return '<button type="button" class="mat-btn mat-btn--' + kind + '" ' + attribute + '>' + text + '</button>';
    }

    function openCamera(state) {
        var dialog = document.createElement('dialog');
        dialog.className = 'mat-dialog ms-camera';
        dialog.id = state.input.id + '-camera';
        dialog.setAttribute('aria-labelledby', dialog.id + '-title');
        dialog.setAttribute('data-static', '');
        dialog.innerHTML = '<div class="mat-dialog__head">'
            + '<h2 class="mat-dialog__title" id="' + dialog.id + '-title">Beleg aufnehmen</h2>'
            + '<button type="button" class="mat-btn mat-btn--ghost mat-btn--icon mat-dialog__close" data-dialog-close aria-label="Schließen">'
            + '<svg class="mat-icon" aria-hidden="true" focusable="false"><use href="#mat-i-x"></use></svg></button></div>'
            + '<div class="mat-dialog__body"><video class="ms-camera__video" autoplay playsinline muted></video></div>'
            + '<div class="mat-dialog__foot"><div class="mat-btn-row mat-btn-row--plain">'
            + button('primary', 'Aufnehmen', 'data-shoot')
            + button('secondary', 'Kamera wechseln', 'data-flip')
            + button('secondary', 'Abbrechen', 'data-dialog-close')
            + '</div></div>';

        document.body.appendChild(dialog);

        var video = dialog.querySelector('video');
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

        // However the dialog closes (a button, Escape, the script), the camera goes off and the dialog goes away.
        dialog.addEventListener('close', function () {
            stopStream();
            if (dialog.parentNode) {
                dialog.parentNode.removeChild(dialog);
            }
        });

        function start() {
            stopStream();
            navigator.mediaDevices.getUserMedia({ video: { facingMode: facing }, audio: false })
                .then(function (result) {
                    stream = result;
                    video.srcObject = stream;
                })
                .catch(function () {
                    window.MatUi.dialog.close(dialog);
                    // No camera permission: let the user pick a file instead.
                    state.input.click();
                });
        }

        dialog.querySelector('[data-flip]').addEventListener('click', function () {
            facing = facing === 'environment' ? 'user' : 'environment';
            start();
        });

        dialog.querySelector('[data-shoot]').addEventListener('click', function () {
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
                window.MatUi.dialog.close(dialog);
                state.input.click();
                return;
            }

            canvas.toBlob(function (blob) {
                window.MatUi.dialog.close(dialog);

                if (!blob) {
                    return;
                }

                upload().add(state.wrap, [new File([blob], 'beleg-' + Date.now() + '.jpg', {
                    type: 'image/jpeg',
                    lastModified: Date.now()
                })]);
            }, 'image/jpeg', JPEG_QUALITY);
        });

        window.MatUi.dialog.open(dialog);
        start();
    }

    function addCameraButton(state) {
        var camera = document.createElement('button');
        camera.type = 'button';
        camera.id = state.input.id + '-capture';
        camera.className = 'mat-btn mat-btn--secondary mat-btn--sm ms-camera-button';
        camera.innerHTML = '<svg class="mat-icon" aria-hidden="true" focusable="false"><use href="#ms-i-camera"></use></svg>';
        camera.appendChild(document.createTextNode('Foto aufnehmen'));
        camera.addEventListener('click', function () {
            if (!canCapture()) {
                state.input.click();
                return;
            }
            openCamera(state);
        });

        state.wrap.insertAdjacentElement('afterend', camera);
    }

    /* -------------------------------- boot ------------------------------- */

    function enhance(field) {
        if (field.getAttribute('data-ms-receipt-ready') === 'true') {
            return;
        }

        var wrap = field.querySelector('.mat-upload');
        var input = wrap ? wrap.querySelector('input[type="file"]') : null;
        if (!input || !upload()) {
            return;
        }

        field.setAttribute('data-ms-receipt-ready', 'true');

        var state = { field: field, wrap: wrap, input: input };

        wrap.addEventListener('mat:upload-change', function (event) {
            if (event.detail.source === 'api') {
                return;
            }
            event.detail.added.forEach(function (file) {
                shrink(state, file);
            });
        });

        // A photo straight from a phone camera is often above the limit; compressed it usually fits.
        wrap.addEventListener('mat:upload-refused', function (event) {
            event.detail.refused.forEach(function (entry) {
                if (entry.reason === 'size') {
                    shrink(state, entry.file);
                }
            });
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
