using System.Text.Json;

namespace TinyCinema;

public static class TinyZoneCinemaMode
{
    public const string EarlyHideScript = """
        (() => {
            const root = document.documentElement;
            root.classList.add('tiny-cinema-pending');
            root.classList.remove('tiny-cinema-ready');

            try {
                document.cookie = 'srv=1; path=/; SameSite=Lax';
            } catch (_) {}

            let style = document.getElementById('tiny-cinema-early-hide');
            if (!style) {
                style = document.createElement('style');
                style.id = 'tiny-cinema-early-hide';
                style.textContent = `
                    html, body {
                        background: #070707 !important;
                        opacity: 0 !important;
                        visibility: hidden !important;
                    }
                    #header,
                    #sidebar_menu,
                    #sidebar_menu_bg,
                    .prebreadcrumb,
                    .detail_page-infor,
                    .detail-tags,
                    .film_related,
                    .dp-i-c-stick,
                    #mobile_menu,
                    #search,
                    #logo,
                    #user-slot,
                    .block-rating,
                    #footer,
                    #eps-list {
                        display: none !important;
                    }
                `;
                root.appendChild(style);
            }
        })();
        """;

    public const string BootstrapScript = """
        (() => {
            window.__tinyCinemaBootstrap = true;

            const HIDE_SELECTORS = [
                '#header',
                '#sidebar_menu',
                '#sidebar_menu_bg',
                '.prebreadcrumb',
                '.detail_page-infor',
                '.detail-tags',
                '.film_related',
                '.dp-i-c-stick',
                '#mobile_menu',
                '#search',
                '#logo',
                '#user-slot',
                '.block-rating',
                '#footer',
                '#eps-list',
                '[class*="linkklipper"]'
            ];

            function hideNoise() {
                HIDE_SELECTORS.forEach((selector) => {
                    document.querySelectorAll(selector).forEach((node) => {
                        node.setAttribute('data-tiny-cinema-hidden', '1');
                        node.style.setProperty('display', 'none', 'important');
                    });
                });
            }

            function ensureStyle() {
                let style = document.getElementById('tiny-cinema-style');
                if (style) return style;

                style = document.createElement('style');
                style.id = 'tiny-cinema-style';
                style.textContent = `
                    html.tiny-cinema-pending,
                    html.tiny-cinema-pending body {
                        opacity: 0 !important;
                        visibility: hidden !important;
                    }

                    html.tiny-cinema-ready,
                    html.tiny-cinema-ready body {
                        opacity: 1 !important;
                        visibility: visible !important;
                    }

                    body.tiny-cinema-mode #play-now .heading-name,
                    body.tiny-cinema-mode #play-now h2,
                    body.tiny-cinema-mode #play-now .description {
                        display: none !important;
                    }

                    html, body, #app, #wrapper, #main-wrapper, .detail_page, .detail_page-watch {
                        background: #070707 !important;
                        margin: 0 !important;
                        padding: 0 !important;
                    }

                    body.tiny-cinema-mode {
                        overflow-x: hidden !important;
                    }

                    body.tiny-cinema-mode #main-wrapper,
                    body.tiny-cinema-mode .detail_page,
                    body.tiny-cinema-mode .detail_page .container,
                    body.tiny-cinema-mode #mid {
                        max-width: 100% !important;
                        width: 100% !important;
                        padding: 0 !important;
                        margin: 0 !important;
                    }

                    body.tiny-cinema-mode #tiny-cinema-shell {
                        min-height: calc(100vh - 8px);
                        display: flex;
                        flex-direction: column;
                        align-items: center;
                        justify-content: center;
                        padding: 24px 20px 16px;
                        box-sizing: border-box;
                    }

                    body.tiny-cinema-mode #tiny-cinema-title {
                        color: #f5f5f5;
                        font-family: "Segoe UI", system-ui, sans-serif;
                        font-size: 22px;
                        font-weight: 600;
                        letter-spacing: 0.2px;
                        margin: 0 0 14px 0;
                        text-align: center;
                        text-shadow: 0 2px 18px rgba(0,0,0,0.45);
                    }

                    body.tiny-cinema-mode #watch {
                        width: min(1200px, 100%) !important;
                        margin: 0 auto !important;
                        display: flex !important;
                        flex-direction: column !important;
                        gap: 12px !important;
                    }

                    body.tiny-cinema-mode #watch > .col-md-9,
                    body.tiny-cinema-mode #watch > .col-md-8,
                    body.tiny-cinema-mode #watch > .col-md-7 {
                        flex: 0 0 auto !important;
                        width: 100% !important;
                        max-width: 100% !important;
                        padding: 0 !important;
                        order: 1 !important;
                    }

                    body.tiny-cinema-mode #watch > .col-md-3,
                    body.tiny-cinema-mode #watch > .col-md-4 {
                        position: static !important;
                        width: 100% !important;
                        max-width: 100% !important;
                        padding: 0 !important;
                        order: 2 !important;
                        opacity: 1 !important;
                    }

                    body.tiny-cinema-mode #playo-now,
                    body.tiny-cinema-mode #play-now {
                        border-radius: 16px !important;
                        overflow: hidden !important;
                        box-shadow: 0 28px 80px rgba(0, 0, 0, 0.55) !important;
                        background: #000 !important;
                        min-height: min(68vh, 720px) !important;
                    }

                    body.tiny-cinema-mode #playit,
                    body.tiny-cinema-mode #playo-now iframe,
                    body.tiny-cinema-mode .embed-responsive-item {
                        width: 100% !important;
                        height: 100% !important;
                        min-height: min(68vh, 720px) !important;
                        border: 0 !important;
                        background: #000 !important;
                    }

                    body.tiny-cinema-mode .list-srv {
                        border-radius: 12px !important;
                        overflow: hidden !important;
                        box-shadow: 0 8px 28px rgba(0,0,0,0.35) !important;
                        max-height: none !important;
                        background: #121212 !important;
                    }

                    body.tiny-cinema-mode .list-srv .card-header {
                        font-size: 12px !important;
                        padding: 10px 12px !important;
                        display: flex !important;
                        align-items: center !important;
                        flex-wrap: wrap !important;
                        gap: 8px !important;
                        background: #161616 !important;
                        border-bottom: 1px solid #2a2a2a !important;
                    }

                    body.tiny-cinema-mode .list-srv .dropdown-menu {
                        max-height: 180px;
                        overflow-y: auto;
                    }

                    body.tiny-cinema-mode #play-now .dp-w-c-play,
                    body.tiny-cinema-mode .dp-w-c-play {
                        transform: scale(1.05);
                    }

                    body.tiny-cinema-mode [data-tiny-cinema-hidden="1"] {
                        display: none !important;
                    }
                `;
                document.documentElement.appendChild(style);
                return style;
            }

            function ensureShell() {
                const mid = document.getElementById('mid') || document.querySelector('.detail_page-watch');
                if (!mid || mid.closest('#tiny-cinema-shell')) return mid;

                const shell = document.createElement('div');
                shell.id = 'tiny-cinema-shell';
                mid.parentElement?.insertBefore(shell, mid);
                shell.appendChild(mid);
                return mid;
            }

            function ensureTitle(title) {
                const shell = document.getElementById('tiny-cinema-shell');
                if (!shell || !title) return;

                let titleNode = document.getElementById('tiny-cinema-title');
                if (!titleNode) {
                    titleNode = document.createElement('h1');
                    titleNode.id = 'tiny-cinema-title';
                    shell.insertBefore(titleNode, shell.firstChild);
                }

                titleNode.textContent = title;
            }

            function startObserver() {
                if (window.__tinyCinemaObserver) return;

                window.__tinyCinemaObserver = new MutationObserver(() => {
                    if (!document.body.classList.contains('tiny-cinema-mode')) return;
                    hideNoise();
                });

                window.__tinyCinemaObserver.observe(document.documentElement, {
                    childList: true,
                    subtree: true
                });
            }

            window.tinyCinemaSelectServer1 = () => {
                try {
                    if (typeof Cookies !== 'undefined') {
                        Cookies.set('srv', 1, { sameSite: 'lax' });
                    }
                } catch (_) {}

                try {
                    if (typeof srv !== 'undefined') {
                        srv = 1;
                    }
                } catch (_) {}

                const serverButton = document.getElementById('srv-1');
                if (serverButton && !serverButton.disabled) {
                    serverButton.click();
                    return 'clicked-srv-1';
                }

                return serverButton ? 'srv-1-present' : 'srv-1-missing';
            };

            window.tinyCinemaSetMode = (enabled, title, reveal) => {
                ensureStyle();
                if (enabled) {
                    document.documentElement.classList.add('tiny-cinema-pending');
                    document.documentElement.classList.remove('tiny-cinema-ready');
                    document.body.classList.add('tiny-cinema-mode');
                    ensureShell();
                    ensureTitle(title || document.title || '');
                    hideNoise();
                    startObserver();

                    if (reveal) {
                        document.documentElement.classList.remove('tiny-cinema-pending');
                        document.documentElement.classList.add('tiny-cinema-ready');
                    }

                    return 'cinema-on';
                }

                document.body.classList.remove('tiny-cinema-mode');
                document.documentElement.classList.remove('tiny-cinema-pending');
                document.documentElement.classList.add('tiny-cinema-ready');
                document.querySelectorAll('[data-tiny-cinema-hidden="1"]').forEach((node) => {
                    node.style.removeProperty('display');
                    node.removeAttribute('data-tiny-cinema-hidden');
                });
                return 'cinema-off';
            };

            window.tinyCinemaReveal = () => {
                document.documentElement.classList.remove('tiny-cinema-pending');
                document.documentElement.classList.add('tiny-cinema-ready');
                return 'revealed';
            };

            document.documentElement.classList.add('tiny-cinema-pending');
        })();
        """;

    public static string BuildPrepareScript(string movieTitle)
    {
        var titleJson = JsonSerializer.Serialize(movieTitle);
        return $$"""
            (() => {
                if (typeof window.tinyCinemaSelectServer1 === 'function') {
                    window.tinyCinemaSelectServer1();
                }
                if (typeof window.tinyCinemaSetMode === 'function') {
                    window.tinyCinemaSetMode(true, {{titleJson}}, false);
                }
                return 'prepared';
            })();
            """;
    }

    public static string BuildRevealScript(string movieTitle)
    {
        var titleJson = JsonSerializer.Serialize(movieTitle);
        return $$"""
            (() => {
                if (typeof window.tinyCinemaSetMode === 'function') {
                    window.tinyCinemaSetMode(true, {{titleJson}}, true);
                } else if (typeof window.tinyCinemaReveal === 'function') {
                    window.tinyCinemaReveal();
                }
                return 'revealed';
            })();
            """;
    }

    public static string BuildSetModeScript(bool enabled, string movieTitle, bool reveal = false)
    {
        var titleJson = JsonSerializer.Serialize(movieTitle);
        return $"window.tinyCinemaSetMode({(enabled ? "true" : "false")}, {titleJson}, {(reveal ? "true" : "false")});";
    }

    public static string BuildVerifyScript()
    {
        return """
            (() => {
                const cinemaOn = document.body.classList.contains('tiny-cinema-mode');
                const headerHidden = !document.getElementById('header')
                    || getComputedStyle(document.getElementById('header')).display === 'none';
                return cinemaOn && headerHidden ? 'ok' : 'missing';
            })()
            """;
    }

    public const string SelectServer1Script = "window.tinyCinemaSelectServer1 && window.tinyCinemaSelectServer1();";

    public const string IframeMinimalCss = """
        body { margin: 0 !important; background: #000 !important; overflow: hidden !important; }
        .jwplayer, .jw-reset, video { max-width: 100% !important; }
        """;

    public static string BuildInjectIframeCssScript()
    {
        var cssJson = JsonSerializer.Serialize(IframeMinimalCss);
        return $$"""
            (() => {
                const css = {{cssJson}};
                let style = document.getElementById('tiny-cinema-iframe-style');
                if (!style) {
                    style = document.createElement('style');
                    style.id = 'tiny-cinema-iframe-style';
                    document.head.appendChild(style);
                }
                style.textContent = css;
            })();
            """;
    }
}
