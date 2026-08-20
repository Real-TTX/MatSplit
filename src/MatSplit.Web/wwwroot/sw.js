/* ==========================================================================
   MatSplit service worker.
   Strategies
     navigation  -> network first, fallback /offline.html
     static      -> stale while revalidate (css / js / img / manifest)
     /receipts/* -> network first with a capped runtime cache
     /api/*      -> network only (never cached, never served stale)
   Background sync tag 'matsplit-sync' flushes the IndexedDB outbox that
   js/offline-sync.js fills while the device is offline. Browsers without
   Background Sync (Safari) fall back to the window 'online' event, handled
   in offline-sync.js.
   ========================================================================== */
'use strict';

var CACHE_VERSION = 'v4';
var SHELL_CACHE = 'matsplit-shell-' + CACHE_VERSION;
var ASSET_CACHE = 'matsplit-assets-' + CACHE_VERSION;
var MEDIA_CACHE = 'matsplit-media-' + CACHE_VERSION;
var CURRENT_CACHES = [SHELL_CACHE, ASSET_CACHE, MEDIA_CACHE];

var OFFLINE_URL = '/offline.html';
var SYNC_TAG = 'matsplit-sync';
var MEDIA_LIMIT = 60;

/* App shell. Everything here must be reachable anonymously, otherwise the
   precache would store a login redirect. */
var PRECACHE_URLS = [
    OFFLINE_URL,
    '/manifest.webmanifest',
    '/css/site.css',
    '/css/theme.css',
    '/css/layout.css',
    '/css/controls.css',
    '/css/scrollbars.css',
    '/js/site.js',
    '/js/offline-sync.js',
    '/js/pwa-install.js',
    '/img/logo.svg',
    '/img/logo-mark.svg',
    '/img/favicon.svg',
    '/img/apple-touch-icon.png',
    '/img/icon-192.png',
    '/img/icon-512.png',
    '/img/icon-512.svg'
];

var STATIC_PREFIXES = ['/css/', '/js/', '/img/', '/lib/'];

/* ------------------------------- helpers -------------------------------- */

function isSameOrigin(url) {
    return url.origin === self.location.origin;
}

function isApiRequest(url) {
    return url.pathname === '/health' || url.pathname.indexOf('/api/') === 0;
}

function isStaticAsset(url) {
    if (url.pathname === '/manifest.webmanifest') {
        return true;
    }

    for (var i = 0; i < STATIC_PREFIXES.length; i++) {
        if (url.pathname.indexOf(STATIC_PREFIXES[i]) === 0) {
            return true;
        }
    }

    return false;
}

function isMediaRequest(url) {
    return url.pathname.indexOf('/receipts/') === 0;
}

function isNavigation(request) {
    if (request.mode === 'navigate') {
        return true;
    }

    var accept = request.headers.get('accept') || '';
    return request.method === 'GET' && accept.indexOf('text/html') !== -1;
}

/* Cacheable = a real 200 from us. Opaque and partial responses are skipped so
   we never serve a broken body from the cache. */
function isCacheable(response) {
    return !!response
        && response.status === 200
        && (response.type === 'basic' || response.type === 'default');
}

function trimCache(cacheName, maxEntries) {
    return caches.open(cacheName).then(function (cache) {
        return cache.keys().then(function (keys) {
            if (keys.length <= maxEntries) {
                return null;
            }

            var doomed = keys.slice(0, keys.length - maxEntries).map(function (key) {
                return cache.delete(key);
            });

            return Promise.all(doomed);
        });
    });
}

function notifyClients(message) {
    return self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then(function (clientList) {
        clientList.forEach(function (client) {
            client.postMessage(message);
        });
        return clientList.length;
    });
}

/* -------------------------------- install ------------------------------- */

self.addEventListener('install', function (event) {
    event.waitUntil(
        caches.open(SHELL_CACHE).then(function (cache) {
            /* One missing file must not fail the whole installation. */
            var jobs = PRECACHE_URLS.map(function (url) {
                var request = new Request(url, { cache: 'reload', credentials: 'same-origin' });
                return fetch(request)
                    .then(function (response) {
                        if (!isCacheable(response)) {
                            return null;
                        }
                        return cache.put(url, response);
                    })
                    .catch(function () { return null; });
            });

            return Promise.all(jobs);
        }).then(function () {
            return self.skipWaiting();
        })
    );
});

/* -------------------------------- activate ------------------------------ */

self.addEventListener('activate', function (event) {
    event.waitUntil(
        caches.keys()
            .then(function (names) {
                var doomed = names.filter(function (name) {
                    return name.indexOf('matsplit-') === 0 && CURRENT_CACHES.indexOf(name) === -1;
                });

                return Promise.all(doomed.map(function (name) {
                    return caches.delete(name);
                }));
            })
            .then(function () {
                if (self.registration.navigationPreload) {
                    return self.registration.navigationPreload.enable().catch(function () { });
                }
                return null;
            })
            .then(function () {
                return self.clients.claim();
            })
            .then(function () {
                return notifyClients({ type: 'matsplit-activated', version: CACHE_VERSION });
            })
    );
});

/* --------------------------------- fetch -------------------------------- */

/* Navigation: always try the network first, the HTML is user specific. */
function handleNavigation(event) {
    var preload = event.preloadResponse;

    return Promise.resolve(preload)
        .then(function (preloaded) {
            if (preloaded) {
                return preloaded;
            }
            return fetch(event.request);
        })
        .catch(function () {
            return caches.open(SHELL_CACHE).then(function (cache) {
                return cache.match(OFFLINE_URL).then(function (cached) {
                    if (cached) {
                        return cached;
                    }

                    return new Response(
                        '<!DOCTYPE html><html lang="de"><head><meta charset="utf-8">' +
                        '<title>Offline</title></head><body><h1>Offline</h1>' +
                        '<p>MatSplit ist gerade nicht erreichbar.</p></body></html>',
                        { status: 503, headers: { 'Content-Type': 'text/html; charset=utf-8' } });
                });
            });
        });
}

/* Static assets: answer from cache at once, refresh in the background. */
function handleStatic(event) {
    var request = event.request;

    return caches.open(ASSET_CACHE).then(function (cache) {
        return cache.match(request, { ignoreSearch: true }).then(function (cached) {
            var network = fetch(request)
                .then(function (response) {
                    if (isCacheable(response)) {
                        cache.put(request, response.clone());
                    }
                    return response;
                })
                .catch(function () { return null; });

            if (cached) {
                event.waitUntil(network);
                return cached;
            }

            return network.then(function (response) {
                if (response) {
                    return response;
                }

                /* Precached shell copy is the last resort (asp-append-version
                   adds a ?v= query, hence ignoreSearch). */
                return caches.open(SHELL_CACHE).then(function (shell) {
                    return shell.match(request, { ignoreSearch: true }).then(function (fallback) {
                        return fallback || Response.error();
                    });
                });
            });
        });
    });
}

/* Receipt images: fresh when online, cached copy when offline. */
function handleMedia(event) {
    var request = event.request;

    return fetch(request)
        .then(function (response) {
            if (isCacheable(response)) {
                var copy = response.clone();
                event.waitUntil(caches.open(MEDIA_CACHE).then(function (cache) {
                    return cache.put(request, copy).then(function () {
                        return trimCache(MEDIA_CACHE, MEDIA_LIMIT);
                    });
                }));
            }
            return response;
        })
        .catch(function () {
            return caches.match(request).then(function (cached) {
                return cached || Response.error();
            });
        });
}

self.addEventListener('fetch', function (event) {
    var request = event.request;

    if (request.method !== 'GET') {
        /* POST/PUT/DELETE never touch the cache. Offline writes are queued by
           offline-sync.js before they ever reach the network. */
        return;
    }

    var url;
    try {
        url = new URL(request.url);
    } catch (error) {
        return;
    }

    if (!isSameOrigin(url) || url.pathname === '/sw.js') {
        return;
    }

    if (isApiRequest(url)) {
        return; /* network only */
    }

    if (isNavigation(request)) {
        event.respondWith(handleNavigation(event));
        return;
    }

    if (isMediaRequest(url)) {
        event.respondWith(handleMedia(event));
        return;
    }

    if (isStaticAsset(url)) {
        event.respondWith(handleStatic(event));
    }
});

/* ------------------------- outbox flush (IndexedDB) --------------------- */
/* Kept deliberately small: the page owns the queue logic, the worker only
   needs to be able to drain it when no window is open (Chrome/Android
   Background Sync). Duplicates are harmless, the server deduplicates on
   clientId. */

var DB_NAME = 'matsplit-offline';
var DB_VERSION = 1;
var STORES = [
    { store: 'pendingExpenses', endpoint: '/api/sync/expenses' },
    { store: 'pendingPayments', endpoint: '/api/sync/payments' }
];

function openDb() {
    return new Promise(function (resolve, reject) {
        var request = indexedDB.open(DB_NAME, DB_VERSION);

        request.onupgradeneeded = function () {
            var db = request.result;
            STORES.forEach(function (entry) {
                if (!db.objectStoreNames.contains(entry.store)) {
                    db.createObjectStore(entry.store, { keyPath: 'clientId' });
                }
            });
        };

        request.onsuccess = function () { resolve(request.result); };
        request.onerror = function () { reject(request.error); };
        request.onblocked = function () { reject(new Error('IndexedDB blocked')); };
    });
}

function readAll(db, storeName) {
    return new Promise(function (resolve, reject) {
        if (!db.objectStoreNames.contains(storeName)) {
            resolve([]);
            return;
        }

        var tx = db.transaction(storeName, 'readonly');
        var request = tx.objectStore(storeName).getAll();
        request.onsuccess = function () { resolve(request.result || []); };
        request.onerror = function () { reject(request.error); };
    });
}

function writeQueue(db, storeName, remove, update) {
    return new Promise(function (resolve, reject) {
        if (!db.objectStoreNames.contains(storeName)) {
            resolve();
            return;
        }

        var tx = db.transaction(storeName, 'readwrite');
        var store = tx.objectStore(storeName);
        remove.forEach(function (clientId) { store.delete(clientId); });
        update.forEach(function (entry) { store.put(entry); });
        tx.oncomplete = function () { resolve(); };
        tx.onerror = function () { reject(tx.error); };
        tx.onabort = function () { reject(tx.error); };
    });
}

function flushStore(db, entry) {
    return readAll(db, entry.store).then(function (items) {
        var pending = items.filter(function (item) {
            return item && item.payload && item.status !== 'done';
        });

        if (pending.length === 0) {
            return { sent: 0, failed: 0 };
        }

        var body = pending.map(function (item) {
            var payload = Object.assign({}, item.payload);
            payload.clientId = item.clientId;
            return payload;
        });

        return fetch(entry.endpoint, {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        }).then(function (response) {
            if (!response.ok) {
                throw new Error('HTTP ' + response.status);
            }
            return response.json();
        }).then(function (data) {
            var results = (data && data.results) || [];
            var byId = {};
            results.forEach(function (result) {
                if (result && result.clientId) {
                    byId[result.clientId] = result;
                }
            });

            var remove = [];
            var update = [];

            pending.forEach(function (item) {
                var result = byId[item.clientId];
                if (result && result.success) {
                    remove.push(item.clientId);
                    return;
                }

                item.attempts = (item.attempts || 0) + 1;
                item.lastError = (result && result.error) || 'Unbekannter Fehler beim Synchronisieren.';
                item.status = 'error';
                update.push(item);
            });

            return writeQueue(db, entry.store, remove, update).then(function () {
                return { sent: remove.length, failed: update.length };
            });
        });
    });
}

function flushOutbox() {
    var db = null;

    return openDb()
        .then(function (opened) {
            db = opened;
            return flushStore(db, STORES[0]).then(function (first) {
                return flushStore(db, STORES[1]).then(function (second) {
                    return {
                        sent: first.sent + second.sent,
                        failed: first.failed + second.failed
                    };
                });
            });
        })
        .then(function (summary) {
            if (db) {
                db.close();
            }
            return notifyClients({ type: 'matsplit-sync-done', summary: summary }).then(function () {
                return summary;
            });
        })
        .catch(function (error) {
            if (db) {
                db.close();
            }
            return notifyClients({
                type: 'matsplit-sync-failed',
                error: (error && error.message) || 'Sync fehlgeschlagen.'
            }).then(function () {
                /* Rethrow so the browser retries the sync registration later. */
                throw error;
            });
        });
}

self.addEventListener('sync', function (event) {
    if (event.tag !== SYNC_TAG) {
        return;
    }

    /* A visible window flushes on its own (better error reporting), the worker
       only steps in when the app is closed. */
    event.waitUntil(
        self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then(function (clientList) {
            if (clientList.length > 0) {
                return notifyClients({ type: 'matsplit-sync', tag: SYNC_TAG }).then(function () {
                    return null;
                });
            }
            return flushOutbox();
        })
    );
});

self.addEventListener('message', function (event) {
    var data = event.data || {};

    if (data.type === 'matsplit-skip-waiting') {
        self.skipWaiting();
        return;
    }

    if (data.type === 'matsplit-flush') {
        event.waitUntil(flushOutbox().catch(function () { }));
        return;
    }

    if (data.type === 'matsplit-register-sync' && self.registration.sync) {
        event.waitUntil(self.registration.sync.register(SYNC_TAG).catch(function () { }));
    }
});
