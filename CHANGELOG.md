# [0.5.7] - 2025-11-23
NEW: Identity Manager Window. Keeps track of your logged in identity and ensures that you don't publish databases with the wrong identity. Switching Docker modules now reminds you if Docker changed identity for your next publish.
NEW: Docker images are now more intelligently managed and update checks will ask to clear old images and latest image tagged images.
Fix: Shows progress bar for Docker module switching.
Fix: Docker starts server automatically when pre-req is fully met to ensure the server is ready for first new module initialization.
Fix: Init New Module opens the serverDirectory in the file explorer to clarify if it was successful and otherwise displays a message if no files were written.
Fix: ServerWindow GC alloc and Editor lag optimizations. Now uses a few bytes GC alloc instead of up to several kb.
Fix: Logout removed from commands since it was unnecessary and Login now does the logout and login process by default.

# [0.5.6] - 2025-11-11
Fix: Switching to another selected module ensures that the Docker container has the correct bindings. May fix cases where Docker didn't find the server directory.
Fix: Extra check to make sure that the set-default is correctly set on server start so the CLI is configured for local or maincloud server if having switched modes while the Docker CLI was not running.
Fix: Updated the path logic for the new changes in SpacetimeDB 1.7.0.
Fix: Reducer window now shows better information if it couldn't access the database.
NEW: Login Refresh command. This automatically logout and login again with a short interval to assist with refreshing your SSO login. If done manually SpacetimeDB may log you in again with an offline identity before you have the change to login.

# [0.5.5] - 2025-11-10
NEW: Private table names are now displayed in Database window. Dev Mode toggle in Database Window that makes private tables fully public for easier development. Check the documentation for details.
Fix: Output Log Window could cut off the latest text on the bottom at some resolutions and DPI values.
Fix: Edit Module on MacOS also opens file location and no longer shows a warning.
Fix: MacOS server restore reliability and error message fix.
Fix: Maincloud CLI warning message on some commands when in Maincloud mode.
Fix: Maincloud server mode status now persists through domain reloads like Docker and WSL server modes and restores the previous connection state, so we don't have to wait until the first status check.
Fix: Maincloud server mode now shows a Start Docker CLI button until the localCLIProvider is running when in Docker mode.
Fix: Removed auto compile after successful generate, since Unity doesn't always update the existence of the newly generated files and the compilation was unnecessary then. Unity will find the newly generated bindings.

# [0.5.4] - 2025-11-07
Fix: The saving of the settings asset may no longer show "Trying to access the DPI setting of a visual element that is not on a panel" log messages when saving scenes at the same time.
Fix: SpacetimeDB updates are not being displayed as available to update independently when using Docker. Updated the text to reflect that we need to wait for the official Docker image to update.
Fix: SpacetimeDB SDK updates are now being advertised just once per editor session.
Fix: Updated path compability so that trying to do backups on MacOS may no longer result in a command not found message.
Fix: Setup Window Debug menu tab is now only shown in debug mode.
Fix: Docker server mode now only updates the Setup Window state when Docker Desktop is running and otherwise caches the last known state.
Fix: SpacetimeDB may no longer occasionally remain being displayed as Running even if the Docker or WSL CLI provider is not when in local server mode.
Fix: Shows full path of the backup archive when doing a backup.

# [0.5.3] - 2025-11-03
Fix: MacOS additional compability fixes for Docker containers.
Fix: Docker command refactor that builds the commands with MacOS specific requirements.
Fix: Updated texts in several windows.
Fix: Docker Desktop now attempts to start on server start even if the image wasn't found the last time Setup Window was run.
Fix: Docker CLI error message fix.

# [0.5.2] - 2025-11-02
Fix: MacOS improved Docker support with some remaining methods made multiplatform and with path compability.
Fix: The Main Window Pre-requisites check now also includes the SpacetimeDB SDK.
Fix: The Welcome Window's Request Support button will show the Support.pdf on all platforms.
Fix: The Setup Window will show more informative status messages for not yet set up Docker items.
Fix: The Setup Window will show improved setup instructions for Docker.
Fix: WSL Compability tool is now accessible for the manual setup process.
Fix: The Publish command will show the same detailed feedback for common publishing errors in Docker mode as were already in WSL mode.
Fix: Docker image 'latest' version tag will now map to the actual version number so that the update message will correctly identify when a new SpacetimeDB docker image update is available. Earlier it could suggest to update from latest until it got an actual version number from the first update.
Fix: The Docker image setup pull command is now set to not include the 'start' part of the command, since it would then start a randomly named container which could confuse the regular start process.
Fix: Ensure the Docker server is started automatically when all pre-requisites are met.

# [0.5.1] - 2025-10-30
Fix: Manual WSL and Docker CLI update checks in the Commands section now shows a visible version status.
Fix: Rust version in WSL mode is displayed again.
Fix: SSH Keygen now also supports Linux and MacOS.

# [0.5.0] - 2025-10-28
NEW: Initial release.