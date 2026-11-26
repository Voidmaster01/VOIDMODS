# The approved mod list (`mods.json`)

The Mod Menu's **GET MODS** downloads this file from the `main` branch on GitHub and lists every mod in it.
Only mods Voidmaster01 has checked go in here.

## Adding or updating a mod

1. A modder fills in **Issues → New issue → Submit a mod**, or you release one of your own mods.
2. Check the mod (and its source code).
3. Run, from the repo folder:
   ```
   pwsh -NoProfile -File tools\add-mod.ps1 -Url <direct .dll link> -About "one line" -Page <repo or release page>
   ```
   It downloads the DLL, takes its SHA-256 fingerprint, and reads its id, version, author and the mods it needs from
   the DLL itself. To update a mod that's already listed, `-Url` is enough.
4. Commit `registry/mods.json` and push.

**Every new release needs step 3 again**, also for ModConfigs, Voidcons and ModMenu. The list holds the fingerprint of
one exact file. If you re-upload a DLL to the same release tag without updating the list, the Mod Menu refuses the
new file as "not the approved file" and installs nothing.

## Fields

| field | meaning |
|---|---|
| `id` | The name in the mod's `[MelonInfo]`. The Mod Menu uses it to find the installed copy and its version. |
| `name`, `author`, `about` | What the list shows. |
| `version` | The approved version. If it's newer than the installed one, the mod gets an UPDATE button. |
| `file` | The file name in the Mods folder (a plain `.dll` name, no folders). |
| `url` | A direct `https://` download link to exactly that file. |
| `sha256` | The fingerprint of that file. A download that doesn't match is never installed. |
| `requires` | The ids of mods it needs. They're installed along with it if they're missing. |
| `page` | A web page about the mod. |

`format` is the file's layout version. A Mod Menu that's older than the format says so instead of guessing.

## Testing without GitHub

Put a list at `<game>\UserData\ModMenu\registry-test.json` and the Mod Menu uses it instead, with a purple TEST LIST
note. Only a test list may use `file:///` links. Delete the file to go back to the real list.
