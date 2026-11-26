<#
  make-release.ps1 - builds every VOIDMOD into a release folder, ready to upload to GitHub Releases.

  HOW TO RUN (PowerShell 7, from the repo folder):
    pwsh -NoProfile -File tools\make-release.ps1                 release named after today's date
    pwsh -NoProfile -File tools\make-release.ps1 -Name v1.1      your own release name
  WHAT IT NEEDS
    The same as build.ps1 (game-path.txt), plus ModConfigs.dll in the game's Mods folder, because the other mods are
    compiled against it (build-all.ps1 puts it there). The game's Mods folder itself is NOT changed by this script.

  WHAT IT MAKES, in dist\VOIDMODS-<name>\ (dist\ is in .gitignore, so builds never end up in the repo):
    ModConfigs.dll, Voidcons.dll                    one file per mod, so players pick only what they want
    VOIDMODS-all.zip                                all mods + INSTALL.txt in one download
    RELEASE-NOTES.md                                text to paste into the GitHub release: versions, needs, install steps
  THEN: on github.com open the repo -> Releases -> Draft a new release -> tag (e.g. v2026.11.24) -> drag the .dll files
  and the zip in -> paste RELEASE-NOTES.md -> Publish release.
#>
param([string]$Name = (Get-Date -Format 'yyyy.MM.dd'))
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot   # the repo folder is one level above tools\
$out = Join-Path $repo "dist\VOIDMODS-$Name"
New-Item -ItemType Directory -Force $out | Out-Null

# The mods in this release: folder name, what it needs, and one line about it. Add new VOIDMODS here.
$mods = @(
    [pscustomobject]@{ Name = 'ModConfigs';  Needs = '-';                   About = 'MOD CONFIGS settings hub on the home screen' },
    [pscustomobject]@{ Name = 'Voidcons';    Needs = 'ModConfigs';          About = 'your own round HD player icon, picked and cropped in game' },
    [pscustomobject]@{ Name = 'ModMenu';     Needs = 'ModConfigs 1.1.0';    About = 'MODS button: switch mods on/off, install and update approved mods' }
)

foreach ($m in $mods) {
    # Read the version from the mod's [assembly: MelonInfo(typeof(...), "Name", "1.2.3", ...)] line with a regex:
    #   MelonInfo\(  the attribute name and its "("     [^"]*"  skip to the first quote (the mod name follows)
    #   [^"]*",\s*"  the name, its closing quote, a comma, spaces and the next opening quote
    #   ([^"]+)      capture everything up to the next quote: the version
    $source = (Get-ChildItem "$repo\$($m.Name)\*.cs" | Get-Content -Raw) -join "`n"
    $match = [regex]::Match($source, 'MelonInfo\([^"]*"[^"]*",\s*"([^"]+)"')
    $m | Add-Member Version $(if ($match.Success) { $match.Groups[1].Value } else { '?' })
    pwsh -NoProfile -File "$repo\build.ps1" -Mod $m.Name -Out "$out\$($m.Name).dll"
    if ($LASTEXITCODE -ne 0) { Write-Output "Stopped: $($m.Name) failed to build."; exit 1 }
}

# INSTALL.txt for the zip (plain text, Windows line endings so Notepad shows it right).
$install = @(
    'VOIDMODS for FNAF Online Multiplayer'
    ''
    '1. Install MelonLoader 0.7 into the game folder and start the game once.'
    '2. Copy the .dll files you want into the game''s Mods folder.'
    '   Voidcons and ModMenu need ModConfigs. ModConfigs works on its own.'
    '3. Start the game. With ModConfigs, MOD CONFIGS is in the home screen menu.'
    ''
    'Source code, license and updates: see the VOIDMODS repository on GitHub.'
) -join "`r`n"
Set-Content "$out\INSTALL.txt" $install -Encoding ascii

# The all-in-one zip: every DLL + INSTALL.txt (Compress-Archive is PowerShell's built-in zip maker).
$zip = "$out\VOIDMODS-all.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (@($mods | ForEach-Object { "$out\$($_.Name).dll" }) + "$out\INSTALL.txt") -DestinationPath $zip

# Release notes in Markdown, with a table of the mods.
$rows = $mods | ForEach-Object { "| ``$($_.Name).dll`` | $($_.Version) | $($_.Needs) | $($_.About) |" }
$notes = @(
    "## VOIDMODS $Name"
    ''
    'Pick the mods you want: every mod is its own file. Or take `VOIDMODS-all.zip` for everything.'
    ''
    '| File | Version | Needs | What it does |'
    '|---|---|---|---|'
) + $rows + @(
    ''
    '**Install:** MelonLoader 0.7 in the game folder (start the game once), then put the `.dll` files in the game''s `Mods` folder.'
    'Voidcons icons are shared through the lobby host, so the host and everyone who wants to see icons need Voidcons.'
)
Set-Content "$out\RELEASE-NOTES.md" ($notes -join "`n") -Encoding utf8

Get-ChildItem $out | Select-Object Name, Length
"Release folder: $out"
# The Mod Menu installs only files whose fingerprint is in registry\mods.json, so every mod you re-release must be
# approved again, or players' Mod Menus refuse the new file (see registry\README.md).
'AFTER PUBLISHING: run tools\add-mod.ps1 -Url <new .dll link> for every mod you uploaded, then commit registry\mods.json.'
