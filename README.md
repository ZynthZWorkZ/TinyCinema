# TinyCinema

Desktop app for browsing, organizing, and watching movies and TV shows from your personal catalog.

## Requirements

- **Windows** (Mac support planned)
- [WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) — required for the built-in player
- Optional external players: **VLC**, **FFPLAY** (FFmpeg), or **TinyPlayer**

## Run

```bash
dotnet run
```

## Build

```bash
dotnet publish -c Release
```

Output is a self-contained Windows executable in `bin/Release/net9.0-windows/win-x64/publish/`.

---

## Getting Started

1. Launch TinyCinema.
2. Open **Settings** to point at your movie and TV show catalog files, pick a theme, and choose a default player.
3. Browse **Movies** or **TV Shows**, select a title, and click **Play**.

Catalog data lives in text files on your machine. You can import titles via **Fetch Movies** / **Fetch TV Shows** in Settings, or add individual entries by URL.

---

## Main Sections

| Section | Description |
|---------|-------------|
| **Movies** | Browse your movie catalog as poster cards |
| **TV Shows** | Browse TV series; resume with **Continue** when watch history exists |
| **Explore** | Recommendation rows based on what you watch and favorite |
| **Favorites** | Titles marked with the heart button on the hero panel |
| **Settings** | Theme, players, catalog files, caching, TMDB, Roku |

Use the **search bar** and **genre/country filters** at the top to narrow results. **Shuffle** and **Sort** (year or genre) are in the title bar.

---

## Hero Panel

Click a poster to show details in the banner above the grid: title, year, genre, runtime, description, and action buttons.

| Button | Action |
|--------|--------|
| **Play** | Open the built-in player |
| **Continue** | Resume last watched episode (TV shows) |
| **Favorite** | Add or remove from Favorites |
| **Trailer** | Play trailer (requires TMDB API key) |
| **More Info** | Cast, genres, and full description |
| **URL** | View or copy the source link |

---

## Player

The built-in browser player opens when you click **Play**.

**Movies** — Cinema mode hides site clutter. Switch source between **TinyZone** and **MovieLair** (MovieLair needs a TMDB key).

**TV Shows** — Use the **EPS** panel to pick a season and episode. Watch history is saved for **Continue**.

**Live URLs panel** — Captures stream URLs while you watch. Open in an external player, download via FFmpeg, copy, or send to Roku.

Footer controls: source dropdown (movies), **Cinema** toggle, and **Refresh**.

---

## Settings

| Area | Options |
|------|---------|
| **Appearance** | Color theme |
| **Image Cache** | Cache poster images locally for faster loading |
| **TV Show Cache** | Remember episode lists between visits |
| **Movie / TV Links** | Change catalog file location, fetch from TinyZone/MovieLair, add by URL |
| **Player** | Default external player, block popups/ads |
| **TMDB** | API key for trailers, metadata, Explore, and MovieLair playback |
| **Roku** | IP, username, and password for sideloading streams to a Roku device |

All settings save automatically.

---

## TMDB API Key

Get a free key at [themoviedb.org](https://www.themoviedb.org/settings/api). It enables:

- Trailers on the hero panel
- Richer **More Info** details
- **Explore** recommendations
- **MovieLair** as a movie playback source

Paste the key in **Settings → TMDB**.

---

## Quick Reference

| Goal | How |
|------|-----|
| Find a title | Search bar or browse Movies / TV Shows |
| Watch | Select title → **Play** |
| Resume a show | Select show → **Continue** |
| Save for later | **Favorite** on the hero panel |
| Import titles | Settings → Fetch or Add by URL |
| Stream to Roku | Configure Roku in Settings, capture HLS URL in player → TV icon |
| Change theme | Settings → Theme |
