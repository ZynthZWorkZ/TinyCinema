# TinyCinema User Guide

TinyCinema is a desktop app for browsing, organizing, and watching movies and TV shows from your personal catalog. This guide explains every part of the app .
---

## Getting Started

### What you need

- **Windows** (TinyCinema is built for Windows , Mac Soon )
- **Microsoft Edge WebView2 Runtime** — required for the built-in In-App Browser player. If playback does not start, install WebView2 from Microsoft’s website.
- **A video player** (optional, depending on your settings):
  - **In-App Browser** — watch inside TinyCinema (recommended for most users)
  - **VLC** — used automatically if installed at `C:\Program Files\VideoLAN\VLC`
  - **FFPLAY** — part of FFmpeg, which also needs to eb installed on your system

### First launch

1. Open **TinyCinema**.
2. The app loads your movie and TV show lists from text files on your computer (see **Catalog files** in Settings).
3. Click **Settings** in the sidebar to choose your theme, default player, and other preferences. Changes save automatically.

<!-- IMAGE: Full main window on first launch — sidebar, search bar, hero area, and poster grid -->
![TinyCinema main window]()

---

## Main Window Overview

The main window has three areas:

| Area | What it does |
|------|----------------|
| **Sidebar (left)** | Switch between Movies, TV Shows, Explore, Favorites, and Settings |
| **Top bar** | Search, filters, shuffle, sort, and window controls |
| **Main content** | A large **hero banner** for the selected title, plus a **poster grid** or **Explore rows** below |

<!-- IMAGE: Labeled screenshot of main window — sidebar, search, hero panel, poster grid -->
![Main window layout]()

---

## Sidebar Navigation

### Movies

Shows every movie in your catalog as poster cards. Click a poster to select it — details appear in the hero banner above the grid.

<!-- IMAGE: Movies section with poster grid and hero panel filled in -->
![Movies view]()

### TV Shows

Same layout as Movies, but for TV series. When you select a show, the hero panel shows show info. Use **Play** to open the episode player, or **Continue** if you have watch history for that show.

<!-- IMAGE: TV Shows section with a series selected in the hero panel -->
![TV Shows view]()

### Explore

Personalized recommendation rows based on what you watch and favorite. Scroll horizontally through each row to discover titles. The more you use the app (Play, Favorite, Continue), the better Explore gets.

If you are new, Explore may show a **Discover** row or a message asking you to watch and favorite a few titles first.

<!-- IMAGE: Explore page with horizontal carousel rows -->
![Explore view]()

### Favorites

Only titles you have marked with the **Favorite** button on the hero panel.

<!-- IMAGE: Favorites view showing favorited posters -->
![Favorites view]()

### Settings

Opens the settings window. All options are explained in the **Settings** section below.

---

## Search and Filters

### Search bar

Type in the search box at the top to filter by title. Results update as you type. Works on Movies, TV Shows, and Favorites views.

<!-- IMAGE: Search bar with typed query and filtered results -->
![Search]()

### Genre filter

Dropdown next to the search bar. Narrow the list to one genre (for example Action, Comedy, Drama).

### Country filter

Second dropdown. Filter titles by country of origin.

---

## Title Bar Buttons

These small icons sit in the top-right corner of the main window:

| Button | What it does |
|--------|----------------|
| **Shuffle** (random icon) | Randomizes the order of titles in the current list |
| **Sort** (sort icon) | Opens a menu to sort by **Year** (oldest/newest first) or **Genre** (A–Z / Z–A) |
| **Minimize** | Minimize the window |
| **Maximize** | Maximize or restore the window |
| **Close** | Close TinyCinema |

---

## Hero Panel (Selected Title)

When you click a poster, the large banner at the top fills in with that title’s info: name, year, type (Movie or TV Show), genre, runtime, country, and description.

### Hero buttons

| Button | What it does |
|--------|----------------|
| **Play** | Opens the built-in player for this title |
| **Continue** | *(TV shows only, when available)* Resumes the last season and episode you watched |
| **Favorite** | Adds or removes the title from your Favorites list. The heart icon fills in when favorited |
| **Trailer** | *(When available)* Plays the official trailer. Requires a TMDB API key in Settings |
| **Opening Credits** | *(Some titles only)* Opens a separate clip for opening credits |
| **More Info** | Opens a detailed window with cast, genres, and full description |
| **URL** | Shows the source web address for this title. You can copy it or open it in your browser |
| **Roku** | Shows instructions for streaming to a Roku device via the player’s Live URLs panel |

<!-- IMAGE: Hero panel close-up with all visible action buttons labeled -->
![Hero panel buttons]()

---

## More Info Window

Opened from **More Info** on the hero panel. Shows:

- Poster and backdrop
- Full description
- Genres and release info
- Cast list with photos (when available)

Close the window with the **X** in the top-right corner.

<!-- IMAGE: More Info / Details window for a movie or TV show -->
![More Info window]()

---

## Built-In Player (In-App Browser)

When you click **Play**, TinyCinema opens the player window. This is where you actually watch content.

### Movies

- The player loads the title in a clean **cinema view** that hides most of the website clutter and focuses on the video.
- At the bottom you can switch the video source between **TinyZone** and **MovieLair** (MovieLair needs a TMDB API key in Settings).

### TV Shows

- The **Episodes** panel opens on the right (tab labeled **EPS**).
- Pick a **season** from the dropdown, then click an episode to play it.
- The player loads only the video embed — not the full streaming site.
- Your last-watched episode is remembered for **Continue** on the main screen.

<!-- IMAGE: Player window showing video with Episodes panel open for a TV show -->
![TV player with episodes]()

<!-- IMAGE: Player window in cinema mode for a movie -->
![Movie player]()

### Player footer controls

| Control | What it does |
|---------|----------------|
| **Status text (left)** | Short message about what the player is doing (loading, listening for streams, etc.) |
| **Source dropdown** *(movies only)* | Switch between **TinyZone** and **MovieLair** |
| **Cinema** toggle *(movies, TinyZone only)* | **On** — minimal player-only view. **Off** — shows the full web page |
| **Refresh** | Reloads the current page or episode |

### Side panels

Two tabs on the right edge of the player:

#### EPS (Episodes) — TV shows only

- Lists all episodes for the selected season.
- Click any episode to switch playback.
- Use the season dropdown to change seasons.
- Click the arrow at the top to hide the panel.

#### URLs (Live URLs)

Shows network addresses the player detects while you watch. Useful for opening a stream in an external player, downloading, or sending to Roku.

| Control | What it does |
|---------|----------------|
| **Capture URLs** | Turn listening on or off. When on, new URLs appear as the page loads |
| **HLS streams only** | When on, hides non-stream URLs so you only see playable video links |
| **Search box** | Filter the URL list by typing |
| **Clear** | Remove all captured URLs from the list |

Each captured URL row may show these action icons:

| Icon | What it does |
|------|----------------|
| **Play** | Open that stream in your default external player (from Settings) |
| **Download** | Download or clip the stream using FFmpeg |
| **TV (Roku)** | Send the stream to your Roku (requires Roku settings — see below) |
| **Copy** | Copy the URL to your clipboard |

<!-- IMAGE: Live URLs panel open with HLS stream entries and action icons -->
![Live URLs panel]()

---

## Settings

Open **Settings** from the sidebar. Every change saves automatically when you adjust it.

<!-- IMAGE: Full Settings window -->
![Settings window]()

### Appearance

**Theme** — Pick a color theme for the app. Several options are available (Black, Red, Midnight Blue, Emerald, Purple, and more). The change applies immediately.

<!-- IMAGE: Theme dropdown in Settings -->
![Theme setting]()

### Image Cache Settings

**Enable Image Caching** — When on, poster images are saved locally so they load faster next time.

**Change Location** — Choose which folder stores cached poster images.

### TV Show Cache Settings

**Enable TV Show Episode Caching** — When on, TinyCinema remembers the episode list for each TV show so you do not have to wait for it to scan again every time you open the show.

### Movie Links File Settings

Your **movie catalog** is a text file on your computer. TinyCinema reads titles, years, and links from this file.

| Button | What it does |
|--------|----------------|
| **Change Location** | Point TinyCinema to a different movie links file |
| **Fetch Movies from TinyZone** | Download a fresh movie list from TinyZone into your links file |
| **Add Movie by URL** | Manually add a single movie by pasting its watch page URL |

### TV Show Links File Settings

Same idea as movies, but for TV shows (usually sourced from MovieLair).

| Button | What it does |
|--------|----------------|
| **Change Location** | Point to a different TV show links file |
| **Fetch TV Shows from MovieLair** | Browse MovieLair categories and import shows into your catalog |
| **Add TV Show by URL** | Manually add a single show by pasting its MovieLair watch URL |

<!-- IMAGE: Fetch TV Shows dialog -->
![Fetch TV Shows dialog]()

### Player Settings

**Default Player** — Choose what opens when you click **Play** on a stream URL in the Live URLs panel:

- **In-App Browser** — Watch inside TinyCinema (default for the main **Play** button)
- **TinyPlayer** — The included lightweight player
- **FFPLAY** — FFmpeg’s player
- **VLC** — VLC Media Player

**Block Popups and Ads** — When on, the built-in browser tries to block pop-ups and ad redirects while you watch. Recommended to leave on.

**MovieLair Probe Logging** — Advanced diagnostic logging for troubleshooting playback. Leave **off** during normal use. When on, logs are saved to a `ProbeLogs` folder in your temp directory.

### TMDB Settings

**API Key** — A free key from [themoviedb.org](https://www.themoviedb.org/settings/api). Used for:

- Trailers on the hero panel
- Richer **More Info** details (cast, descriptions)
- **Explore** recommendations
- **MovieLair** as a movie playback source

Paste your key into the box and it saves automatically.

<!-- IMAGE: TMDB API key field in Settings -->
![TMDB settings]()

### Roku Sideload Settings

To play streams on a Roku TV or device:

1. Enable **Developer Mode** on your Roku (see the expandable instructions in Settings).
2. Enter your Roku’s **IP address**, **username**, and **password** (default username is often `rokudev`).
3. In the player, capture an **HLS** stream in the Live URLs panel and click the **TV** icon on that row.

<!-- IMAGE: Roku settings section with IP, username, password fields -->
![Roku settings]()

<!-- IMAGE: Roku sideload in action on TV or success message -->
![Roku sideload]()

---

## Typical Workflows

### Watch a movie

1. Open **Movies** in the sidebar.
2. Search or browse until you find a title.
3. Click the poster, then click **Play**.
4. Wait for the player to load. Use **Cinema** mode for a clean view.
5. If needed, switch source to **MovieLair** in the player footer (requires TMDB key).

### Watch a TV show

1. Open **TV Shows** in the sidebar.
2. Select a show and click **Play** (or **Continue** to resume).
3. In the player, pick a season and episode from the **EPS** panel.
4. Click another episode anytime to switch.

### Add something new to your catalog

1. Open **Settings**.
2. Under **Movie Links** or **TV Show Links**, click **Add by URL** and paste a watch link.
   - Or use **Fetch Movies** / **Fetch TV Shows** to import many titles at once.

### Send video to your Roku

1. Set up **Roku Sideload Settings** (see above).
2. **Play** a title in the built-in player.
3. Start playback on the page so an **HLS** stream appears in the **URLs** panel.
4. Click the **TV** icon on that stream row.

### Download a stream

1. **Play** a title and wait for an **HLS** entry in the **URLs** panel.
2. Click the **Download** icon on that row.
3. Follow the download options dialog to save the file.

<!-- IMAGE: Download dialog or progress window -->
![Download flow]()

---

## Tips

- **Posters loading slowly?** Turn on **Image Caching** in Settings and pick a folder on a fast drive.
- **Explore looks empty?** Watch and favorite a few titles first — recommendations improve with use.
- **MovieLair movies not loading?** Add your **TMDB API key** in Settings and select **MovieLair** in the player source dropdown.
- **TV show episodes slow to appear?** Enable **TV Show Episode Caching** in Settings. The first scan takes longer; later visits are much faster.
- **Pop-ups while watching?** Keep **Block Popups and Ads** enabled in Player Settings.

---

## Quick Reference

| I want to… | Do this… |
|------------|----------|
| Find a title | Search bar, or browse Movies / TV Shows |
| Watch something | Select it, click **Play** |
| Resume a TV show | Select the show, click **Continue** |
| Save a title for later | Click **Favorite** on the hero panel |
| See cast and details | Click **More Info** |
| Change app colors | Settings → **Theme** |
| Change external player | Settings → **Default Player** |
| Import more titles | Settings → **Fetch Movies** or **Fetch TV Shows** |
| Stream to Roku | Settings → Roku credentials, then **TV** icon on an HLS URL in the player |
| Get trailers | Settings → add **TMDB API key**, then **Trailer** on the hero panel |

---

Enjoy your library with TinyCinema.
