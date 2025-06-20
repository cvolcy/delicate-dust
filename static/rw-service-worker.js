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

this.addEventListener('message', async (event) => {
    switch (event.data?.type) {
        case 'READY':
            event.source.postMessage({ type: 'REFRESH_APP_DATA', appData: await getAppData() });
            break;
        case 'SHOW_NOTIFICATION':
            await showDailyNotification();
            event.source.postMessage({ type: 'REFRESH_APP_DATA', appData: await getAppData() });
            break;
        default:
            break;
    }
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

    let data = await getAppData();
    data = data || {}; // Initialize if not found
    data.words = (data.words || []);
    const newWord = { ...randomWord, v: DB_VERSION, date: new Date().toISOString(), id: uuidv4() };
    data.words.push(newWord);

    await saveAppData(newWord);
    console.log('Word added to IndexedDB:', randomWord);

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

const DB_NAME = 'my_app_db';
const STORE_NAME = 'app_data_store';
const DB_VERSION = 1;

function openDb() {
    return new Promise((resolve, reject) => {
        const request = indexedDB.open(DB_NAME, DB_VERSION);

        request.onerror = (event) => {
            console.error('IndexedDB error:', event.target.errorCode);
            reject(event.target.errorCode);
        };

        request.onsuccess = (event) => {
            resolve(event.target.result);
        };

        request.onupgradeneeded = (event) => {
            const db = event.target.result;
            if (!db.objectStoreNames.contains(STORE_NAME)) {
                db.createObjectStore(STORE_NAME, { keyPath: 'id' });
            }
        };
    });
}

async function getAppData() {
    const db = await openDb();
    return new Promise((resolve, reject) => {
        const transaction = db.transaction(STORE_NAME, 'readonly');
        const store = transaction.objectStore(STORE_NAME);
        const request = store.getAll();

        request.onerror = (event) => reject(event.target.errorCode);
        request.onsuccess = (event) =>  {
            const data = event.target.result;
            return resolve(event.target.result);
        }
    });
}

async function saveAppData(data) {
    const db = await openDb();
    return new Promise((resolve, reject) => {
        const transaction = db.transaction(STORE_NAME, 'readwrite');
        const store = transaction.objectStore(STORE_NAME);
        const request = store.put(data);

        request.onerror = (event) => reject(event.target.errorCode);
        request.onsuccess = (event) => resolve();
    });
}

function uuidv4() {
    return "10000000-1000-4000-8000-100000000000".replace(/[018]/g, c =>
        (+c ^ crypto.getRandomValues(new Uint8Array(1))[0] & 15 >> +c / 4).toString(16)
    );
}