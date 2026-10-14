/* ==========================================================================
   MatSplit install helper.
   Android/Chromium: catches 'beforeinstallprompt' and offers one button that
   triggers the native prompt exactly once.
   iOS: there is no install event, so a short "Teilen -> Zum Home-Bildschirm"
   guide is shown instead - only when the app does not already run from the
   home screen. Since iOS 16.4 every browser can add a web app, so the guide
   is not limited to Safari; the wording follows the browser.
   The hint is dismissible and stays away for 30 days (localStorage).
   Styles live in css/app.css (.ms-pwa-install), the buttons are MatUi's.
   ========================================================================== */
(function () {
    'use strict';

    var DISMISS_KEY = 'matsplit.pwa.installDismissed';
    var SNOOZE_DAYS = 30;
    var SHOW_DELAY = 1200;
    var SVG_NS = 'http://www.w3.org/2000/svg';

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

    /* On iOS every browser uses WebKit; Safari has the share button at the
       bottom (iPhone) or the top (iPad), the others in the address bar. */
    function isIosSafari() {
        if (!isIos()) {
            return false;
        }

        var ua = window.navigator.userAgent || '';
        return !/CriOS|FxiOS|EdgiOS|OPiOS|Chrome|Firefox|DuckDuckGo|Yandex/.test(ua);
    }

    /* A web view inside another app (Instagram, Facebook, Gmail ...) cannot add to the home screen. */
    function isInAppBrowser() {
        var ua = window.navigator.userAgent || '';
        return /FBAN|FBAV|Instagram|Line\/|MicroMessenger|Snapchat|GSA\//.test(ua);
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

    function icon(id, className) {
        var svg = document.createElementNS(SVG_NS, 'svg');
        svg.setAttribute('class', className);
        svg.setAttribute('aria-hidden', 'true');
        svg.setAttribute('focusable', 'false');
        var use = document.createElementNS(SVG_NS, 'use');
        use.setAttribute('href', '#' + id);
        svg.appendChild(use);
        return svg;
    }

    /* The share symbol of iOS (a square with an arrow up), so that the guide shows what to look for. */
    function shareGlyph() {
        var svg = document.createElementNS(SVG_NS, 'svg');
        svg.setAttribute('class', 'mat-icon');
        svg.setAttribute('viewBox', '0 0 24 24');
        svg.setAttribute('aria-hidden', 'true');
        svg.setAttribute('focusable', 'false');
        var path = document.createElementNS(SVG_NS, 'path');
        path.setAttribute('d', 'M12 3v12M8.5 6.5 12 3l3.5 3.5M7 10H6a1 1 0 0 0-1 1v8a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1v-8a1 1 0 0 0-1-1h-1');
        svg.appendChild(path);
        return svg;
    }

    function button(label, kind) {
        var element = document.createElement('button');
        element.type = 'button';
        element.className = 'mat-btn mat-btn--' + kind + ' mat-btn--sm';
        element.textContent = label;
        return element;
    }

    function step(parts) {
        var item = document.createElement('li');
        parts.forEach(function (part) {
            if (typeof part === 'string') {
                item.appendChild(document.createTextNode(part));
            } else {
                item.appendChild(part);
            }
        });
        return item;
    }

    function bold(text) {
        var strong = document.createElement('strong');
        strong.textContent = text;
        return strong;
    }

    function build(mode) {
        var box = document.createElement('aside');
        box.className = 'ms-pwa-install';
        box.setAttribute('data-ms-pwa-install-host', 'true');
        box.setAttribute('role', 'complementary');
        box.setAttribute('aria-label', 'App installieren');

        var body = document.createElement('div');
        body.className = 'ms-pwa-install__body';

        var title = document.createElement('p');
        title.className = 'ms-pwa-install__title';
        title.textContent = 'MatSplit als App';
        body.appendChild(title);

        var actions = document.createElement('div');
        actions.className = 'ms-pwa-install__actions';

        if (mode === 'ios') {
            var intro = document.createElement('p');
            intro.className = 'ms-pwa-install__text';
            intro.textContent = 'Auf dem Home-Bildschirm startet MatSplit im Vollbild, schnell und auch offline.';
            body.appendChild(intro);

            var steps = document.createElement('ol');
            steps.className = 'ms-pwa-install__steps';
            steps.appendChild(step(isIosSafari()
                ? ['Tippe auf ', shareGlyph(), ' ', bold('Teilen'), ' (iPhone: unten, iPad: oben).']
                : ['Tippe im Browser auf ', shareGlyph(), ' ', bold('Teilen'), ' (oder auf die drei Punkte).']));
            steps.appendChild(step(['Wähle ', bold('Zum Home-Bildschirm'), ' (eventuell nach unten wischen).']));
            steps.appendChild(step(['Tippe auf ', bold('Hinzufügen'), '.']));
            body.appendChild(steps);

            var understood = button('Verstanden', 'secondary');
            understood.addEventListener('click', dismiss);
            actions.appendChild(understood);
        } else {
            var text = document.createElement('p');
            text.className = 'ms-pwa-install__text';
            text.textContent = 'Installiere MatSplit auf dem Startbildschirm: Vollbild, schneller Start und Offline-Erfassung.';
            body.appendChild(text);

            var install = button('Installieren', 'primary');
            install.addEventListener('click', function () {
                install.disabled = true;
                triggerPrompt();
            });
            actions.appendChild(install);

            var later = button('Später', 'secondary');
            later.addEventListener('click', dismiss);
            actions.appendChild(later);
        }

        body.appendChild(actions);

        var close = document.createElement('button');
        close.type = 'button';
        close.className = 'mat-btn mat-btn--ghost mat-btn--icon mat-btn--sm ms-pwa-install__close';
        close.setAttribute('aria-label', 'Hinweis schließen');
        close.appendChild(icon('mat-i-x', 'mat-icon'));
        close.addEventListener('click', dismiss);

        box.appendChild(icon('mat-i-download', 'mat-icon ms-pwa-install__icon'));
        box.appendChild(body);
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

        /* iOS never fires beforeinstallprompt, so the guide is the only option. */
        if (isIos() && !isInAppBrowser()) {
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
            show(deferredPrompt ? 'prompt' : (isIos() ? 'ios' : 'prompt'));
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
