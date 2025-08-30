# Third Party Notices

**Cosmos Cove Control Panel for Unity**  
*Northern Rogue Games*

---

## Important Notice

This Unity package provides **automated installation tools** for SpacetimeDB development environment setup. **No third-party software is bundled or distributed with this asset.** All external software is downloaded from official sources during the optional installation process.

By using the automated installer features, you acknowledge and agree to the respective license terms of any downloaded software.

---

## Third-Party Software Downloaded During Installation

### 1. Windows Subsystem for Linux (WSL)
- **Publisher:** Microsoft Corporation
- **License:** Microsoft Software License Terms
- **Source:** Official Microsoft channels
- **Purpose:** Provides Linux environment on Windows for SpacetimeDB server
- **Download URL:** Installed via Windows Features or Microsoft Store
- **Documentation:** https://docs.microsoft.com/en-us/windows/wsl/

### 2. Debian Linux Distribution
- **Publisher:** Debian Project
- **License:** Various open-source licenses (primarily GPL, LGPL, BSD)
- **Source:** Microsoft Store / Official Debian repositories
- **Purpose:** Linux operating system for WSL environment
- **Download URL:** Microsoft Store or official Debian repositories
- **Documentation:** https://www.debian.org/legal/licenses/

### 3. SpacetimeDB Server
- **Publisher:** Clockwork Labs
- **License:** SpacetimeDB Commercial License
- **Source:** Official SpacetimeDB installation script
- **Purpose:** Core database server for real-time multiplayer applications
- **Download URL:** https://install.spacetimedb.com
- **Documentation:** https://spacetimedb.com/docs

### 4. .NET SDK 8.0
- **Publisher:** Microsoft Corporation
- **License:** MIT License
- **Source:** Official Microsoft installation script
- **Purpose:** Software development kit for .NET applications and SpacetimeDB C# modules
- **Download URL:** via `apt install dotnet-sdk-8.0`
- **Documentation:** https://github.com/dotnet/core/blob/main/LICENSE.TXT

### 5. Rust Programming Language & Cargo
- **Publisher:** Mozilla Foundation / Rust Foundation
- **License:** MIT License and Apache License 2.0
- **Source:** Official Rust installation script
- **Purpose:** Programming language and package manager for SpacetimeDB modules
- **Download URL:** https://sh.rustup.rs
- **Documentation:** https://forge.rust-lang.org/infra/channel-layout.html#license

### 6. Git Version Control System
- **Publisher:** Git Project / Software Freedom Conservancy
- **License:** GNU General Public License v2.0
- **Source:** Official Debian package repositories
- **Purpose:** Version control for SpacetimeDB module development
- **Installation:** via `apt install git`
- **Documentation:** https://git-scm.com/about/free-and-open-source

### 7. cURL Data Transfer Tool
- **Publisher:** Daniel Stenberg and contributors
- **License:** MIT/X derivate license (curl license)
- **Source:** Official Debian package repositories
- **Purpose:** Command-line tool for downloading installation scripts
- **Installation:** via `apt install curl`
- **Documentation:** https://curl.se/docs/copyright.html

### 8. Binaryen WebAssembly Toolkit
- **Publisher:** WebAssembly Community Group
- **License:** Apache License 2.0
- **Source:** Official Debian package repositories
- **Purpose:** WebAssembly optimization tools for SpacetimeDB
- **Installation:** via `apt install binaryen`
- **Documentation:** https://github.com/WebAssembly/binaryen/blob/main/LICENSE

---

## License Compatibility

All downloaded software licenses have been reviewed for compatibility:

- **Microsoft WSL:** Proprietary license, no redistribution concerns
- **SpacetimeDB:** Commercial license, downloaded from official source
- **.NET SDK (MIT):** Requires attribution (provided in documentation)
- **Rust (MIT/Apache 2.0):** Requires attribution (provided in documentation)
- **Git (GPL v2.0):** Not redistributed, user downloads directly
- **cURL (MIT-style):** Permissive license, no redistribution
- **Binaryen (Apache 2.0):** Requires attribution (provided in documentation)

**Note:** GPL and Apache 2.0 licensed software is downloaded by users directly from official sources and is not redistributed with this Unity package.

---

## Attribution Requirements

For software requiring attribution when used:

### .NET SDK
```
.NET is licensed under the MIT License
Copyright (c) .NET Foundation and Contributors
```

### Rust Programming Language
```
Rust is licensed under both the MIT license and Apache License 2.0.
Copyright (c) 2010-2025 The Rust Project Developers
```

### Binaryen WebAssembly Toolkit
```
Licensed under the Apache License, Version 2.0
Copyright (c) 2015-2025 WebAssembly Community Group participants
```

---

## Manual Installation Alternative

Complete manual installation instructions are provided in the package documentation as an alternative to the automated installer. Users may choose to install all required software manually if they prefer to manage licenses independently.

---

## Contact Information

For questions regarding third-party software licensing or this notice:

- **Package Author:** Mathias Toivonen, Northern Rogue Games
- **Support:** mathias@northernrogue.se
- **Documentation:** [Online Documentation](https://docs.google.com/document/d/1HpGrdNicubKD8ut9UN4AzIOwdlTh1eO4ampZuEk5fM0/edit?tab=t.0#heading=h.idmhrresk20g)

---

**Last Updated:** August 30, 2025  
**Package Version:** 0.4.2

---

*This notice is provided in compliance with Unity Asset Store guidelines section 1.2.a regarding third-party component disclosure.*