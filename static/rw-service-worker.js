const CACHE_NAME = 'random-word-v1';

// Use the install event to pre-cache all initial resources.
this.addEventListener('install', event => {
    event.waitUntil((async () => {
        const cache = await caches.open(CACHE_NAME);
        await cache.addAll([
            '/api/static/random-word.html',
        ]);
    })());
});

this.addEventListener('fetch', event => {
    event.respondWith((async () => {
        const cache = await caches.open(CACHE_NAME);

        // Get the resource from the cache.
        const cachedResponse = await cache.match(event.request);
        if (cachedResponse) {
            return cachedResponse;
        } else {
            try {
                // If the resource was not in the cache, try the network.
                const fetchResponse = await fetch(event.request);

                // Save the resource in the cache and return it.
                cache.put(event.request, fetchResponse.clone());
                return fetchResponse;
            } catch (e) {
                // The network failed.
            }
        }
    })());
});

this.addEventListener('periodicsync', (event) => {
    if (event.tag === 'daily-notification-sync') {
        event.waitUntil(showDailyNotification());
    }
});

async function showDailyNotification() {
    const randomWord = await (await fetch("/api/randomword")).json();
    const randomWordLocalized = randomWord.fr;
    // const randomWordLocalized = { word: 'test', definition: 'test' };

    const title = 'Daily Update : ' + randomWordLocalized.word;
    const options = {
        body: randomWordLocalized.definition,
        tag: 'daily-update-notification',
        renotify: true, // Optional: ensures new notification is shown if tag is reused
        requireInteraction: false, // Optional: notification disappears after a short time on desktop
        data: {
            timestamp: Date.now(),
            notificationType: 'daily_update'
        }
    };

    // Display the notification
    this.registration.showNotification(title, options)
        .then(() => {
            console.log('Daily notification displayed successfully!');
        })
        .catch((error) => {
            console.error('Failed to show daily notification:', error);
        });
}