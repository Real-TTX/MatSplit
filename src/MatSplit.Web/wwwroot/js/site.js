/* ==========================================================================
   MatSplit front end. No dependencies, no CDN.
   Features: theme switch (localStorage + prefers-color-scheme), off canvas
   drawer, scrollbar visibility, progressive disclosure for ms-field,
   toolbar auto submit, confirm dialogs, tabs, alert dismiss, clipboard,
   camera capture fallback and client side validation for the data-val-*
   attributes rendered by ms-field.
   ========================================================================== */
(function () {
    'use strict';

    var THEME_KEY = 'matsplit.theme';
    var SCROLL_IDLE = 700;
    var root = document.documentElement;

    function qsa(selector, scope) {
        return Array.prototype.slice.call((scope || document).querySelectorAll(selector));
    }

    function darkMedia() {
        return window.matchMedia('(prefers-color-scheme: dark)');
    }

    /* ------------------------------- theme ------------------------------- */

    function currentThemeMode() {
        var stored = null;
        try {
            stored = window.localStorage.getItem(THEME_KEY);
        } catch (error) {
            stored = null;
        }
        return stored || root.getAttribute('data-theme-preference') || 'system';
    }

    function applyTheme(mode) {
        var dark = mode === 'dark' || (mode === 'system' && darkMedia().matches);
        root.setAttribute('data-theme', dark ? 'dark' : 'light');
        root.setAttribute('data-theme-mode', mode);
        qsa('[data-ms-theme]').forEach(function (button) {
            var active = button.getAttribute('data-ms-theme') === mode;
            button.setAttribute('aria-pressed', active ? 'true' : 'false');
        });
    }

    function persistTheme(mode) {
        try {
            window.localStorage.setItem(THEME_KEY, mode);
        } catch (error) {
            /* private mode, ignore */
        }

        var switcher = document.querySelector('[data-ms-theme-switcher]');
        var url = switcher ? switcher.getAttribute('data-ms-theme-url') : null;
        if (!url) {
            return;
        }

        var token = document.querySelector('input[name="__RequestVerificationToken"]');
        var headers = { 'Content-Type': 'application/x-www-form-urlencoded' };
        var body = 'theme=' + encodeURIComponent(mode);
        if (token) {
            headers.RequestVerificationToken = token.value;
            body += '&__RequestVerificationToken=' + encodeURIComponent(token.value);
        }

        window.fetch(url, { method: 'POST', headers: headers, body: body, credentials: 'same-origin' })
            .catch(function () { /* theme stays client side */ });
    }

    function setTheme(mode) {
        applyTheme(mode);
        persistTheme(mode);
    }

    function initTheme() {
        applyTheme(currentThemeMode());

        qsa('[data-ms-theme]').forEach(function (button) {
            button.addEventListener('click', function () {
                setTheme(button.getAttribute('data-ms-theme') || 'system');
            });
        });

        var media = darkMedia();
        var listener = function () {
            if (currentThemeMode() === 'system') {
                applyTheme('system');
            }
        };

        if (typeof media.addEventListener === 'function') {
            media.addEventListener('change', listener);
        } else if (typeof media.addListener === 'function') {
            media.addListener(listener);
        }
    }

    /* ------------------------------- drawer ------------------------------ */

    function setDrawer(open) {
        document.body.classList.toggle('ms-drawer-open', open);
        qsa('[data-ms-drawer-toggle]').forEach(function (button) {
            button.setAttribute('aria-expanded', open ? 'true' : 'false');
        });
        qsa('.ms-shell__overlay').forEach(function (overlay) {
            if (open) {
                overlay.removeAttribute('hidden');
            } else {
                overlay.setAttribute('hidden', 'hidden');
            }
        });

        if (open) {
            var firstLink = document.querySelector('#ms-menu .ms-nav__link');
            if (firstLink) {
                firstLink.focus({ preventScroll: true });
            }
        }
    }

    function initDrawer() {
        qsa('[data-ms-drawer-toggle]').forEach(function (button) {
            button.addEventListener('click', function () {
                setDrawer(!document.body.classList.contains('ms-drawer-open'));
            });
        });

        qsa('[data-ms-drawer-close]').forEach(function (element) {
            element.addEventListener('click', function () {
                setDrawer(false);
            });
        });

        document.addEventListener('keydown', function (event) {
            if (event.key === 'Escape' && document.body.classList.contains('ms-drawer-open')) {
                setDrawer(false);
            }
        });

        qsa('#ms-menu a').forEach(function (link) {
            link.addEventListener('click', function () {
                if (window.innerWidth < 900) {
                    setDrawer(false);
                }
            });
        });

        window.addEventListener('resize', function () {
            if (window.innerWidth >= 900) {
                setDrawer(false);
            }
        });
    }

    /* ---------------------------- scrollbars ----------------------------- */

    function initScrollbars() {
        var pageTimer = null;

        window.addEventListener('scroll', function () {
            root.classList.add('ms-scrolling');
            window.clearTimeout(pageTimer);
            pageTimer = window.setTimeout(function () {
                root.classList.remove('ms-scrolling');
            }, SCROLL_IDLE);
        }, { passive: true });

        qsa('[data-ms-scroll]').forEach(function (element) {
            var timer = null;
            element.addEventListener('scroll', function () {
                element.classList.add('is-scrolling');
                window.clearTimeout(timer);
                timer = window.setTimeout(function () {
                    element.classList.remove('is-scrolling');
                }, SCROLL_IDLE);
            }, { passive: true });
        });
    }

    /* ----------------------- progressive disclosure ---------------------- */

    function controlValue(control) {
        if (!control) {
            return '';
        }

        if (control.type === 'checkbox') {
            return control.checked ? 'true' : 'false';
        }

        if (control.type === 'radio') {
            var group = document.getElementsByName(control.name);
            for (var index = 0; index < group.length; index++) {
                if (group[index].checked) {
                    return group[index].value;
                }
            }
            return '';
        }

        return control.value || '';
    }

    function findMaster(field, name) {
        var scope = field.closest('form') || document;
        var byName = scope.querySelector('[name="' + name + '"]');
        if (byName) {
            return byName;
        }
        var byId = document.getElementById(name);
        return byId || null;
    }

    function matchesExpected(value, expected) {
        if (expected === '*') {
            return value !== '' && value !== 'false';
        }

        var wanted = expected.split(',');
        for (var index = 0; index < wanted.length; index++) {
            if (wanted[index].trim().toLowerCase() === String(value).trim().toLowerCase()) {
                return true;
            }
        }
        return false;
    }

    function toggleField(field, visible) {
        if (visible) {
            field.removeAttribute('hidden');
        } else {
            field.setAttribute('hidden', 'hidden');
        }

        qsa('input, select, textarea', field).forEach(function (control) {
            if (visible) {
                if (control.getAttribute('data-ms-was-required') === 'true') {
                    control.required = true;
                    control.removeAttribute('data-ms-was-required');
                }
                return;
            }

            if (control.required) {
                control.setAttribute('data-ms-was-required', 'true');
                control.required = false;
            }
        });
    }

    function initConditionals(scope) {
        qsa('[data-depends-on]', scope).forEach(function (field) {
            if (field.getAttribute('data-ms-bound') === 'true') {
                return;
            }
            field.setAttribute('data-ms-bound', 'true');

            var name = field.getAttribute('data-depends-on') || '';
            var expected = field.getAttribute('data-depends-value') || '*';
            var master = findMaster(field, name);

            if (!master) {
                field.removeAttribute('hidden');
                return;
            }

            var evaluate = function () {
                toggleField(field, matchesExpected(controlValue(master), expected));
            };

            master.addEventListener('change', evaluate);
            master.addEventListener('input', evaluate);

            if (master.type === 'radio' && master.name) {
                qsa('[name="' + master.name + '"]').forEach(function (radio) {
                    radio.addEventListener('change', evaluate);
                });
            }

            evaluate();
        });
    }

    /* --------------------------- toolbar submit -------------------------- */

    var FOCUS_KEY = 'matsplit.toolbarFocus';

    function rememberFocus(form, control) {
        if (!control || !control.name) {
            return;
        }
        try {
            window.sessionStorage.setItem(FOCUS_KEY, JSON.stringify({
                form: form.id || '',
                name: control.name,
                caret: typeof control.selectionStart === 'number' ? control.selectionStart : -1
            }));
        } catch (error) {
            /* ignore */
        }
    }

    function restoreFocus() {
        var raw = null;
        try {
            raw = window.sessionStorage.getItem(FOCUS_KEY);
            window.sessionStorage.removeItem(FOCUS_KEY);
        } catch (error) {
            return;
        }

        if (!raw) {
            return;
        }

        var state = null;
        try {
            state = JSON.parse(raw);
        } catch (error) {
            return;
        }

        var scope = state.form ? document.getElementById(state.form) : document;
        if (!scope) {
            return;
        }

        var control = scope.querySelector('[name="' + state.name + '"]');
        if (!control) {
            return;
        }

        control.focus({ preventScroll: true });
        if (state.caret >= 0 && typeof control.setSelectionRange === 'function') {
            try {
                control.setSelectionRange(state.caret, state.caret);
            } catch (error) {
                /* not a text input */
            }
        }
    }

    function initToolbars() {
        qsa('form[data-ms-autosubmit]').forEach(function (form) {
            var withText = form.getAttribute('data-ms-autosubmit-text') === 'true';
            var timer = null;

            form.addEventListener('change', function (event) {
                var target = event.target;
                if (!target || target.tagName === 'BUTTON') {
                    return;
                }
                if (target.type === 'text' || target.type === 'search') {
                    return;
                }
                rememberFocus(form, target);
                form.requestSubmit ? form.requestSubmit() : form.submit();
            });

            if (!withText) {
                return;
            }

            form.addEventListener('input', function (event) {
                var target = event.target;
                if (!target || (target.type !== 'text' && target.type !== 'search')) {
                    return;
                }
                window.clearTimeout(timer);
                timer = window.setTimeout(function () {
                    rememberFocus(form, target);
                    form.requestSubmit ? form.requestSubmit() : form.submit();
                }, 500);
            });
        });

        restoreFocus();
    }

    /* ------------------------------ confirm ------------------------------ */

    function initConfirm() {
        document.addEventListener('click', function (event) {
            var trigger = event.target.closest ? event.target.closest('[data-ms-confirm]') : null;
            if (!trigger) {
                return;
            }

            var question = trigger.getAttribute('data-ms-confirm');
            if (question && !window.confirm(question)) {
                event.preventDefault();
                event.stopPropagation();
            }
        });
    }

    /* -------------------------------- tabs ------------------------------- */

    function activateTab(container, button) {
        var targetId = button.getAttribute('data-ms-tab-target');
        if (!targetId) {
            return;
        }

        qsa('.ms-tabs__tab[data-ms-tab-target]', container).forEach(function (tab) {
            var active = tab === button;
            tab.classList.toggle('is-active', active);
            tab.setAttribute('aria-selected', active ? 'true' : 'false');
            if (active) {
                tab.removeAttribute('tabindex');
            } else {
                tab.setAttribute('tabindex', '-1');
            }
        });

        qsa('.ms-tabs__panel', container).forEach(function (panel) {
            var active = panel.id === targetId;
            panel.classList.toggle('is-active', active);
            if (active) {
                panel.removeAttribute('hidden');
            } else {
                panel.setAttribute('hidden', 'hidden');
            }
        });

        if (container.getAttribute('data-ms-tabs-remember') === 'true' && container.id) {
            try {
                window.sessionStorage.setItem('matsplit.tab.' + container.id, targetId);
            } catch (error) {
                /* ignore */
            }
        }
    }

    function initTabs() {
        qsa('[data-ms-tabs]').forEach(function (container) {
            var buttons = qsa('.ms-tabs__tab[data-ms-tab-target]', container);

            buttons.forEach(function (button, index) {
                button.addEventListener('click', function () {
                    activateTab(container, button);
                });

                button.addEventListener('keydown', function (event) {
                    var offset = event.key === 'ArrowRight' ? 1 : (event.key === 'ArrowLeft' ? -1 : 0);
                    if (offset === 0) {
                        return;
                    }
                    event.preventDefault();
                    var next = buttons[(index + offset + buttons.length) % buttons.length];
                    next.focus();
                    activateTab(container, next);
                });
            });

            if (container.getAttribute('data-ms-tabs-remember') !== 'true' || !container.id) {
                return;
            }

            var stored = null;
            try {
                stored = window.sessionStorage.getItem('matsplit.tab.' + container.id);
            } catch (error) {
                stored = null;
            }

            if (!stored) {
                return;
            }

            buttons.forEach(function (button) {
                if (button.getAttribute('data-ms-tab-target') === stored) {
                    activateTab(container, button);
                }
            });
        });
    }

    /* ------------------------- dismiss and clipboard --------------------- */

    function initDismiss() {
        qsa('[data-ms-dismiss]').forEach(function (button) {
            button.addEventListener('click', function () {
                var target = document.getElementById(button.getAttribute('data-ms-dismiss'));
                if (target) {
                    target.setAttribute('hidden', 'hidden');
                    target.style.display = 'none';
                }
            });
        });
    }

    function initClipboard() {
        qsa('[data-ms-copy]').forEach(function (button) {
            button.addEventListener('click', function () {
                var raw = button.getAttribute('data-ms-copy') || '';
                var text = raw;

                if (raw.charAt(0) === '#') {
                    var source = document.getElementById(raw.substring(1));
                    if (source) {
                        text = typeof source.value === 'string' && source.value !== ''
                            ? source.value
                            : (source.textContent || '').trim();
                    }
                }

                if (!text) {
                    return;
                }

                var label = button.querySelector('.ms-btn__label');
                var done = function () {
                    if (!label) {
                        return;
                    }
                    var original = label.textContent;
                    label.textContent = 'Kopiert';
                    window.setTimeout(function () {
                        label.textContent = original;
                    }, 1600);
                };

                if (navigator.clipboard && navigator.clipboard.writeText) {
                    navigator.clipboard.writeText(text).then(done).catch(function () { });
                    return;
                }

                var helper = document.createElement('textarea');
                helper.value = text;
                helper.setAttribute('readonly', 'readonly');
                helper.style.position = 'absolute';
                helper.style.left = '-9999px';
                document.body.appendChild(helper);
                helper.select();
                try {
                    document.execCommand('copy');
                    done();
                } catch (error) {
                    /* ignore */
                }
                document.body.removeChild(helper);
            });
        });
    }

    /* -------------------------------- share ------------------------------ */

    // Native Web Share on capable devices (mobile / installed PWA), otherwise
    // open WhatsApp with a prefilled message. Used for anonymous invite links.
    function initShare() {
        qsa('[data-ms-share]').forEach(function (button) {
            button.addEventListener('click', function () {
                var url = button.getAttribute('data-ms-share-url') || '';

                if (url.charAt(0) === '#') {
                    var source = document.getElementById(url.substring(1));
                    if (source) {
                        url = typeof source.value === 'string' && source.value !== ''
                            ? source.value
                            : (source.textContent || '').trim();
                    }
                }

                if (url && url.indexOf('http') !== 0) {
                    try {
                        url = new URL(url, window.location.origin).href;
                    } catch (error) {
                        /* keep as-is */
                    }
                }

                var title = button.getAttribute('data-ms-share-title') || document.title;
                var text = button.getAttribute('data-ms-share-text') || '';

                if (navigator.share) {
                    navigator.share({ title: title, text: text, url: url }).catch(function () { });
                    return;
                }

                var message = (text ? text + ' ' : '') + url;
                window.open('https://wa.me/?text=' + encodeURIComponent(message.trim()), '_blank', 'noopener');
            });
        });
    }

    /* ------------------------------- camera ------------------------------ */

    function openCamera(input) {
        var overlay = document.createElement('div');
        overlay.className = 'ms-camera';
        overlay.innerHTML = '<div class="ms-camera__box">' +
            '<video class="ms-camera__video" autoplay playsinline muted></video>' +
            '<div class="ms-camera__actions">' +
            '<button type="button" class="ms-btn ms-btn--primary" data-ms-camera-shoot>Aufnehmen</button>' +
            '<button type="button" class="ms-btn ms-btn--secondary" data-ms-camera-cancel>Abbrechen</button>' +
            '</div></div>';
        document.body.appendChild(overlay);

        var video = overlay.querySelector('video');
        var stream = null;

        var stop = function () {
            if (stream) {
                stream.getTracks().forEach(function (track) {
                    track.stop();
                });
            }
            if (overlay.parentNode) {
                overlay.parentNode.removeChild(overlay);
            }
        };

        overlay.querySelector('[data-ms-camera-cancel]').addEventListener('click', stop);

        overlay.querySelector('[data-ms-camera-shoot]').addEventListener('click', function () {
            var canvas = document.createElement('canvas');
            canvas.width = video.videoWidth || 1280;
            canvas.height = video.videoHeight || 960;
            canvas.getContext('2d').drawImage(video, 0, 0, canvas.width, canvas.height);

            canvas.toBlob(function (blob) {
                if (blob && window.DataTransfer && window.File) {
                    var file = new File([blob], 'beleg-' + Date.now() + '.jpg', { type: 'image/jpeg' });
                    var transfer = new DataTransfer();
                    transfer.items.add(file);
                    input.files = transfer.files;
                    input.dispatchEvent(new Event('change', { bubbles: true }));
                }
                stop();
            }, 'image/jpeg', 0.9);
        });

        navigator.mediaDevices.getUserMedia({ video: { facingMode: 'environment' }, audio: false })
            .then(function (result) {
                stream = result;
                video.srcObject = stream;
            })
            .catch(function () {
                stop();
                input.click();
            });
    }

    function initCamera() {
        qsa('[data-ms-camera]').forEach(function (button) {
            button.addEventListener('click', function () {
                var input = document.getElementById(button.getAttribute('data-ms-camera'));
                if (!input) {
                    return;
                }

                var supported = navigator.mediaDevices && navigator.mediaDevices.getUserMedia
                    && window.DataTransfer && window.File;

                if (!supported) {
                    input.click();
                    return;
                }

                openCamera(input);
            });
        });
    }

    /* ----------------------------- validation ---------------------------- */

    function messageTarget(form, name) {
        return form.querySelector('[data-valmsg-for="' + name + '"]');
    }

    function showError(control, form, message) {
        control.classList.add('input-validation-error');
        var target = messageTarget(form, control.name);
        if (target) {
            target.classList.remove('field-validation-valid');
            target.classList.add('field-validation-error');
            target.textContent = message;
        }
    }

    function clearError(control, form) {
        control.classList.remove('input-validation-error');
        var target = messageTarget(form, control.name);
        if (target) {
            target.classList.add('field-validation-valid');
            target.classList.remove('field-validation-error');
            target.textContent = '';
        }
    }

    function ruleMessage(control, rule, fallback) {
        return control.getAttribute('data-val-' + rule) || fallback;
    }

    function validateControl(control, form) {
        if (control.disabled || control.type === 'hidden') {
            return null;
        }

        // A cleared checkbox posts "false" through its hidden companion field, so
        // the required rule that asp.net emits for non nullable bools never applies.
        if (control.type === 'checkbox') {
            return null;
        }

        // Fields bound with for="..." carry data-val rules; fields with a plain
        // required attribute are still checked (native bubbles are switched off).
        var hasRules = control.getAttribute('data-val') === 'true';
        if (!hasRules && !control.required) {
            return null;
        }

        var field = control.closest('.ms-field');
        if (field && field.hasAttribute('hidden')) {
            return null;
        }

        var trimmed = (control.value || '').trim();

        if ((control.hasAttribute('data-val-required') || control.required) && trimmed === '') {
            return ruleMessage(control, 'required', 'Dieses Feld ist erforderlich.');
        }

        if (trimmed === '') {
            return null;
        }

        if (control.type === 'email' && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(trimmed)) {
            return ruleMessage(control, 'email', 'Bitte eine gültige E-Mail-Adresse eingeben.');
        }

        if (!hasRules) {
            return null;
        }

        var maxLength = control.getAttribute('data-val-length-max') || control.getAttribute('data-val-maxlength-max');
        if (maxLength && trimmed.length > parseInt(maxLength, 10)) {
            return ruleMessage(control, 'length', ruleMessage(control, 'maxlength', 'Der Wert ist zu lang.'));
        }

        var minLength = control.getAttribute('data-val-length-min') || control.getAttribute('data-val-minlength-min');
        if (minLength && trimmed.length < parseInt(minLength, 10)) {
            return ruleMessage(control, 'length', ruleMessage(control, 'minlength', 'Der Wert ist zu kurz.'));
        }

        if (control.hasAttribute('data-val-number') && isNaN(Number(trimmed.replace(',', '.')))) {
            return ruleMessage(control, 'number', 'Bitte eine Zahl eingeben.');
        }

        var rangeMin = control.getAttribute('data-val-range-min');
        var rangeMax = control.getAttribute('data-val-range-max');
        if (rangeMin !== null || rangeMax !== null) {
            var numeric = Number(trimmed.replace(',', '.'));
            if (!isNaN(numeric)) {
                if (rangeMin !== null && numeric < Number(rangeMin)) {
                    return ruleMessage(control, 'range', 'Der Wert ist zu klein.');
                }
                if (rangeMax !== null && numeric > Number(rangeMax)) {
                    return ruleMessage(control, 'range', 'Der Wert ist zu gro\u00df.');
                }
            }
        }

        var pattern = control.getAttribute('data-val-regex-pattern');
        if (pattern && !new RegExp('^(?:' + pattern + ')$').test(trimmed)) {
            return ruleMessage(control, 'regex', 'Das Format ist ung\u00fcltig.');
        }

        if (control.hasAttribute('data-val-email') && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(trimmed)) {
            return ruleMessage(control, 'email', 'Bitte eine g\u00fcltige E-Mail-Adresse eingeben.');
        }

        var other = control.getAttribute('data-val-equalto-other');
        if (other) {
            var otherName = other.replace(/^\*\./, '');
            var partner = form.querySelector('[name="' + otherName + '"]');
            if (partner && partner.value !== control.value) {
                return ruleMessage(control, 'equalto', 'Die Werte stimmen nicht \u00fcberein.');
            }
        }

        return null;
    }

    function updateSummary(form, messages) {
        var summary = form.querySelector('[data-valmsg-summary]');
        if (!summary) {
            return;
        }

        var list = summary.querySelector('ul');
        if (!list) {
            list = document.createElement('ul');
            summary.appendChild(list);
        }

        list.innerHTML = '';
        messages.forEach(function (message) {
            var item = document.createElement('li');
            item.textContent = message;
            list.appendChild(item);
        });

        summary.classList.toggle('validation-summary-errors', messages.length > 0);
        summary.classList.toggle('validation-summary-valid', messages.length === 0);
    }

    function validateForm(form) {
        var messages = [];
        var firstInvalid = null;

        qsa('input, select, textarea', form).forEach(function (control) {
            if (!control.name) {
                return;
            }

            var message = validateControl(control, form);
            if (message) {
                showError(control, form, message);
                messages.push(message);
                if (!firstInvalid) {
                    firstInvalid = control;
                }
                return;
            }

            clearError(control, form);
        });

        updateSummary(form, messages);

        if (firstInvalid) {
            firstInvalid.focus();
        }

        return messages.length === 0;
    }

    function initValidation(scope) {
        qsa('form[data-ms-form]', scope).forEach(function (form) {
            if (form.getAttribute('data-ms-validated') === 'true'
                || form.getAttribute('data-ms-novalidate') === 'true') {
                return;
            }
            form.setAttribute('data-ms-validated', 'true');

            form.addEventListener('submit', function (event) {
                if (!validateForm(form)) {
                    event.preventDefault();
                }
            });

            qsa('input, select, textarea', form).forEach(function (control) {
                control.addEventListener('blur', function () {
                    if (!control.name) {
                        return;
                    }
                    var message = validateControl(control, form);
                    if (message) {
                        showError(control, form, message);
                        return;
                    }
                    clearError(control, form);
                });
            });
        });
    }

    /* -------------------------------- boot ------------------------------- */

    function init() {
        initTheme();
        initDrawer();
        initScrollbars();
        initConditionals(document);
        initToolbars();
        initConfirm();
        initTabs();
        initDismiss();
        initClipboard();
        initShare();
        initCamera();
        initValidation(document);
    }

    window.MatSplit = {
        init: init,
        initValidation: function (scope) {
            initValidation(scope || document);
        },
        initConditionals: function (scope) {
            initConditionals(scope || document);
        },
        setTheme: setTheme,
        validateForm: validateForm
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
