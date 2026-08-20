/* ==========================================================================
   MatSplit install helper.
   Android/Chromium: catches 'beforeinstallprompt' and offers one button that
   triggers the native prompt exactly once.
   iOS Safari: no install event exists, so a short "Teilen -> Zum
   Home-Bildschirm" hint is shown instead - but only in Safari and only when
   the app does not already run standalone.
   The hint is dismissible and stays away for 30 days (localStorage).
   Styles live in Pages/Shared/_OfflineBanner.cshtml (.ms-pwa-install).
   ========================================================================== */
(function () {
    'use strict';

    var DISMISS_KEY = 'matsplit.pwa.installDismissed';
    var SNOOZE_DAYS = 30;
    var SHOW_DELAY = 1200;

    var root = document.documentElement;
    var deferredPrompt = null;
    var host = null;
    var shown = false;

    /* ------------------------------ detection ---------------------------- */

    function isStandalone() {
        if (root.getAttribute('data-standalone') === 'true') {
            return true;
        }

        if (window.navigator.standalone === true) {
            return true;
        }

        return !!(window.matchMedia && (
            window.matchMedia('(display-mode: standalone)').matches
            || window.matchMedia('(display-mode: fullscreen)').matches
            || window.matchMedia('(display-mode: minimal-ui)').matches));
    }

    function isIos() {
        var ua = window.navigator.userAgent || '';
        var iosDevice = /iPad|iPhone|iPod/.test(ua);
        /* iPadOS 13+ reports itself as Macintosh but has a touch screen. */
        var iPadDesktopUa = /Macintosh/.test(ua)
            && typeof document.ontouchend !== 'undefined'
            && window.navigator.maxTouchPoints > 1;

        return iosDevice || iPadDesktopUa;
    }

    /* On iOS every browser uses WebKit, but only Safari can add to the home
       screen from the share sheet. */
    function isIosSafari() {
        if (!isIos()) {
            return false;
        }

        var ua = window.navigator.userAgent || '';
        return !/CriOS|FxiOS|EdgiOS|OPiOS|Chrome|Firefox|DuckDuckGo|Yandex/.test(ua);
    }

    function isDismissed() {
        try {
            var stored = window.localStorage.getItem(DISMISS_KEY);
            if (!stored) {
                return false;
            }

            var when = parseInt(stored, 10);
            if (isNaN(when)) {
                return true;
            }

            return (Date.now() - when) < SNOOZE_DAYS * 24 * 60 * 60 * 1000;
        } catch (error) {
            return false;
        }
    }

    function dismiss() {
        try {
            window.localStorage.setItem(DISMISS_KEY, String(Date.now()));
        } catch (error) {
            /* private mode: the hint simply comes back next time */
        }

        hide();
    }

    /* --------------------------------- ui -------------------------------- */

    function hide() {
        if (host && host.parentNode) {
            host.parentNode.removeChild(host);
        }
        host = null;
        root.removeAttribute('data-ms-pwa-install');
    }

    function button(label, kind) {
        var element = document.createElement('button');
        element.type = 'button';
        element.className = 'ms-pwa-install__button ms-pwa-install__button--' + kind;
        element.textContent = label;
        return element;
    }

    function build(mode) {
        var box = document.createElement('aside');
        box.className = 'ms-pwa-install';
        box.setAttribute('data-ms-pwa-install-host', 'true');
        box.setAttribute('role', 'complementary');
        box.setAttribute('aria-label', 'App installieren');

        var icon = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        icon.setAttribute('class', 'ms-pwa-install__icon');
        icon.setAttribute('width', '28');
        icon.setAttribute('height', '28');
        icon.setAttribute('viewBox', '0 0 24 24');
        icon.setAttribute('aria-hidden', 'true');
        var use = document.createElementNS('http://www.w3.org/2000/svg', 'use');
        use.setAttribute('href', '#ms-i-download');
        icon.appendChild(use);

        var body = document.createElement('div');
        body.className = 'ms-pwa-install__body';

        var title = document.createElement('p');
        title.className = 'ms-pwa-install__title';
        title.textContent = 'MatSplit als App';

        var text = document.createElement('p');
        text.className = 'ms-pwa-install__text';
        text.textContent = mode === 'ios'
            ? 'In Safari auf "Teilen" tippen und "Zum Home-Bildschirm" waehlen - danach laeuft MatSplit im Vollbild und offline.'
            : 'Installiere MatSplit auf dem Startbildschirm: Vollbild, schneller Start und Offline-Erfassung.';

        body.appendChild(title);
        body.appendChild(text);

        var actions = document.createElement('div');
        actions.className = 'ms-pwa-install__actions';

        if (mode === 'prompt') {
            var install = button('Installieren', 'primary');
            install.addEventListener('click', function () {
                install.disabled = true;
                triggerPrompt();
            });
            actions.appendChild(install);
        }

        var later = button(mode === 'ios' ? 'Verstanden' : 'Spaeter', 'ghost');
        later.addEventListener('click', dismiss);
        actions.appendChild(later);

        var close = document.createElement('button');
        close.type = 'button';
        close.className = 'ms-pwa-install__close';
        close.setAttribute('aria-label', 'Hinweis schliessen');
        var closeIcon = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        closeIcon.setAttribute('width', '16');
        closeIcon.setAttribute('height', '16');
        closeIcon.setAttribute('viewBox', '0 0 24 24');
        closeIcon.setAttribute('aria-hidden', 'true');
        var closeUse = document.createElementNS('http://www.w3.org/2000/svg', 'use');
        closeUse.setAttribute('href', '#ms-i-close');
        closeIcon.appendChild(closeUse);
        close.appendChild(closeIcon);
        close.addEventListener('click', dismiss);

        box.appendChild(icon);
        box.appendChild(body);
        box.appendChild(actions);
        box.appendChild(close);

        return box;
    }

    function show(mode) {
        if (shown || host || isStandalone() || isDismissed()) {
            return;
        }

        shown = true;
        host = build(mode);
        document.body.appendChild(host);
        root.setAttribute('data-ms-pwa-install', mode);

        /* Animate in after the node is in the tree. */
        window.setTimeout(function () {
            if (host) {
                host.setAttribute('data-visible', 'true');
            }
        }, 20);
    }

    function triggerPrompt() {
        if (!deferredPrompt) {
            hide();
            return;
        }

        var prompt = deferredPrompt;
        deferredPrompt = null;   /* a saved event may only be used once */

        try {
            prompt.prompt();
        } catch (error) {
            hide();
            return;
        }

        var choice = prompt.userChoice;
        if (!choice || typeof choice.then !== 'function') {
            hide();
            return;
        }

        choice.then(function (result) {
            if (result && result.outcome === 'accepted') {
                hide();
                return;
            }
            dismiss();
        }, function () {
            hide();
        });
    }

    /* ------------------------------- wiring ------------------------------ */

    window.addEventListener('beforeinstallprompt', function (event) {
        event.preventDefault();
        deferredPrompt = event;

        if (isStandalone() || isDismissed()) {
            return;
        }

        window.setTimeout(function () {
            show('prompt');
        }, SHOW_DELAY);
    });

    window.addEventListener('appinstalled', function () {
        deferredPrompt = null;
        try {
            window.localStorage.setItem(DISMISS_KEY, String(Date.now()));
        } catch (error) {
            /* ignore */
        }
        hide();
    });

    function init() {
        if (isStandalone()) {
            root.setAttribute('data-standalone', 'true');
            return;
        }

        /* iOS never fires beforeinstallprompt, so the hint is the only option. */
        if (isIosSafari()) {
            window.setTimeout(function () {
                show('ios');
            }, SHOW_DELAY);
        }
    }

    window.MatSplitInstall = {
        /** True when the app runs from the home screen. */
        isStandalone: isStandalone,
        /** True when a native install prompt is available. */
        canPrompt: function () { return !!deferredPrompt; },
        /** Trigger the native prompt (Android/Chromium only). */
        prompt: triggerPrompt,
        /** Show the hint again even if it was dismissed before. */
        show: function () {
            try {
                window.localStorage.removeItem(DISMISS_KEY);
            } catch (error) {
                /* ignore */
            }
            shown = false;
            show(deferredPrompt ? 'prompt' : (isIosSafari() ? 'ios' : 'prompt'));
        },
        /** Hide and snooze for 30 days. */
        dismiss: dismiss
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
