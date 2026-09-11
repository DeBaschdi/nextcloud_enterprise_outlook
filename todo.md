# My Nextcloud in Outlook

The Outlook flow should match the Thunderbird sharing window wherever native Outlook controls allow it. This work covers local files and the user's own Nextcloud. Other VFS providers are not part of it.

- [ ] Start the queue step with the same “Build share” heading and a clearly labelled destination folder. Show the complete destination path below it, not only the configured base folder.
- [ ] Use the same visible wording as Thunderbird for the source buttons, queue summary, source headings, file status and footer buttons in every language. Outlook only leaves out “Other source”.
- [ ] Replace the current flat file list with a queue split into “Local” and “My Nextcloud”. Keep the `+ Local` and `+ My Nextcloud` source buttons above it, each with file and folder choices.
- [ ] Show folders as an expandable tree. Files need matching icons, their size and status, and a remove button.
- [ ] Match Thunderbird's summary row: entries, sources and total size on the left; storage limit and occupied Nextcloud storage on the right.
- [ ] Match the queue states shown in Thunderbird: selected folders start expanded, files show “Waiting” before upload, and Back, Upload, Next and Cancel follow the same enabled states.
- [x] Build a Nextcloud file and folder picker. Match Thunderbird's path, search, name, size, modified date, file icons, preview state and storage display.
- [x] Copy selected Nextcloud files and folders into the share on the server. Leave the originals untouched and do not download them first.
- [x] Handle local and Nextcloud items in the same share. Duplicate names, empty folders, progress, cancellation and cleanup must keep working.
- [ ] Keep attachment automation on the same queue.
- [ ] Add every new text to all existing translations.
- [ ] Check keyboard use and both light and dark Windows themes.
- [ ] Update the user and development documentation.
- [ ] Run the tests and build, then verify that the work folder and feature branch match.
