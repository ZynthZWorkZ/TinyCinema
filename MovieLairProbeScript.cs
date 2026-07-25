using System.IO;
using System.Text;
using System.Text.Json;

namespace TinyCinema;

public static class MovieLairProbeScript
{
    public const string ProbeBootstrapScript =
        """
        (() => {
            if (window.__tinyCinemaProbeInstalled)
                return;

            window.__tinyCinemaProbeInstalled = true;

            function post(type, payload) {
                try {
                    window.chrome.webview.postMessage(JSON.stringify({
                        type: type,
                        payload: payload || {},
                        href: location.href,
                        origin: location.origin || '',
                        ts: Date.now()
                    }));
                } catch (e) {
                    // Host may not be ready yet.
                }
            }

            function readStorage(store) {
                const result = {};
                try {
                    for (let i = 0; i < store.length; i++) {
                        const key = store.key(i);
                        if (key)
                            result[key] = store.getItem(key);
                    }
                } catch (e) {
                    result.__error = String(e);
                }
                return result;
            }

            function scanVideos() {
                const videos = [];
                try {
                    for (const video of document.querySelectorAll('video, audio')) {
                        videos.push({
                            tag: video.tagName.toLowerCase(),
                            currentTime: video.currentTime,
                            duration: video.duration,
                            paused: video.paused,
                            ended: video.ended,
                            src: video.currentSrc || video.src || '',
                            readyState: video.readyState
                        });
                    }
                } catch (e) {
                    videos.push({ error: String(e) });
                }
                return videos;
            }

            function scanIframes() {
                const frames = [];
                try {
                    for (const iframe of document.querySelectorAll('iframe, embed')) {
                        frames.push({
                            tag: iframe.tagName.toLowerCase(),
                            id: iframe.id || '',
                            className: iframe.className || '',
                            src: iframe.getAttribute('src') || iframe.getAttribute('data-src') || ''
                        });
                    }
                } catch (e) {
                    frames.push({ error: String(e) });
                }
                return frames;
            }

            function scanDomMarkers() {
                return {
                    iframeEmbed: !!document.querySelector('#iframe-embed'),
                    seasonSelect: !!document.querySelector('select.seasonSelect'),
                    episodesRow: !!document.querySelector('.episodes'),
                    videoCount: document.querySelectorAll('video').length,
                    audioCount: document.querySelectorAll('audio').length,
                    iframeCount: document.querySelectorAll('iframe').length
                };
            }

            function wrapStorage(store, name) {
                try {
                    const originalSetItem = store.setItem.bind(store);
                    const originalGetItem = store.getItem.bind(store);
                    const originalRemoveItem = store.removeItem.bind(store);

                    store.setItem = function(key, value) {
                        post('storage-set', { store: name, key: key, value: value });
                        return originalSetItem(key, value);
                    };

                    store.getItem = function(key) {
                        const value = originalGetItem(key);
                        post('storage-get', { store: name, key: key, value: value });
                        return value;
                    };

                    store.removeItem = function(key) {
                        post('storage-remove', { store: name, key: key });
                        return originalRemoveItem(key);
                    };
                } catch (e) {
                    post('storage-wrap-failed', { store: name, error: String(e) });
                }
            }

            wrapStorage(window.localStorage, 'localStorage');
            wrapStorage(window.sessionStorage, 'sessionStorage');

            window.addEventListener('message', (event) => {
                post('postmessage-in', {
                    data: typeof event.data === 'string' ? event.data : JSON.stringify(event.data),
                    origin: event.origin || ''
                });
            });

            const originalPostMessage = window.postMessage.bind(window);
            window.postMessage = function(message, targetOrigin, transfer) {
                post('postmessage-out', {
                    data: typeof message === 'string' ? message : JSON.stringify(message),
                    targetOrigin: targetOrigin || '*'
                });
                return originalPostMessage(message, targetOrigin, transfer);
            };

            document.addEventListener('play', (event) => {
                if (event.target && (event.target.tagName === 'VIDEO' || event.target.tagName === 'AUDIO')) {
                    post('media-play', { media: scanVideos() });
                }
            }, true);

            document.addEventListener('pause', (event) => {
                if (event.target && (event.target.tagName === 'VIDEO' || event.target.tagName === 'AUDIO')) {
                    post('media-pause', { media: scanVideos() });
                }
            }, true);

            document.addEventListener('seeked', (event) => {
                if (event.target && (event.target.tagName === 'VIDEO' || event.target.tagName === 'AUDIO')) {
                    post('media-seeked', { media: scanVideos() });
                }
            }, true);

            post('probe-init', {
                userAgent: navigator.userAgent,
                readyState: document.readyState
            });

            post('dom-markers', scanDomMarkers());
            post('iframe-list', { frames: scanIframes() });
            post('storage-snapshot', {
                localStorage: readStorage(localStorage),
                sessionStorage: readStorage(sessionStorage)
            });
            post('video-snapshot', { media: scanVideos() });

            window.__tinyCinemaProbeSnapshot = () => {
                post('manual-snapshot', {
                    dom: scanDomMarkers(),
                    frames: scanIframes(),
                    media: scanVideos(),
                    localStorage: readStorage(localStorage),
                    sessionStorage: readStorage(sessionStorage)
                });
            };

            setInterval(() => {
                post('heartbeat', {
                    dom: scanDomMarkers(),
                    media: scanVideos()
                });
            }, 5000);
        })();
        """;

    public const string RequestSnapshotScript =
        """
        (() => {
            if (typeof window.__tinyCinemaProbeSnapshot === 'function')
                window.__tinyCinemaProbeSnapshot();
        })();
        """;
}
