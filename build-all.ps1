<#
  build-all.ps1 - builds every VOIDMOD in this repo into the game's Mods folder, in the right order.

  HOW TO RUN (PowerShell 7, game closed):   pwsh -NoProfile -File build-all.ps1
  WHAT IT NEEDS: the same as build.ps1 (game-path.txt pointing at the game folder).

  WHY THE ORDER MATTERS: Voidcons (and any future mod that uses the hub) is compiled against ModConfigs.dll,
  so ModConfigs has to be built first. New VOIDMODS go after ModConfigs in the list below.
#>
$ErrorActionPreference = 'Stop'
foreach ($mod in 'ModConfigs', 'Voidcons', 'ModMenu') {
    pwsh -NoProfile -File "$PSScriptRoot\build.ps1" -Mod $mod
    # $LASTEXITCODE is the exit code of the last program that ran; anything but 0 means the build failed.
    if ($LASTEXITCODE -ne 0) { Write-Output "Stopped: $mod failed to build."; exit 1 }
}
