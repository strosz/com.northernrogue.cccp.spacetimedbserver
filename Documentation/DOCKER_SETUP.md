# Docker Setup Guide for SpacetimeDB Unity Server Manager

## Overview

The Docker setup allows you to run SpacetimeDB in containers, providing a consistent environment across Windows, macOS, and Linux platforms.

## Prerequisites

1. **Docker Desktop**: Required for running Docker containers
   - Download from: https://www.docker.com/products/docker-desktop/
   - Includes Docker Engine and Docker Compose

## SpacetimeDB Docker Image Requirements

The official SpacetimeDB Docker image (`spacetimedb/spacetimedb`) includes:

- **SpacetimeDB Server**: The core database server
- **SpacetimeDB CLI**: Command-line interface for managing databases and modules

## Optional Language Support

Depending on your module's programming language, you may need additional tools:

### For Rust Modules
- Rust toolchain (rustc, cargo)
- Included in most SpacetimeDB images

### For C# Modules
- .NET SDK 8.0 or later
- May need to extend the base Docker image

### For TypeScript Modules
- Node.js and npm
- May need to extend the base Docker image

### WebAssembly Optimization
- **Binaryen** (wasm-opt): For optimizing WebAssembly modules
- Recommended for production deployments

## Getting Started

1. **Install Docker Desktop**
   - Use the "Install Docker Desktop" option in the Docker Setup tab
   - Follow the installation wizard
   - Restart your computer if prompted

2. **Pull SpacetimeDB Image**
   - Use the "Pull SpacetimeDB Docker Image" option
   - This downloads the official image from Docker Hub

3. **Generate Docker Compose Configuration**
   - Use the "Generate Docker Compose YAML" option
   - Select your SpacetimeDB server directory
   - Save the generated `docker-compose.yml` file

4. **Start SpacetimeDB**
   ```bash
   # Navigate to the directory with docker-compose.yml
   cd /path/to/your/docker-compose/directory
   
   # Start the container
   docker-compose up -d
   ```

5. **Verify Running**
   - SpacetimeDB will be available on `http://localhost:3000`
   - Check container status: `docker-compose ps`
   - View logs: `docker-compose logs -f`

## Docker Compose Configuration

The generated `docker-compose.yml` includes:

- **Port Mapping**: Host port 3000 → Container port 3000
- **Volume Mounting**: Your server directory mounted to `/app` in the container
- **Persistent Data**: SpacetimeDB data stored in a named volume
- **Auto-restart**: Container restarts unless manually stopped

## Managing the Container

### Start the Container
```bash
docker-compose up -d
```

### Stop the Container
```bash
docker-compose down
```

### View Logs
```bash
docker-compose logs -f spacetimedb
```

### Restart the Container
```bash
docker-compose restart
```

### Update the Image
```bash
docker-compose pull
docker-compose up -d
```

## Extending the Docker Image

If you need additional tools (e.g., .NET SDK for C# modules), create a custom Dockerfile:

```dockerfile
FROM spacetimedb/spacetimedb:latest

# Add .NET SDK for C# modules
RUN apt-get update && \
    apt-get install -y wget && \
    wget https://dot.net/v1/dotnet-install.sh && \
    chmod +x dotnet-install.sh && \
    ./dotnet-install.sh --channel 8.0 && \
    rm dotnet-install.sh

# Add Binaryen for wasm optimization
RUN apt-get install -y curl && \
    curl -L https://github.com/WebAssembly/binaryen/releases/download/version_123/binaryen-version_123-x86_64-linux.tar.gz | \
    tar xz -C /usr/local --strip-components=1

CMD ["spacetime", "start", "--listen-addr", "0.0.0.0:3000"]
```

Then update your `docker-compose.yml`:
```yaml
services:
  spacetimedb:
    build: .
    # ... rest of configuration
```

## Distribution Type Compatibility

### Asset Store Build
- **Available**: Docker Setup, Custom Remote Setup
- **Not Available**: WSL Local Setup (Windows-specific)
- Docker is the recommended cross-platform solution

### GitHub Build
- **Available**: WSL Local Setup, Docker Setup, Custom Remote Setup
- All installation methods available for development

## Troubleshooting

### Docker Desktop Not Starting
- Ensure virtualization is enabled in BIOS
- Check Windows Subsystem for Linux (WSL 2) is installed on Windows
- Restart Docker Desktop service

### Container Won't Start
- Check Docker Desktop is running
- Verify port 3000 is not in use: `netstat -an | grep 3000`
- Check container logs: `docker-compose logs spacetimedb`

### Module Publishing Issues
- Ensure WSL or Docker setup is complete for CLI access
- Verify SpacetimeDB CLI is accessible in the container
- Check module directory is correctly mounted

## Integration with Unity Server Manager

The Docker Setup tab in the Unity Server Manager provides:
1. **Automated Prerequisites Checking**: Verifies Docker and Docker Compose installation
2. **Image Management**: Pull and manage SpacetimeDB Docker images
3. **Configuration Generation**: Creates docker-compose.yml with your settings
4. **Unity SDK**: Installs required Unity components for SpacetimeDB

## Notes

- Docker mode is fully supported regardless of distribution type (Asset Store or GitHub)
- The SpacetimeDB Unity SDK is required for both Asset Store and GitHub builds
- Building and running Docker containers is compliant with both distribution types
