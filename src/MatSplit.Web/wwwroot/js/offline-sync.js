/* ==========================================================================
   MatSplit offline outbox.
   Captures new expenses and payments while the device is offline, stores them
   in IndexedDB ('matsplit-offline' -> 'pendingExpenses' / 'pendingPayments')
   and replays them against POST /api/sync/expenses resp. /api/sync/payments
   as soon as the connection is back. Every entry carries a clientId (uuid),
   the server treats it as an idempotency key, so a double replay is harmless.
   Conflicts: the server always wins - a rejected entry stays in the queue with
   its error text and is never silently applied.
   No dependencies, ES5 syntax so old iOS Safari keeps working.
   ========================================================================== */
(function () {
    'use strict';

    var DB_NAME = 'matsplit-offline';
    var DB_VERSION = 1;
    var STORE_EXPENSES = 'pendingExpenses';
    var STORE_PAYMENTS = 'pendingPayments';
    var SYNC_TAG = 'matsplit-sync';

    var KIND_EXPENSE = 'expense';
    var KIND_PAYMENT = 'payment';

    var KINDS = {};
    KINDS[KIND_EXPENSE] = { store: STORE_EXPENSES, endpoint: '/api/sync/expenses', label: 'Ausgabe' };
    KINDS[KIND_PAYMENT] = { store: STORE_PAYMENTS, endpoint: '/api/sync/payments', label: 'Zahlung' };

    var root = document.documentElement;
    var supported = typeof indexedDB !== 'undefined' && !!window.Promise;

    var state = {
        online: navigator.onLine !== false,
        status: 'idle',      /* idle | offline | queued | syncing | error */
        pending: 0,
        failed: 0,
        message: null,
        lastError: null
    };

    var flushing = null;

    /* ------------------------------ utilities ---------------------------- */

    function newId() {
        if (window.crypto && typeof window.crypto.randomUUID === 'function') {
            return window.crypto.randomUUID();
        }

        var bytes = new Array(16);
        if (window.crypto && window.crypto.getRandomValues) {
            var buffer = new Uint8Array(16);
            window.crypto.getRandomValues(buffer);
            for (var i = 0; i < 16; i++) {
                bytes[i] = buffer[i];
            }
        } else {
            for (var j = 0; j < 16; j++) {
                bytes[j] = Math.floor(Math.random() * 256);
            }
        }

        var hex = bytes.map(function (value) {
            return ('0' + value.toString(16)).slice(-2);
        }).join('');

        return hex.slice(0, 8) + '-' + hex.slice(8, 12) + '-4' + hex.slice(13, 16)
            + '-a' + hex.slice(17, 20) + '-' + hex.slice(20, 32);
    }

    function isOnline() {
        return navigator.onLine !== false;
    }

    function normalizeKey(key) {
        var name = String(key || '');
        var dot = name.lastIndexOf('.');
        if (dot >= 0) {
            name = name.substring(dot + 1);
        }
        return name.toLowerCase().replace(/[^a-z0-9]/g, '');
    }

    /* "12,50" / "12.50" / "1.234,56" / "1 234,56 EUR" -> 1250 / 123456 */
    function parseAmountToCents(raw, alreadyCents) {
        if (raw === null || raw === undefined) {
            return null;
        }

        var text = String(raw).replace(/[^0-9,.\-]/g, '').trim();
        if (text === '' || text === '-') {
            return null;
        }

        if (alreadyCents) {
            var cents = parseInt(text.replace(/[.,]/g, ''), 10);
            return isNaN(cents) ? null : cents;
        }

        var lastComma = text.lastIndexOf(',');
        var lastDot = text.lastIndexOf('.');
        var decimalAt = Math.max(lastComma, lastDot);

        var normalized;
        if (decimalAt < 0) {
            normalized = text;
        } else {
            var head = text.substring(0, decimalAt).replace(/[.,]/g, '');
            var tail = text.substring(decimalAt + 1).replace(/[.,]/g, '');
            /* 1.234 with three trailing digits is a thousands group, not cents. */
            normalized = tail.length === 3 && lastDot === decimalAt && lastComma < 0
                ? head + tail
                : head + '.' + tail;
        }

        var value = parseFloat(normalized);
        if (isNaN(value)) {
            return null;
        }

        return Math.round(value * 100);
    }

    function toIsoDate(raw) {
        if (!raw) {
            return null;
        }

        var text = String(raw).trim();
        if (/^\d{4}-\d{2}-\d{2}$/.test(text)) {
            return text + 'T00:00:00Z';
        }

        var german = /^(\d{1,2})\.(\d{1,2})\.(\d{4})$/.exec(text);
        if (german) {
            return german[3]
                + '-' + ('0' + german[2]).slice(-2)
                + '-' + ('0' + german[1]).slice(-2)
                + 'T00:00:00Z';
        }

        var parsed = new Date(text);
        if (!isNaN(parsed.getTime())) {
            return parsed.toISOString();
        }

        return null;
    }

    function queryParam(name) {
        var match = new RegExp('[?&]' + name + '=([^&#]*)', 'i').exec(window.location.search);
        return match ? decodeURIComponent(match[1].replace(/\+/g, ' ')) : null;
    }

    /* ------------------------------ IndexedDB ---------------------------- */

    function openDb() {
        return new Promise(function (resolve, reject) {
            var request = indexedDB.open(DB_NAME, DB_VERSION);

            request.onupgradeneeded = function () {
                var db = request.result;
                if (!db.objectStoreNames.contains(STORE_EXPENSES)) {
                    db.createObjectStore(STORE_EXPENSES, { keyPath: 'clientId' });
                }
                if (!db.objectStoreNames.contains(STORE_PAYMENTS)) {
                    db.createObjectStore(STORE_PAYMENTS, { keyPath: 'clientId' });
                }
            };

            request.onsuccess = function () { resolve(request.result); };
            request.onerror = function () { reject(request.error); };
            request.onblocked = function () { reject(new Error('IndexedDB ist blockiert.')); };
        });
    }

    function withDb(callback) {
        if (!supported) {
            return Promise.reject(new Error('Offline-Speicher steht nicht zur Verfuegung.'));
        }

        return openDb().then(function (db) {
            return Promise.resolve()
                .then(function () { return callback(db); })
                .then(function (result) {
                    db.close();
                    return result;
                }, function (error) {
                    db.close();
                    throw error;
                });
        });
    }

    function readAll(db, storeName) {
        return new Promise(function (resolve, reject) {
            var tx = db.transaction(storeName, 'readonly');
            var request = tx.objectStore(storeName).getAll();
            request.onsuccess = function () { resolve(request.result || []); };
            request.onerror = function () { reject(request.error); };
        });
    }

    function putEntry(db, storeName, entry) {
        return new Promise(function (resolve, reject) {
            var tx = db.transaction(storeName, 'readwrite');
            tx.objectStore(storeName).put(entry);
            tx.oncomplete = function () { resolve(entry); };
            tx.onerror = function () { reject(tx.error); };
            tx.onabort = function () { reject(tx.error); };
        });
    }

    function applyResults(db, storeName, remove, update) {
        return new Promise(function (resolve, reject) {
            var tx = db.transaction(storeName, 'readwrite');
            var store = tx.objectStore(storeName);
            remove.forEach(function (clientId) { store.delete(clientId); });
            update.forEach(function (entry) { store.put(entry); });
            tx.oncomplete = function () { resolve(); };
            tx.onerror = function () { reject(tx.error); };
            tx.onabort = function () { reject(tx.error); };
        });
    }

    function deleteEntry(kind, clientId) {
        var config = KINDS[kind];
        if (!config) {
            return Promise.resolve(false);
        }

        return withDb(function (db) {
            return new Promise(function (resolve, reject) {
                var tx = db.transaction(config.store, 'readwrite');
                tx.objectStore(config.store).delete(clientId);
                tx.oncomplete = function () { resolve(true); };
                tx.onerror = function () { reject(tx.error); };
            });
        }).then(function (removed) {
            return refreshCounts().then(function () { return removed; });
        });
    }

    function listQueue() {
        if (!supported) {
            return Promise.resolve([]);
        }

        return withDb(function (db) {
            return readAll(db, STORE_EXPENSES).then(function (expenses) {
                return readAll(db, STORE_PAYMENTS).then(function (payments) {
                    return expenses.concat(payments);
                });
            });
        }).catch(function () {
            return [];
        });
    }

    function refreshCounts() {
        return listQueue().then(function (items) {
            state.pending = items.length;
            state.failed = items.filter(function (item) { return item.status === 'error'; }).length;

            if (state.pending === 0 && (state.status === 'queued' || state.status === 'error')) {
                state.status = isOnline() ? 'idle' : 'offline';
                state.message = null;
            }

            render();
            return items;
        });
    }

    /* -------------------------------- banner ----------------------------- */

    function bannerText() {
        if (state.status === 'syncing') {
            return state.pending > 1
                ? state.pending + ' Eintraege werden synchronisiert ...'
                : 'Eintrag wird synchronisiert ...';
        }

        if (state.status === 'error') {
            return 'Synchronisierung fehlgeschlagen';
        }

        if (!state.online) {
            return state.pending > 0
                ? 'Offline - ' + state.pending + ' Eintrag/Eintraege warten auf Synchronisierung'
                : 'Offline - Aenderungen werden zwischengespeichert';
        }

        if (state.pending > 0) {
            return state.pending + ' Eintrag/Eintraege warten auf Synchronisierung';
        }

        return 'Alle Eintraege sind synchronisiert';
    }

    function bannerTone() {
        if (state.status === 'error') {
            return 'error';
        }
        if (state.status === 'syncing') {
            return 'info';
        }
        if (!state.online) {
            return 'warning';
        }
        return state.pending > 0 ? 'info' : 'success';
    }

    function isBannerVisible() {
        return !state.online || state.pending > 0 || state.status === 'syncing' || state.status === 'error';
    }

    /* The banner element only. Never <html> or <body>, whatever attributes they
       happen to carry - a hidden root element would blank the whole page. */
    function findBanner() {
        var nodes = document.querySelectorAll('[data-ms-offline-banner]');

        for (var i = 0; i < nodes.length; i++) {
            if (nodes[i] !== root && nodes[i] !== document.body) {
                return nodes[i];
            }
        }

        return null;
    }

    function measureBanner(banner) {
        var height = banner.offsetHeight || 0;
        root.style.setProperty('--ms-offline-banner-height', height + 'px');
    }

    function notifyState() {
        var detail = {
            online: state.online,
            status: state.status,
            pending: state.pending,
            failed: state.failed
        };

        if (typeof window.CustomEvent === 'function') {
            document.dispatchEvent(new CustomEvent('matsplit:offline-state', { detail: detail }));
        }
    }

    function render() {
        /* Always readable for pages and CSS, even without the banner partial. */
        root.setAttribute('data-ms-connection', state.online ? 'online' : 'offline');

        var banner = findBanner();
        if (!banner) {
            notifyState();
            return;
        }

        var visible = isBannerVisible();
        var textNode = banner.querySelector('[data-ms-offline-text]');
        var detailNode = banner.querySelector('[data-ms-offline-detail]');
        var retry = banner.querySelector('[data-ms-offline-retry]');
        var spinner = banner.querySelector('[data-ms-offline-spinner]');

        if (textNode) {
            textNode.textContent = bannerText();
        }

        if (detailNode) {
            var detail = state.message || state.lastError || '';
            detailNode.textContent = detail;
            detailNode.hidden = detail === '';
        }

        if (retry) {
            retry.hidden = !(state.online && state.pending > 0 && state.status !== 'syncing');
        }

        if (spinner) {
            spinner.hidden = state.status !== 'syncing';
        }

        banner.setAttribute('data-tone', bannerTone());
        banner.setAttribute('aria-hidden', visible ? 'false' : 'true');
        banner.hidden = !visible;

        root.setAttribute('data-ms-banner', visible ? 'visible' : 'hidden');

        if (visible) {
            measureBanner(banner);
        } else {
            root.style.setProperty('--ms-offline-banner-height', '0px');
        }

        notifyState();
    }

    function setMessage(message) {
        state.message = message || null;
        render();

        if (message) {
            window.setTimeout(function () {
                if (state.message === message) {
                    state.message = null;
                    render();
                }
            }, 8000);
        }
    }

    /* ------------------------------- queueing ---------------------------- */

    function enqueue(kind, payload, label) {
        var config = KINDS[kind];
        if (!config) {
            return Promise.reject(new Error('Unbekannter Eintragstyp.'));
        }

        var entry = {
            clientId: newId(),
            kind: kind,
            groupId: payload && payload.groupId ? payload.groupId : 0,
            label: label || config.label,
            payload: payload,
            createdUtc: new Date().toISOString(),
            attempts: 0,
            status: 'pending',
            lastError: null
        };

        return withDb(function (db) {
            return putEntry(db, config.store, entry);
        }).then(function () {
            state.status = 'queued';
            return refreshCounts().then(function () {
                return entry;
            });
        });
    }

    /* -------------------------------- syncing ---------------------------- */

    function postBatch(config, items) {
        var body = items.map(function (item) {
            var payload = {};
            for (var key in item.payload) {
                if (Object.prototype.hasOwnProperty.call(item.payload, key)) {
                    payload[key] = item.payload[key];
                }
            }
            payload.clientId = item.clientId;
            return payload;
        });

        return window.fetch(config.endpoint, {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
            body: JSON.stringify(body)
        }).then(function (response) {
            if (response.status === 401 || response.status === 403) {
                throw new Error('Nicht angemeldet - bitte neu anmelden.');
            }
            if (!response.ok) {
                throw new Error('Server antwortete mit HTTP ' + response.status + '.');
            }
            return response.json();
        });
    }

    function flushStore(db, kind) {
        var config = KINDS[kind];

        return readAll(db, config.store).then(function (items) {
            var pending = items.filter(function (item) {
                return item && item.payload;
            });

            if (pending.length === 0) {
                return { sent: 0, failed: 0 };
            }

            return postBatch(config, pending).then(function (data) {
                var byId = {};
                ((data && data.results) || []).forEach(function (result) {
                    if (result && result.clientId) {
                        byId[result.clientId] = result;
                    }
                });

                var remove = [];
                var update = [];
                var lastError = null;

                pending.forEach(function (item) {
                    var result = byId[item.clientId];
                    if (result && result.success) {
                        remove.push(item.clientId);
                        return;
                    }

                    item.attempts = (item.attempts || 0) + 1;
                    item.status = 'error';
                    item.lastError = (result && result.error)
                        || 'Der Server hat den Eintrag nicht bestaetigt.';
                    lastError = item.lastError;
                    update.push(item);
                });

                return applyResults(db, config.store, remove, update).then(function () {
                    return { sent: remove.length, failed: update.length, lastError: lastError };
                });
            });
        });
    }

    function flush() {
        if (!supported) {
            return Promise.resolve({ sent: 0, failed: 0 });
        }

        if (flushing) {
            return flushing;
        }

        if (!isOnline()) {
            state.online = false;
            state.status = 'offline';
            render();
            return Promise.resolve({ sent: 0, failed: 0 });
        }

        flushing = listQueue().then(function (items) {
            if (items.length === 0) {
                state.status = 'idle';
                state.lastError = null;
                return refreshCounts().then(function () {
                    return { sent: 0, failed: 0 };
                });
            }

            state.status = 'syncing';
            state.lastError = null;
            render();

            return withDb(function (db) {
                return flushStore(db, KIND_EXPENSE).then(function (first) {
                    return flushStore(db, KIND_PAYMENT).then(function (second) {
                        return {
                            sent: first.sent + second.sent,
                            failed: first.failed + second.failed,
                            lastError: second.lastError || first.lastError || null
                        };
                    });
                });
            }).then(function (summary) {
                state.status = summary.failed > 0 ? 'error' : 'idle';
                state.lastError = summary.failed > 0 ? summary.lastError : null;

                if (summary.sent > 0) {
                    setMessage(summary.sent + ' Eintrag/Eintraege uebernommen.');
                }

                return refreshCounts().then(function () {
                    return summary;
                });
            }).catch(function (error) {
                state.status = 'error';
                state.lastError = (error && error.message) || 'Synchronisierung fehlgeschlagen.';
                return refreshCounts().then(function () {
                    return { sent: 0, failed: state.pending, error: state.lastError };
                });
            });
        }).then(function (summary) {
            flushing = null;
            return summary;
        }, function (error) {
            flushing = null;
            state.status = 'error';
            state.lastError = (error && error.message) || 'Synchronisierung fehlgeschlagen.';
            render();
            return { sent: 0, failed: state.pending, error: state.lastError };
        });

        return flushing;
    }

    function requestBackgroundSync() {
        if (!('serviceWorker' in navigator)) {
            return Promise.resolve(false);
        }

        return navigator.serviceWorker.ready.then(function (registration) {
            if (!registration.sync) {
                /* iOS Safari: no Background Sync, the 'online' handler covers it. */
                return false;
            }

            return registration.sync.register(SYNC_TAG).then(function () {
                return true;
            }).catch(function () {
                return false;
            });
        }).catch(function () {
            return false;
        });
    }

    /* --------------------------- form interception ----------------------- */

    /* An edit page can host more than one form (Beleg-Upload, Loeschen). Only a
       form that really carries the main field is taken over. */
    function hasPrimaryField(form, kind) {
        var wanted = kind === KIND_EXPENSE
            ? ['description', 'beschreibung', 'bezeichnung', 'title', 'titel']
            : ['touserid', 'to', 'anuserid', 'an', 'empfaengeruserid'];

        if (fieldByRole(form, kind === KIND_EXPENSE ? 'description' : 'toUserId')) {
            return true;
        }

        var controls = form.querySelectorAll('[name]');
        for (var i = 0; i < controls.length; i++) {
            if (wanted.indexOf(normalizeKey(controls[i].name)) >= 0) {
                return true;
            }
        }

        return false;
    }

    function detectKind(form) {
        var explicit = (form.getAttribute('data-ms-offline') || '').toLowerCase();
        if (explicit === 'off' || explicit === 'false') {
            return null;
        }
        if (explicit === KIND_EXPENSE || explicit === KIND_PAYMENT) {
            return explicit;
        }

        var path = window.location.pathname.toLowerCase().replace(/\/+$/, '');
        var kind = null;

        if (path.indexOf('/groups/expenses/edit') >= 0) {
            kind = KIND_EXPENSE;
        } else if (path.indexOf('/groups/payments/edit') >= 0) {
            kind = KIND_PAYMENT;
        }

        if (kind === null || !hasPrimaryField(form, kind)) {
            return null;
        }

        return kind;
    }

    function isInsertForm(form) {
        var mode = (form.getAttribute('data-ms-offline-mode') || '').toLowerCase();
        if (mode === 'insert') {
            return true;
        }
        if (mode === 'update') {
            return false;
        }

        var id = queryParam('id');
        if (id !== null && id !== '' && id !== '0') {
            return false;
        }

        var field = form.querySelector('[data-ms-offline-field="id"]');
        if (field && field.value && field.value !== '0') {
            return false;
        }

        /* Input.Id hidden field of the edit pages. */
        var candidates = form.querySelectorAll('input[name]');
        for (var i = 0; i < candidates.length; i++) {
            if (normalizeKey(candidates[i].name) === 'id'
                && candidates[i].value
                && candidates[i].value !== '0') {
                return false;
            }
        }

        return true;
    }

    function fieldByRole(form, role) {
        return form.querySelector('[data-ms-offline-field="' + role + '"]');
    }

    /* Explicit data-ms-offline-field wins, otherwise the form data keys are
       matched by their trailing segment (Input.Description -> description). */
    function pick(form, data, role, candidates) {
        var explicit = fieldByRole(form, role);
        if (explicit) {
            if (explicit.type === 'checkbox') {
                return { value: explicit.checked ? 'true' : 'false', key: role };
            }
            return { value: explicit.value, key: explicit.name || role };
        }

        var keys = [];
        data.forEach(function (value, key) {
            keys.push(key);
        });

        for (var i = 0; i < candidates.length; i++) {
            for (var j = 0; j < keys.length; j++) {
                if (normalizeKey(keys[j]) === candidates[i]) {
                    var raw = data.get(keys[j]);
                    if (typeof raw === 'string' && raw.trim() !== '') {
                        return { value: raw, key: keys[j] };
                    }
                }
            }
        }

        return null;
    }

    function pickAmountCents(form, data, candidates) {
        var hit = pick(form, data, 'amountCents', ['amountcents', 'betragcents']);
        if (hit) {
            return parseAmountToCents(hit.value, true);
        }

        hit = pick(form, data, 'amount', candidates);
        if (!hit) {
            return null;
        }

        var isCents = normalizeKey(hit.key).indexOf('cents') >= 0;
        return parseAmountToCents(hit.value, isCents);
    }

    function pickGroupId(form, data) {
        var hit = pick(form, data, 'groupId', ['groupid']);
        var value = hit ? parseInt(hit.value, 10) : NaN;
        if (!isNaN(value) && value > 0) {
            return value;
        }

        var fromUrl = parseInt(queryParam('groupId') || '', 10);
        return isNaN(fromUrl) ? 0 : fromUrl;
    }

    /* Reads Input.Shares[0].UserId style collections (see
       Pages/Groups/Expenses/Edit.cshtml). A checkbox posts "true" followed by
       the hidden "false", therefore the FIRST value of a key wins - exactly
       what the model binder does. */
    function collectShareRows(data) {
        var rows = {};

        data.forEach(function (value, key) {
            var match = /shares?\[(\d+)\]\.([a-z0-9_]+)$/i.exec(key);
            if (!match || typeof value !== 'string') {
                return;
            }

            var index = match[1];
            var field = match[2].toLowerCase();
            rows[index] = rows[index] || {};

            if (!Object.prototype.hasOwnProperty.call(rows[index], field)) {
                rows[index][field] = value;
            }
        });

        return rows;
    }

    function isIncluded(row) {
        var flag = row.isincluded;
        if (flag === undefined) {
            flag = row.included;
        }
        if (flag === undefined) {
            flag = row.selected;
        }
        if (flag === undefined) {
            return true;   /* no checkbox in the markup: everybody takes part */
        }
        return flag === 'true' || flag === 'True' || flag === 'on' || flag === '1';
    }

    /* Mirrors EditModel.BuildShares: "Equal" (or no mode at all) sends an empty
       list so the server splits by the group factors, "Factors" sends the
       factors of the selected members and "Amounts" the fixed cent amounts. */
    function resolveShares(form, data, amountCents) {
        var modeHit = pick(form, data, 'shareMode', ['sharemode', 'mode', 'aufteilung']);
        var mode = (modeHit ? modeHit.value : '').toLowerCase();

        if (mode === '' || mode === 'equal') {
            return { shares: [] };
        }

        var amountMode = mode === 'amounts';
        var rows = collectShareRows(data);
        var shares = [];
        var sum = 0;

        Object.keys(rows).forEach(function (index) {
            var row = rows[index];
            var userId = parseInt(row.userid || '', 10);
            if (isNaN(userId) || userId <= 0 || !isIncluded(row)) {
                return;
            }

            if (!amountMode) {
                var factor = parseInt(row.sharefactor || '1', 10);
                if (isNaN(factor) || factor < 1) {
                    factor = 1;
                }
                shares.push({ userId: userId, shareFactor: Math.min(factor, 100), shareAmountCents: null });
                return;
            }

            var cents = parseAmountToCents(row.shareamountcents, true);
            if (cents === null) {
                cents = parseAmountToCents(row.amount, false);
            }

            if (cents === null || cents <= 0) {
                return;
            }

            sum += cents;
            shares.push({ userId: userId, shareFactor: 1, shareAmountCents: cents });
        });

        if (shares.length === 0) {
            return { error: 'Bitte mindestens einen Beteiligten auswaehlen.' };
        }

        if (amountMode && sum !== amountCents) {
            return { error: 'Die festen Betraege ergeben nicht den Gesamtbetrag.' };
        }

        return { shares: shares };
    }

    function hasFileUpload(form) {
        var inputs = form.querySelectorAll('input[type="file"]');
        for (var i = 0; i < inputs.length; i++) {
            if (inputs[i].files && inputs[i].files.length > 0) {
                return true;
            }
        }
        return false;
    }

    function buildExpense(form) {
        var data = new FormData(form);
        var description = pick(form, data, 'description', ['description', 'beschreibung', 'bezeichnung', 'title', 'titel']);
        var amountCents = pickAmountCents(form, data, ['amount', 'amounteuro', 'betrag', 'summe']);
        var groupId = pickGroupId(form, data);
        var payer = pick(form, data, 'paidByUserId', ['paidbyuserid', 'paidby', 'payeruserid', 'payer', 'zahler']);
        var date = pick(form, data, 'expenseDate', ['expensedate', 'date', 'datum']);
        var currency = pick(form, data, 'currency', ['currency', 'waehrung']);
        var category = pick(form, data, 'category', ['category', 'kategorie']);

        if (!description || !description.value.trim()) {
            return { error: 'Ohne Beschreibung kann die Ausgabe offline nicht gespeichert werden.' };
        }

        if (amountCents === null || amountCents <= 0) {
            return { error: 'Der Betrag konnte nicht gelesen werden.' };
        }

        if (groupId <= 0) {
            return { error: 'Die Gruppe konnte nicht ermittelt werden.' };
        }

        var shares = resolveShares(form, data, amountCents);
        if (shares.error) {
            return { error: shares.error };
        }

        var payerId = payer ? parseInt(payer.value, 10) : 0;

        return {
            label: description.value.trim(),
            payload: {
                groupId: groupId,
                description: description.value.trim(),
                amountCents: amountCents,
                currency: currency ? currency.value : null,
                paidByUserId: isNaN(payerId) ? 0 : payerId,
                expenseDate: toIsoDate(date ? date.value : null),
                category: category ? category.value : null,
                shares: shares.shares
            }
        };
    }

    function buildPayment(form) {
        var data = new FormData(form);
        var groupId = pickGroupId(form, data);
        var from = pick(form, data, 'fromUserId', ['fromuserid', 'from', 'vonuserid', 'von', 'senderuserid']);
        var to = pick(form, data, 'toUserId', ['touserid', 'to', 'anuserid', 'an', 'empfaengeruserid']);
        var amountCents = pickAmountCents(form, data, ['amount', 'amounteuro', 'betrag', 'summe']);
        var date = pick(form, data, 'paymentDate', ['paymentdate', 'date', 'datum']);
        var note = pick(form, data, 'note', ['note', 'notiz', 'bemerkung', 'kommentar']);

        var fromId = from ? parseInt(from.value, 10) : NaN;
        var toId = to ? parseInt(to.value, 10) : NaN;

        if (groupId <= 0) {
            return { error: 'Die Gruppe konnte nicht ermittelt werden.' };
        }

        if (isNaN(fromId) || fromId <= 0 || isNaN(toId) || toId <= 0) {
            return { error: 'Zahler und Empfaenger muessen gewaehlt sein.' };
        }

        if (amountCents === null || amountCents <= 0) {
            return { error: 'Der Betrag konnte nicht gelesen werden.' };
        }

        return {
            label: 'Zahlung ueber ' + (amountCents / 100).toFixed(2).replace('.', ',') + ' EUR',
            payload: {
                groupId: groupId,
                fromUserId: fromId,
                toUserId: toId,
                amountCents: amountCents,
                paymentDate: toIsoDate(date ? date.value : null),
                note: note ? note.value : null
            }
        };
    }

    function resetForm(form) {
        try {
            form.reset();
        } catch (error) {
            /* nothing we can do, the entry is stored anyway */
        }
    }

    function handleSubmit(event) {
        if (event.defaultPrevented || isOnline()) {
            return;
        }

        var form = event.target;
        if (!form || form.tagName !== 'FORM') {
            return;
        }

        var method = (form.getAttribute('method') || 'get').toLowerCase();
        if (method !== 'post') {
            return;
        }

        var kind = detectKind(form);
        if (!kind) {
            return;
        }

        if (!isInsertForm(form)) {
            event.preventDefault();
            setMessage('Bestehende Eintraege koennen offline nicht geaendert werden (der Server gewinnt).');
            return;
        }

        /* Reuse the client side validation of site.js so the user sees the same
           field errors as online. */
        if (window.MatSplit
            && typeof window.MatSplit.validateForm === 'function'
            && !window.MatSplit.validateForm(form)) {
            event.preventDefault();
            return;
        }

        var built = kind === KIND_EXPENSE ? buildExpense(form) : buildPayment(form);
        if (built.error) {
            event.preventDefault();
            setMessage(built.error);
            return;
        }

        event.preventDefault();

        var warnUpload = hasFileUpload(form);

        enqueue(kind, built.payload, built.label).then(function () {
            resetForm(form);
            var text = KINDS[kind].label + ' offline gespeichert - wird synchronisiert, sobald du online bist.';
            if (warnUpload) {
                text += ' Belegfotos lassen sich offline nicht uebertragen, bitte spaeter erneut hochladen.';
            }
            setMessage(text);
            return requestBackgroundSync();
        }).catch(function (error) {
            setMessage((error && error.message) || 'Der Eintrag konnte nicht zwischengespeichert werden.');
        });
    }

    /* ------------------------------ wiring ------------------------------- */

    function onOnline() {
        state.online = true;
        state.status = state.pending > 0 ? 'queued' : 'idle';
        render();
        flush();
    }

    function onOffline() {
        state.online = false;
        state.status = 'offline';
        render();
    }

    function bindBanner() {
        var banner = findBanner();
        if (!banner) {
            return;
        }

        var retry = banner.querySelector('[data-ms-offline-retry]');
        if (retry) {
            retry.addEventListener('click', function (event) {
                event.preventDefault();
                flush();
            });
        }

        window.addEventListener('resize', function () {
            if (isBannerVisible()) {
                measureBanner(banner);
            }
        });
    }

    function bindServiceWorkerMessages() {
        if (!('serviceWorker' in navigator)) {
            return;
        }

        navigator.serviceWorker.addEventListener('message', function (event) {
            var data = event.data || {};

            if (data.type === 'matsplit-sync') {
                flush();
                return;
            }

            if (data.type === 'matsplit-sync-done') {
                refreshCounts();
                return;
            }

            if (data.type === 'matsplit-sync-failed') {
                state.status = 'error';
                state.lastError = data.error || 'Synchronisierung fehlgeschlagen.';
                refreshCounts();
            }
        });
    }

    function init() {
        state.online = isOnline();
        state.status = state.online ? 'idle' : 'offline';

        bindBanner();
        bindServiceWorkerMessages();

        document.addEventListener('submit', handleSubmit, true);
        window.addEventListener('online', onOnline);
        window.addEventListener('offline', onOffline);

        document.addEventListener('visibilitychange', function () {
            if (document.visibilityState === 'visible' && isOnline() && state.pending > 0) {
                flush();
            }
        });

        render();

        refreshCounts().then(function (items) {
            if (items.length > 0 && isOnline()) {
                flush();
            }
        });
    }

    window.MatSplitOffline = {
        /** Current connectivity/queue snapshot. */
        state: function () {
            return {
                online: state.online,
                status: state.status,
                pending: state.pending,
                failed: state.failed,
                lastError: state.lastError,
                supported: supported
            };
        },
        /** Queue an expense payload by hand: { groupId, description, amountCents, ... }. */
        queueExpense: function (payload) { return enqueue(KIND_EXPENSE, payload, payload && payload.description); },
        /** Queue a payment payload by hand: { groupId, fromUserId, toUserId, amountCents, ... }. */
        queuePayment: function (payload) { return enqueue(KIND_PAYMENT, payload, null); },
        /** All queued entries including their error text. */
        list: listQueue,
        /** Drop one entry, e.g. after the user gave up on it. */
        discard: deleteEntry,
        /** Push the whole outbox to the server now. */
        flush: flush,
        /** Ask the service worker for a background sync (no-op on iOS). */
        requestSync: requestBackgroundSync,
        /** Re-render the banner, e.g. after dynamically injected markup. */
        refresh: refreshCounts
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
