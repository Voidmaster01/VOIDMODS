<#
  make-patch.ps1 - builds a one-click VOIDMODS patch: ONE .bat file a player double-clicks to install the mods.
  The same idea as mothmaster's MothMaster patches ("Sleepy Patch"), made for FNAF Online + MelonLoader.

  HOW TO RUN (PowerShell 7, from the repo folder):
    pwsh -NoProfile -File tools\make-patch.ps1 -Id 1                       all VOIDMODS (the default)
    pwsh -NoProfile -File tools\make-patch.ps1 -Id 2 -Mods Voidcons       only some mods (ModConfigs is added when needed)
    pwsh -NoProfile -File tools\make-patch.ps1 -Id 3 -Notes "Fixed X"     a note shown to the player at the end
  WHAT IT NEEDS: the same as build.ps1 (game-path.txt, ModConfigs.dll in the game's Mods folder to compile against).
  The game's Mods folder itself is NOT changed by this script.
  WHAT IT MAKES: dist\VOIDMODS-patch-<Id>.bat (dist\ is git-ignored). Send that one file to your friends.

  HOW IT WORKS
    1. Builds each chosen mod into a temporary folder (with build.ps1 -Out).
    2. Packs the DLLs (as files\Mods\<Mod>.dll) and a small manifest.json into a zip, in memory.
    3. Turns the zip into plain text with base64 and glues it into a .bat, together with the installer code
       (tools\patch_core.ps1). Three parts, split by markers made of three colons:
         head  : cmd lines that start PowerShell, which reads this same .bat, takes the base64 text between the
                 DATA and CODE markers, and runs everything after the CODE marker
         DATA  : the base64 zip, in 76-character lines
         CODE  : patch_core.ps1
    One file carries both the mods and the installer, so there is nothing else to download.
#>
param(
    [Parameter(Mandatory)][string]$Id,
    [string[]]$Mods = @(),
    [string]$Notes = '',
    [string]$Title = ''
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

# Every VOIDMOD in the repo: a folder with .cs files, except tools/branding/dist. "for all" = the default.
$allMods = @(Get-ChildItem $repo -Directory | Where-Object { $_.Name -notin 'tools', 'branding', 'dist', '.git' -and (Get-ChildItem "$($_.FullName)\*.cs" -ErrorAction SilentlyContinue) } | ForEach-Object Name)
if ($Mods.Count -eq 0) { $Mods = $allMods }
# "pwsh -File" can hand a comma list over as one text: split it into separate names.
$Mods = @($Mods | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
foreach ($m in $Mods) { if ($m -notin $allMods) { Write-Output "Unknown mod '$m'. Known: $($allMods -join ', ')"; exit 1 } }
# Every mod except ModConfigs needs ModConfigs, so it is added when missing (and installed first, alphabetically fine).
if ($Mods | Where-Object { $_ -ne 'ModConfigs' }) { if ('ModConfigs' -notin $Mods) { $Mods = @('ModConfigs') + $Mods } }
if (-not $Title) { $Title = "VOIDMODS patch $Id" }
$Title = $Title.Replace("'", '')   # the title sits inside '...' in the .bat's PowerShell line; a ' would end it early

# 1. Build the chosen mods into a temporary folder.
$work = Join-Path ([IO.Path]::GetTempPath()) ("voidmods-patch-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $work | Out-Null
try {
    foreach ($m in $Mods) {
        pwsh -NoProfile -File "$repo\build.ps1" -Mod $m -Out "$work\$m.dll"
        if ($LASTEXITCODE -ne 0) { Write-Output "Stopped: $m failed to build."; exit 1 }
    }

    # 2. Zip in memory: a MemoryStream is a "file" in RAM. ZipArchive writes entries into it; the "using"-like
    #    try/finally disposes it so the zip is complete before we read the bytes.
    $manifest = [ordered]@{ id = $Id; mods = $Mods; delete = @(); notes = $Notes } | ConvertTo-Json
    $ms = [IO.MemoryStream]::new()
    $zip = [IO.Compression.ZipArchive]::new($ms, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        $entry = $zip.CreateEntry('manifest.json')
        $w = [IO.StreamWriter]::new($entry.Open(), [Text.UTF8Encoding]::new($false))
        $w.Write($manifest); $w.Dispose()
        foreach ($m in $Mods) {
            $e = $zip.CreateEntry("files/Mods/$m.dll", [IO.Compression.CompressionLevel]::Optimal)
            $s = $e.Open(); $bytes = [IO.File]::ReadAllBytes("$work\$m.dll"); $s.Write($bytes, 0, $bytes.Length); $s.Dispose()
        }
    }
    finally { $zip.Dispose() }

    # 3. base64 + installer code -> one .bat. The installer must never contain the three-colon marker.
    $b64 = [Convert]::ToBase64String($ms.ToArray())
    $core = Get-Content "$PSScriptRoot\patch_core.ps1" -Raw
    if ($core.Contains(':' + '::')) { Write-Output 'patch_core.ps1 contains the three-colon marker; remove it.'; exit 1 }
    # The head: %~f0 is the .bat's own full path, %~dp0 its folder (with a trailing \). PowerShell reads the .bat,
    # finds the markers (written as ':'+'::' so this line itself doesn't contain them), and runs the CODE part with
    # iex (Invoke-Expression). "exit /b %errorlevel%" stops cmd before it reaches the data lines and passes on
    # PowerShell's result (0 = installed, 1 = something stopped it).
    $head = "@echo off`r`ntitle $Title`r`n" +
        "powershell -NoProfile -ExecutionPolicy Bypass -Command `"`$t=[IO.File]::ReadAllText('%~f0');`$m=':'+'::';" +
        "`$a=`$t.IndexOf(`$m+'DATA');`$b=`$t.IndexOf(`$m+'CODE');`$PayloadB64=`$t.Substring(`$a+7,`$b-`$a-7);" +
        "`$PatchTitle='$Title';`$BatDir='%~dp0';iex `$t.Substring(`$b+7)`"`r`nexit /b %errorlevel%`r`n"
    # Cut the base64 into 76-character lines (a regex that matches up to 76 characters at a time).
    $lines = ([regex]::Matches($b64, '.{1,76}') | ForEach-Object Value) -join "`r`n"
    New-Item -ItemType Directory -Force "$repo\dist" | Out-Null
    $out = "$repo\dist\VOIDMODS-patch-$Id.bat"
    $text = $head + ":::DATA`r`n" + $lines + "`r`n:::CODE`r`n" + ($core -replace "`r?`n", "`r`n")
    [IO.File]::WriteAllText($out, $text, [Text.Encoding]::ASCII)   # ASCII, with our own Windows line endings
    "Wrote $out ($([math]::Round((Get-Item $out).Length / 1KB)) KB) with: $($Mods -join ', ')"
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
