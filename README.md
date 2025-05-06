# Cosmos Cove Control Panel for SpacetimeDB
<p align="center">
<a href="https://github.com/strosz/com.northernrogue.cccp.spacetimedbserver"><img src="https://img.shields.io/badge/Made%20with-Unity-57b9d3.svg?style=flat&logo=unity"></a>
<a href="https://github.com/strosz/com.northernrogue.cccp.spacetimedbserver"><img src="https://img.shields.io/github/v/release/strosz/com.northernrogue.cccp.spacetimedbserver?color=%23ff00a0&include_prereleases&label=version&sort=semver&style=flat-square"></a>
<a href="https://ko-fi.com/northernrogue"><img src="https://img.shields.io/badge/buy%20me%20a%20ko-fi-8A2BE2"></a>
<!-- Add other relevant badges here, e.g., license, version -->
</p>

**Bring the power of SpacetimeDB directly into your Unity Editor!**

SpacetimeDB is a revolutionary database and framework designed for building performant Massively Multiplayer Online games and applications with unprecedented ease. Cosmos Cove Control Panel is an unofficial integration that streamlines the SpacetimeDB experience within Unity, making server management a breeze.

Install directly in your Unity Package Manager with this git URL

```https://github.com/strosz/com.northernrogue.cccp.spacetimedbserver.git```

## Features

🚀 **One-Click Server Management:**
   - Start your local SpacetimeDB server silently in about three seconds.
   - Automatic pre-requisite checks ensure a smooth start.
   - No extra command-line windows needed!

🔄 **Automatic Workflow:**
   - Detects changes in your server code.
   - Auto Publish/Generate mode keeps your server up-to-date.

🌱 **Automatic Installer:**
   - Server Installer Window that installs everything necessary from the ground up.
   - Checks what you need and gives you detailed feedback on important steps.

📊 **Real-time Monitoring & Control:**
   - Monitor server status and port availability directly in Unity.
   - View real-time server logs within the editor and save them.
   - Server errors are mirrored in the Unity console for easy debugging.

💾 **Backup & Restore:**
   - Create and restore highly compressed backups of your entire server quickly.
   - Restore previous states with a single click.

🔍 **Database Browser:**
   - Get a quick overview of all your tables and columns.
   - Easily clear tables or delete specific rows.

✅ **Run Reducers:**
   - Access a list of all your SpacetimeDB reducers (server methods).
   - Call reducers directly from the Unity editor interface.

⬇️⬆️ **Data Import/Export:**
   - Export all database tables to JSON or CSV format.
   - Import single tables or entire folders in JSON or CSV format (with manual steps).

⚡ **Performance:**
   - No noticeable performance impact during editor runtime.

## Supported Platforms

*   **Windows:** Requires WSL (Windows Subsystem for Linux) with Debian.
    *   Includes setup instructions and an automatic pre-requisite check within Unity.
    *   Includes an optional automatic installer for all pre-requisites (alpha version).

## Getting Started
   - 1. Install the asset using the .git address above in the Package Manager´s + menu.
   - 2. A Welcome Window displays if successful.
   - 3. Let the Server Installer Window determine if you have everything necessary to run SpacetimeDB on Windows WSL. Install the free and publicly available software one by one starting from the top. Now you can run SpacetimeDB directly in Unity!

   **Note:** The Server Installer Window works by automatically calling install commands from public repositories for you. For a manual install process please check the documentation button available in the Welcome Window.

## Upcoming features
   - Server profiles.
   - Maincloud support.
   - Get notified and update to a new SpacetimeDB version.

## License
   - To be determined.

## Disclaimer

> This code is provided **“as is”**, without warranty of any kind, express or implied,  
> including but not limited to the warranties of merchantability, fitness for a particular  
> purpose, and noninfringement. In no event shall the authors be liable for any claim,  
> damages, or other liability arising from, out of, or in connection with the software.

   **Note:** This is an unofficial SpacetimeDB server manager. SpaceTimeDB® is a registered trademark of Clockwork Labs. This asset is neither endorsed by nor affiliated with Clockwork Labs.