# patch_core.ps1 - the VOIDMODS patch installer (built into every VOIDMODS-patch-<n>.bat by make-patch.ps1).
#
# WHAT IT DOES: installs one VOIDMODS patch on a player's PC: finds FNAF Online Multiplayer, checks MelonLoader is
# installed, backs up every file it is about to replace, and copies the patch's mod files into the game's Mods folder.
# HOW TO RUN: not on its own. The player double-clicks the .bat, which hands this code four variables:
#   $PayloadB64 (the zipped files as base64 text), $PatchTitle, $BatDir (the .bat's own folder).
# WHAT IT NEEDS: Windows PowerShell 5.1 (built into Windows). ASCII only, so 5.1 reads it correctly.
# NOTE: three colons in a row must never appear in this file, because the .bat uses them as section markers.
# The same idea as mothmaster's MothMaster patches ("Sleepy Patch"), made for FNAF Online + MelonLoader.

# 'Stop' turns every error into a hard stop that jumps to the catch { } block at the bottom.
$ErrorActionPreference = 'Stop'
$GameExe = 'FNAF Online Multiplayer.exe'

# Pause-Exit: wait for Enter so the player can read the messages, then end with a code (0 = OK, 1 = problem).
# VOIDMODS_NOPAUSE=1 skips the wait; it's only used for testing.
function Pause-Exit($code) {
  if ($env:VOIDMODS_NOPAUSE -ne '1') { Read-Host "`nPress Enter to close" | Out-Null }
  exit $code
}

# Find-Games: every folder under $root (at most $depth levels down) that holds the game's .exe.
# -Depth limits how deep Get-ChildItem looks, so a whole drive is searched quickly; SilentlyContinue skips
# folders we're not allowed to open. Recycle Bin copies are ignored.
function Find-Games($root, $depth) {
  if (-not $root -or -not (Test-Path $root)) { return @() }
  @(Get-ChildItem -Path $root -Filter $GameExe -File -Recurse -Depth $depth -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\\$Recycle\.Bin\\' } |
    ForEach-Object { $_.DirectoryName })
}

try {
  Write-Host "=== $PatchTitle ===`n"
  # A running game keeps its mod files locked, so they can't be replaced: stop early.
  if (Get-Process -Name 'FNAF Online Multiplayer' -ErrorAction SilentlyContinue) {
    Write-Host "FNAF Online is still open. Close it, then run this again." -ForegroundColor Yellow; Pause-Exit 1
  }

  # --- find the game ---
  # Candidates, in this order: VOIDMODS_GAME (if someone set it by hand), the folder this .bat is in (and the
  # folders around it), the usual personal folders and the Game Jolt client, then every drive (3 levels deep).
  Write-Host "Looking for FNAF Online..."
  $found = @()
  if ($env:VOIDMODS_GAME -and (Test-Path (Join-Path $env:VOIDMODS_GAME $GameExe))) { $found += $env:VOIDMODS_GAME }
  if ($found.Count -eq 0) {
    $found += Find-Games $BatDir 2
    if ($BatDir) { $found += Find-Games (Split-Path $BatDir) 2 }
    foreach ($place in @("$env:USERPROFILE\Desktop", "$env:USERPROFILE\Downloads", "$env:USERPROFILE\Documents",
                         "$env:USERPROFILE\OneDrive\Desktop", "$env:USERPROFILE\OneDrive\Documents")) { $found += Find-Games $place 4 }
    $found += Find-Games "$env:LOCALAPPDATA\game-jolt-client" 7
    # Get-PSDrive -PSProvider FileSystem lists the drives (C:, D:, ...); Used -gt 0 skips empty card readers.
    foreach ($drive in (Get-PSDrive -PSProvider FileSystem | Where-Object { $_.Used -gt 0 })) { $found += Find-Games $drive.Root 3 }
  }
  # The same folder can be found more than once (for example from the .bat folder and from the drive search):
  # Sort-Object -Unique keeps one of each. Modded copies (with a MelonLoader folder) are listed first.
  $found = @($found | Where-Object { $_ } | Sort-Object -Unique)
  $found = @($found | Sort-Object { -not (Test-Path (Join-Path $_ 'MelonLoader')) })

  $game = $null
  if ($found.Count -eq 1) { $game = $found[0] }
  elseif ($found.Count -gt 1) {
    Write-Host "`nFound FNAF Online in more than one place:"
    for ($i = 0; $i -lt $found.Count; $i++) {
      $tag = if (Test-Path (Join-Path $found[$i] 'MelonLoader')) { '  (has MelonLoader)' } else { '' }
      Write-Host ("  {0}) {1}{2}" -f ($i + 1), $found[$i], $tag)
    }
    $pick = Read-Host "Type the number of the one to install into (Enter = 1)"
    if (-not $pick) { $pick = '1' }
    # [int] turns the typed text into a number; -as [int] gives $null instead of an error for non-numbers.
    $n = $pick -as [int]
    if (-not $n -or $n -lt 1 -or $n -gt $found.Count) { Write-Host "That isn't one of the numbers." -ForegroundColor Red; Pause-Exit 1 }
    $game = $found[$n - 1]
  }
  else {
    # Not found automatically: a normal Windows "pick a folder" window (from .NET's Windows Forms).
    Write-Host "Couldn't find FNAF Online automatically. Pick the game folder (the one with '$GameExe') in the window that opens."
    Add-Type -AssemblyName System.Windows.Forms
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    $dlg.Description = "Select the FNAF Online folder (it contains $GameExe)"
    if ($dlg.ShowDialog() -ne 'OK') { Write-Host "Cancelled."; Pause-Exit 1 }
    $game = $dlg.SelectedPath
    if (-not (Test-Path (Join-Path $game $GameExe))) { Write-Host "That folder doesn't contain $GameExe." -ForegroundColor Red; Pause-Exit 1 }
  }
  Write-Host "Game: $game`n"

  # --- MelonLoader must be installed (version.dll is how it starts with the game) ---
  if (-not (Test-Path (Join-Path $game 'MelonLoader')) -or -not (Test-Path (Join-Path $game 'version.dll'))) {
    Write-Host "MelonLoader isn't installed in this game folder." -ForegroundColor Yellow
    Write-Host "Install MelonLoader 0.7 (melonwiki.xyz), start the game once, close it, then run this patch again."
    Pause-Exit 1
  }

  # --- unpack the payload ---
  # base64 text -> bytes -> p.zip -> unpacked into a temporary folder. ConvertFrom-Json reads the manifest.
  $tmp = Join-Path $game '.voidmods-tmp'; if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
  New-Item -ItemType Directory $tmp | Out-Null
  [IO.File]::WriteAllBytes("$tmp\p.zip", [Convert]::FromBase64String($PayloadB64))
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  [IO.Compression.ZipFile]::ExtractToDirectory("$tmp\p.zip", "$tmp\x")
  $man = Get-Content "$tmp\x\manifest.json" -Raw -Encoding UTF8 | ConvertFrom-Json
  # Every patch gets its own backup folder: <game>\VOIDMODS-backup\<patch number>.
  $bk = Join-Path $game ('VOIDMODS-backup\' + $man.id); New-Item -ItemType Directory -Force $bk | Out-Null

  # --- deletes (backed up first): old files a patch replaces under a new name ---
  # @( ... ) makes sure we get a list even when the manifest has one value or none.
  foreach ($rel in @($man.delete)) {
    if (-not $rel) { continue }
    $p = Join-Path $game $rel
    if (Test-Path $p) { Copy-Item $p $bk -Force -Recurse; Remove-Item $p -Force -Recurse; Write-Host "Removed  $rel" }
  }

  # --- copy files ---
  # Each file in the zip's files\ folder goes to the same place in the game folder (files\Mods\X.dll -> Mods\X.dll).
  # Substring cuts off the "files\" part to get that relative path. An existing file is backed up first.
  # (Get-Item ...).FullName gives the folder's real, long path: a short form like C:\Users\VOIDMA~1 would have a
  # different length than the paths Get-ChildItem returns, and the cut would land in the wrong place.
  $fdir = "$tmp\x\files"
  if (Test-Path $fdir) {
    $fdir = (Get-Item $fdir).FullName
    $gameFull = (Get-Item $game).FullName
    Get-ChildItem $fdir -Recurse -File | ForEach-Object {
      $rel = $_.FullName.Substring($fdir.Length + 1)
      $dst = Join-Path $gameFull $rel
      # Safety check: never write anywhere outside the game folder.
      if (-not ([IO.Path]::GetFullPath($dst)).StartsWith($gameFull + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "refusing to write outside the game folder: $rel"
      }
      New-Item -ItemType Directory -Force (Split-Path $dst) | Out-Null
      if (Test-Path $dst) { $b = Join-Path $bk $rel; New-Item -ItemType Directory -Force (Split-Path $b) | Out-Null; Copy-Item $dst $b -Force }
      Copy-Item $_.FullName $dst -Force
      Write-Host "Installed $rel"
    }
  }

  # Clean up and report.
  Remove-Item $tmp -Recurse -Force
  Write-Host "`nDone! Start FNAF Online with mods; MOD CONFIGS is in the home screen menu." -ForegroundColor Green
  if ($man.notes) { Write-Host "`n$($man.notes)" }
  Write-Host "(Backups of anything replaced: $bk)"
  Pause-Exit 0
# Any error above ends up here. $_.Exception.Message is the error's short text.
} catch {
  Write-Host "`nSomething went wrong: $($_.Exception.Message)" -ForegroundColor Red
  Write-Host "Send a screenshot of this window to Voidmaster01."
  Pause-Exit 1
}
