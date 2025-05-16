# Cosmos Cove Control Panel for SpacetimeDB
<p align="center">
<a href="https://github.com/strosz/com.northernrogue.cccp.spacetimedbserver"><img src="https://img.shields.io/badge/Made%20with-Unity-57b9d3.svg?style=flat&logo=unity"></a>
<a href="https://github.com/strosz/com.northernrogue.cccp.spacetimedbserver"><img src="https://img.shields.io/github/v/release/strosz/com.northernrogue.cccp.spacetimedbserver?color=%23ff00a0&include_prereleases&label=version&sort=semver&style=flat-square"></a>
<a href="https://ko-fi.com/northernrogue"><img src="https://img.shields.io/badge/buy%20me%20a%20ko-fi-8A2BE2"></a>
</p>
<img src="https://northernrogue.se/cosmos_cover_new.png" alt="Alt text" width="900">

**Bring the power of SpacetimeDB directly into your Unity Editor!**

SpacetimeDB is a database and framework designed for building performant Massively Multiplayer Online games and applications with unprecedented ease. Cosmos Cove Control Panel is an unofficial that streamlines the SpacetimeDB experience within Unity for Windows, allowing anyone to get started within minutes.

Install directly in your Unity Package Manager with this git URL

```https://github.com/strosz/com.northernrogue.cccp.spacetimedbserver.git```

## Features

🚀 **Silent Server and One-Click Server Management**
   - Start your local WSL SpacetimeDB server silently in about three seconds.
   - Automatic pre-requisite checks ensure a smooth start.
   - No extra command-line windows needed!

🔄 **Automatic Workflow**
   - Detects changes in your server code as you develop.
   - Auto Publish/Generate mode keeps your server up-to-date.

🌱 **Automatic Installer**
   - Server Installer Window which installs everything necessary from the ground up.
   - If starting fresh you will have your own local SpacetimeDB server in no-time.
   - Extra compability checks for important steps.

📊 **Real-time Monitoring**
   - Monitor server status and port availability directly in Unity.
   - View real-time server logs within the editor and save them.
   - Server errors are mirrored in the Unity console for easy debugging.

💾 **Backup and Restore**
   - Create and restore highly compressed backups of your entire server.
   - Restore previous states in seconds.

🔍 **Database Browser**
   - Get a quick overview of all your tables and columns.
   - Easily clear tables or delete specific rows.

✅ **Run Reducers**
   - Access a list of all your SpacetimeDB reducers (server methods).
   - Call reducers directly from the Unity editor interface.

⬇️⬆️ **Data Import/Export**
   - Export all database tables to JSON or CSV format.
   - Import single tables or entire folders in JSON or CSV format (with manual steps).

⚡ **Performance**
   - No noticeable performance impact during editor runtime.

🔧 **Source Available, Free and soon Open Source**
   - The full source code is available on Github for free.
   - Unity Asset Store version is the same version.
   - Soon releasing it under MIT license.

## Supported Platforms

*   **Windows®:** Requires WSL (Windows Subsystem for Linux) with Debian.
    *   Includes setup instructions and an automatic pre-requisite check within Unity®.
    *   Includes an optional automatic installer for all pre-requisites required to install SpacetimeDB® (alpha version).

## Getting Started
   - Install the asset using this .git URL in the Package Manager´s + menu.<br>
   ```https://github.com/strosz/com.northernrogue.cccp.spacetimedbserver.git```
   - A Welcome Window displays if successful with a link to the Server Installer Window which will determine if you have everything necessary to run SpacetimeDB on Windows WSL. Install the listed free and publicly available software one by one starting from the top. 
   - Now you can run SpacetimeDB directly from Unity!

   **Note:** The Server Installer Window works by automatically calling install commands from public repositories for you. It creates a temporary bat file in your Windows temp folder in order to reliably run the installer process. For a manual install process please check the documentation button available in the Welcome Window.

## Upcoming Features
   - Server profiles.
   - Maincloud support.
   - Notification and updater to a new SpacetimeDB server version.

## Community Made
   - Created by a solo MMO developer.
   - If you like this asset, please consider <a href="https://ko-fi.com/northernrogue">buying me a coffee</a>.

## License
> You are free to use, copy, modify, merge, publish, distribute, sublicense, and/or sell **products and services** that incorporate or are built with this Unity editor asset.  
>  
> You may **not sell, sublicense, or distribute the Unity editor asset itself**, in whole or in substantial part, as a **standalone product**, without written permission from the original author.  
>  
> The Software is provided "as is", without warranty of any kind. In no event shall the author be liable for any claim, damages, or other liability from the use of the Software.

## Disclaimer

   **Note:** This is an unofficial SpacetimeDB server manager asset for Unity. This asset is neither endorsed by nor affiliated with Clockwork Labs.

   You are required to check that you don't break the license terms of any Software referred to in this document. Clockwork Labs allows you to run one instance of SpacetimeDB on their free tier, which this asset complies with.

**SpaceTimeDB®** is a registered trademark of Clockwork Labs. 
**Unity®** is a registered trademark of Unity Technologies.
**Windows®** is a registered trademark of Microsoft Corporation.