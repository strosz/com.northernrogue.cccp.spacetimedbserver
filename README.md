# Cosmos Cove Control Panel for SpacetimeDB
<p align="center">
<a href="https://github.com/strosz/com.northernrogue.cccp.spacetimedbserver"><img src="https://img.shields.io/badge/Made%20with-Unity-57b9d3.svg?style=flat&logo=unity"></a>
<a href="https://github.com/strosz/com.northernrogue.cccp.spacetimedbserver"><img src="https://img.shields.io/github/v/release/strosz/com.northernrogue.cccp.spacetimedbserver?color=%23ff00a0&include_prereleases&label=version&sort=semver&style=flat-square"></a>
<a href="https://ko-fi.com/northernrogue"><img src="https://img.shields.io/badge/buy%20me%20a%20ko-fi-8A2BE2"></a>
</p>
<img src="https://northernrogue.se/cosmos_cover_newupdate.png" alt="Alt text" width="900">

**Bring the power of SpacetimeDB directly into your Unity Editor!**

SpacetimeDB is a database and framework designed for building performant Massively Multiplayer Online games and applications with unprecedented ease. Cosmos Cove Control Panel is a Unity integration for SpacetimeDB, allowing anyone to get started creating an online world in a free and highly efficient way. Start experimenting locally within minutes and then publish the same project to your own custom server to host your own Massively Multiplayer Online creation for thousands of simultaneous users.

Install directly in your Unity Package Manager with this git URL

```https://github.com/strosz/com.northernrogue.cccp.spacetimedbserver.git```

## Features

🌌 **WSL Local, Custom Remote or Maincloud**
   - Use your server mode of choice.
   - Control any SpacetimeDB server running on a Debian based distro.
   - Quickly start developing using SpacetimeDB with a local WSL server then publish the exact same project to a custom server or Maincloud.

🚀 **Silent Server and One-Click Server Management**
   - Start your server silently in about three seconds.
   - Automatic pre-requisite checks ensure a smooth start.
   - No extra command-line windows needed!

🔄 **Automatic Workflow**
   - Detects changes in your server code as you develop.
   - Auto Publish/Generate mode keeps your server up-to-date.
   - Create new server modules and switch between them.

🌱 **Automatic Installer**
   - Server Installer Window which installs everything necessary from the ground up.
   - If starting fresh you will have your own local WSL or custom remote SpacetimeDB server in no-time.
   - Extra compability checks for important steps.
   - Get notified of new SpacetimeDB server updates and update it in the Server Installer Window.

📊 **Real-time Monitoring**
   - Monitor server status directly in Unity and send utility commands.
   - Server logs can be viewed in real-time within the editor and saved.
   - Server log errors are mirrored in the Unity console for easy debugging.

💾 **Backup and Restore**
   - Create and restore highly compressed backups of your entire SpacetimeDB within WSL Local.
   - Restore previous states in seconds.

🔍 **Database Browser**
   - Get a quick overview of all your tables and columns.
   - Easily clear tables or delete specific rows.

✅ **Run Reducers**
   - Access a list of all your SpacetimeDB reducers.
   - Run reducers with custom parametres directly from the Unity editor interface.

⬇️⬆️ **Data Import/Export**
   - Export all database tables to JSON or CSV format.
   - Import single tables or entire folders in JSON or CSV format (requires manual steps).

⚡ **Performance**
   - No noticeable performance impact during editor runtime.

🔧 **Source Available, Free and soon Open Source**
   - The full source code is available on Github for free.
   - Unity Asset Store version is the same version.
   - Soon releasing it under MIT license.

## Screenshots
<div align="center">
  <table style="max-width: 600px;">
    <tr>
      <td style="text-align: center;">
        <img src="https://www.northernrogue.se/CCCP/cccp_manager.png" alt="CCCP Manager" style="width: 100%; max-width: 250px; height: auto; display: block; margin-left: auto; margin-right: auto;">
      </td>
      <td style="text-align: center;">
        <img src="https://www.northernrogue.se/CCCP/cccp_logs.png" alt="CCCP Logs" style="width: 100%; max-width: 250px; height: auto; display: block; margin-left: auto; margin-right: auto;">
      </td>
      <td style="text-align: center;">
        <img src="https://www.northernrogue.se/CCCP/cccp_database.png" alt="CCCP Database" style="width: 100%; max-width: 250px; height: auto; display: block; margin-left: auto; margin-right: auto;">
      </td>
    </tr>
    <tr>
      <td style="text-align: center;">
        <img src="https://www.northernrogue.se/CCCP/cccp_reducers.png" alt="CCCP Reducers" style="width: 100%; max-width: 250px; height: auto; display: block; margin-left: auto; margin-right: auto;">
      </td>
      <td style="text-align: center;">
        <img src="https://www.northernrogue.se/CCCP/cccp_installer.png" alt="CCCP Installer" style="width: 100%; max-width: 250px; height: auto; display: block; margin-left: auto; margin-right: auto;">
      </td>
      <td style="text-align: center;">
        <img src="https://www.northernrogue.se/CCCP/cccp_settings.png" alt="CCCP Settings" style="width: 100%; max-width: 250px; height: auto; display: block; margin-left: auto; margin-right: auto;">
      </td>
    </tr>
  </table>
</div>

## Supported Platforms

*   **Windows®:** Requires WSL (Windows Subsystem for Linux) with Debian.
   *   Includes an optional automatic installer for all pre-requisites required to use SpacetimeDB® within Unity®.
   *   Custom Remote Mode installs Debian on the remote server and expects a Debian command environment. May work with other manually installed distros that are based on Debian.

## Getting Started
   - Install the asset using this .git URL in the Package Manager´s + menu.<br>
   ```https://github.com/strosz/com.northernrogue.cccp.spacetimedbserver.git```
   - A Welcome Window displays if successful with a link to the Server Installer Window which will determine if you have everything necessary to run SpacetimeDB on Windows WSL. Install the listed free and publicly available software one by one starting from the top. 
   - Now you can run SpacetimeDB directly from Unity!

   **Note:** The Server Installer Window works by automatically calling install commands from public repositories for you. It creates a temporary bat file in your Windows temp folder in order to reliably run the installer process. For a manual install process please check the documentation button available in the Welcome Window.

## Upcoming Features
   - New command interface with all commands.
   - Better user experience and tips for how to solve common STDB questions.

## Community Made
   - If you like this asset, please consider <a href="https://ko-fi.com/northernrogue">buying me a coffee</a>. It is much appreciated and allows this asset to improve further.

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