/* ==========================================================================
   MatSplit front end. No dependencies, no CDN.

   The shell and the controls (theme button, drawer, dialogs, confirm, tabs, validation, dependent
   fields, clipboard, toasts) are MatUi (matui/matui.js). What is left here is what only MatSplit does:
   the colour of the system bars, the installed app on iOS and Android, the Web Share button and the
   comma in money fields (the camera for the receipt is receipt-capture.js).
   ========================================================================== */
(function () {
    'use strict';

    var root = document.documentElement;

    function qsa(selector, scope) {
        return Array.prototype.slice.call((scope || document).querySelectorAll(selector));
    }

    /* --------------------------- system bar colour ----------------------- */

    /*
        theme-color is the colour of the top bar of the shell (--mat-glass), so that the status bar of an
        installed app and the browser bar are one surface with it, in the theme that is really shown. The
        user can choose Light or Dark against the system, which a media query cannot know: after every
        change of the theme the colour is read from the page and written into the tags.
    */
    function syncThemeColor() {
        var color = window.getComputedStyle(root).getPropertyValue('--mat-glass').trim();
        if (!color) {
            return;
        }

        qsa('meta[name="theme-color"]').forEach(function (meta) {
            meta.setAttribute('content', color);
            meta.removeAttribute('media');
        });
    }

    function initThemeColor() {
        syncThemeColor();
        root.addEventListener('mat:theme', syncThemeColor);
        root.addEventListener('mat:scheme', syncThemeColor);

        if (window.matchMedia) {
            var media = window.matchMedia('(prefers-color-scheme: dark)');
            var listener = function () { window.setTimeout(syncThemeColor, 0); };
            if (typeof media.addEventListener === 'function') {
                media.addEventListener('change', listener);
            } else if (typeof media.addListener === 'function') {
                media.addListener(listener);
            }
        }
    }

    /* ------------------------- installed app (PWA) ----------------------- */

    /*
        data-standalone marks a page that runs from the home screen (iOS: navigator.standalone, the others:
        display-mode). CSS uses it for what only an installed app needs; data-platform tells iOS from the rest.
    */
    function initStandalone() {
        var standalone = window.navigator.standalone === true
            || (window.matchMedia && window.matchMedia('(display-mode: standalone)').matches);
        if (standalone) {
            root.setAttribute('data-standalone', 'true');
        }

        var ua = window.navigator.userAgent || '';
        var ios = /iPad|iPhone|iPod/.test(ua)
            || (/Macintosh/.test(ua) && window.navigator.maxTouchPoints > 1);
        if (ios) {
            root.setAttribute('data-platform', 'ios');
        } else if (/Android/.test(ua)) {
            root.setAttribute('data-platform', 'android');
        }
    }

    /* ------------------------ the on-screen keyboard --------------------- */

    /*
        The shell is pinned to the four edges of the screen (position: fixed). When the on-screen keyboard
        opens, iOS and Android shrink only the visual viewport and leave the layout where it is, so the lower
        part of the page, and the field that has the focus, would sit behind the keyboard. --ms-keyboard-inset
        is the height of what covers the page from below; the shell ends above it (app.css), and the field is
        scrolled into view inside the page, which scrolls on its own.
    */
    function initKeyboard() {
        var viewport = window.visualViewport;
        if (!viewport) {
            return;
        }

        var last = -1;

        function update() {
            /* a pinch zoom also makes the visual viewport smaller than the layout: that is no keyboard */
            var covered = viewport.scale > 1.05 ? 0 : Math.round(window.innerHeight - viewport.height - viewport.offsetTop);
            var inset = covered > 80 ? covered : 0;
            if (inset === last) {
                return;
            }

            last = inset;
            root.style.setProperty('--ms-keyboard-inset', inset + 'px');
            root.classList.toggle('ms-keyboard-open', inset > 0);

            if (inset > 0 && document.activeElement && document.activeElement.scrollIntoView) {
                window.setTimeout(function () {
                    var active = document.activeElement;
                    if (active && /^(INPUT|TEXTAREA|SELECT)$/.test(active.tagName)) {
                        active.scrollIntoView({ block: 'center', behavior: 'smooth' });
                    }
                }, 60);
            }
        }

        viewport.addEventListener('resize', update);
        viewport.addEventListener('scroll', update);

        /* iOS pans the page when a field gets the focus: take it back when the keyboard goes. */
        document.addEventListener('focusout', function () {
            window.setTimeout(function () {
                if (!root.classList.contains('ms-keyboard-open')) {
                    window.scrollTo(0, 0);
                }
            }, 120);
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

    /* ------------------------------- money ------------------------------- */

    /*
        Money fields render as text with inputmode="decimal" so a comma decimal
        separator can be typed on any locale/keyboard. Keep the value invariant
        (dot) live, so client validation, the server model binding and the share
        preview all read a valid number.
    */
    function initMoney() {
        document.addEventListener('input', function (event) {
            var el = event.target;
            if (!el || !el.hasAttribute || !el.hasAttribute('data-ms-money')) {
                return;
            }
            if (el.value.indexOf(',') === -1) {
                return;
            }
            var start = el.selectionStart;
            el.value = el.value.replace(/,/g, '.');
            try {
                el.setSelectionRange(start, start);
            } catch (e) {
                /* setSelectionRange is not supported on every input type */
            }
        });
    }

    /* -------------------------------- boot ------------------------------- */

    function init() {
        initStandalone();
        initThemeColor();
        initKeyboard();
        initShare();
        initMoney();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
