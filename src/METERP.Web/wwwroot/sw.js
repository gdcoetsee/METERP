// PWA Service Worker for offline field use (stub - cache critical assets in production)
const CACHE_NAME = 'meterp-v1';
const urlsToCache = [
  '/',
  '/css/professional.css',
  '/css/site.css'
];

self.addEventListener('install', event => {
  event.waitUntil(
    caches.open(CACHE_NAME)
      .then(cache => cache.addAll(urlsToCache))
  );
});

self.addEventListener('fetch', event => {
  event.respondWith(
    caches.match(event.request)
      .then(response => response || fetch(event.request))
  );
});