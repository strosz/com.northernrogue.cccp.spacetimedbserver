# [0.5.2] - 2025-11-01
Fix: The Main Window Pre-requisites check now also includes the SpacetimeDB SDK. 
Fix: The Welcome Window's Request Support button will now show the Support.pdf on all platforms. 
Fix: The Setup Window will show more informative status messages for not yet set up Docker items. 
Fix: The Setup Window will show improved setup instructions for Docker. 
Fix: The Publish command will show the same detailed feedback for common publishing errors in Docker mode as were already in WSL mode. 
Fix: Docker image 'latest' version tag will now map to the actual version number so that the update message will correctly identify when a new SpacetimeDB docker image update is available. Earlier it could suggest to update from latest until it got an actual version number from the first update.
Fix: The Docker image setup pull command is now set to not include the 'start' part of the command, since it would then start a randomly named container which confuses the regular start process.
Fix: Ensure the Docker server is started automatically when all pre-requisites are met.

# [0.5.1] - 2025-10-30
Fix: Manual WSL and Docker CLI update checks in the Commands section now shows a visible version status.
Fix: Rust version in WSL mode is displayed again.
Fix: SSH Keygen now also supports Linux and MacOS.

# [0.5.0] - 2025-10-28
NEW: Initial release.