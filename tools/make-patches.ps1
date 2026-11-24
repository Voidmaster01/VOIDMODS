<#
  make-patches.ps1 - builds the standard set of one-click VOIDMODS installers into dist\:
    VOIDMODS-FULL.bat        every VOIDMOD
    VOIDMODS-Voidcons.bat    Voidcons (+ ModConfigs, which it needs)
    VOIDMODS-ModConfigs.bat  only the MOD CONFIGS hub

  HOW TO RUN (PowerShell 7, from the repo folder):   pwsh -NoProfile -File tools\make-patches.ps1 -Id 2
  -Id is the patch number: each install keeps its backups in <game>\VOIDMODS-backup\<Id>\, so use a new number for
  every release (the same number as the GitHub release is easiest). WHAT IT NEEDS: the same as make-patch.ps1.
#>
param([Parameter(Mandatory)][string]$Id, [string]$Notes = 'Open MOD CONFIGS on the home screen to set up your mods.')
$ErrorActionPreference = 'Stop'

# Each line: the file name, and which mods go in (an empty list = all of them).
$set = @(
    @{ Name = 'VOIDMODS-FULL';       Mods = @() },
    @{ Name = 'VOIDMODS-Voidcons';   Mods = @('Voidcons') },
    @{ Name = 'VOIDMODS-ModConfigs'; Mods = @('ModConfigs') }
)
foreach ($p in $set) {
    $modArgs = if ($p.Mods.Count) { @('-Mods', ($p.Mods -join ',')) } else { @() }
    # @modArgs "splats" the list: each item becomes its own argument (or nothing, when the list is empty).
    pwsh -NoProfile -File "$PSScriptRoot\make-patch.ps1" -Id $Id -Name $p.Name -Notes $Notes @modArgs | Select-Object -Last 1
    if ($LASTEXITCODE -ne 0) { Write-Output "Stopped: $($p.Name) failed."; exit 1 }
}
