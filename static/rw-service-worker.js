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

this.addEventListener('periodicsync', (event) => {
    if (event.tag === 'daily-notification-sync') {
        event.waitUntil(showDailyNotification());
    }
});

async function showDailyNotification() {
    const randomWord = await (await fetch("/api/randomword")).json();
    // const randomWord = JSON.parse(`{"fr": {"word": "épistolaire","definition": "relatif à l'art ou à la pratique de la correspondance écrite","example": "Son style épistolaire a captivé de nombreux lecteurs."},"en": {"word": "epistolary","definition": "relating to the art or practice of correspondence through letters","example": "Her epistolary style captivated many readers."}}`);
    const { fr: randomWordLocalized } = randomWord;

    // TODO change for a specialized service
    const data = JSON.parse(localStorage.getItem('app_data')) || {};
    data.words = (data.words || []);
    data.words.push({...randomWord, v: 1, date: new Date().toISOString() });
    localStorage.setItem('app_data', JSON.stringify(data));
    // END TODO

    const title = 'Word Update : ' + randomWordLocalized.word;
    const options = {
        body: `${randomWordLocalized.definition} \n\n ${randomWordLocalized.example}`,
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