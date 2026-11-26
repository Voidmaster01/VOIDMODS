<#
  add-mod.ps1 - approves a mod for the Mod Menu: adds it to (or updates it in) registry\mods.json.

  HOW TO RUN (PowerShell 7, from the repo folder), AFTER you have checked the mod and its source code:
    pwsh -NoProfile -File tools\add-mod.ps1 -Url https://github.com/someone/theirmod/releases/download/v1.0/TheirMod.dll `
         -About "One line about what it does" -Page https://github.com/someone/theirmod
    Then commit registry\mods.json and push it (GitHub Desktop). Players see the change the next time the Mod Menu
    checks the list (GitHub can take up to 5 minutes to show the new file).
  Run it AGAIN every time a listed mod gets a new release (also your own: ModConfigs, Voidcons, ModMenu), because the
  list holds the fingerprint of one exact file: until you do, the Mod Menu refuses the new file as "not approved".

  OPTIONS
    -Url       the direct download link of the .dll (a GitHub release asset link). Required.
    -About     one line shown under the mod's name.
    -Page      a web page about the mod (its repo or release page).
    -Name -Version -Author -Id -Requires   normally read from the DLL itself; give them only to override.

  WHAT IT NEEDS
    PowerShell 7 and an internet connection. It never RUNS the downloaded mod: it only reads it as data.

  HOW IT WORKS
    1. Downloads the DLL into a new, empty temporary folder.
    2. Computes its SHA-256 fingerprint (Get-FileHash).
    3. Reads the mod's [MelonInfo] (id, version, author) and [MelonAdditionalDependencies] (what it needs) from the
       DLL's metadata with System.Reflection.Metadata, the .NET library for reading compiled code without loading it.
    4. Writes the entry into registry\mods.json (replacing an older entry with the same id) and shows it.
#>
param(
    [Parameter(Mandatory)][string]$Url,
    [string]$About = '',
    [string]$Page = '',
    [string]$Name = '',
    [string]$Version = '',
    [string]$Author = '',
    [string]$Id = '',
    [string[]]$Requires = $null
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot          # the repo folder is one level above tools\
$listPath = Join-Path $repo 'registry\mods.json'

if (-not $Url.StartsWith('https://')) { Write-Output 'The -Url must start with https://'; exit 1 }
# The file name is the last part of the link ("TheirMod.dll"). Uri.Segments splits the link at each "/".
$fileName = [Uri]::UnescapeDataString(([Uri]$Url).Segments[-1])
if ($fileName -notmatch '^[A-Za-z0-9._-]+\.dll$') { Write-Output "The link must end in a plain .dll file name (got '$fileName')."; exit 1 }

# ---- 1. Download into a new empty folder (downloaded files are untrusted, so they get their own folder) ----
$tmp = Join-Path ([IO.Path]::GetTempPath()) ('add-mod-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $tmp | Out-Null
$dll = Join-Path $tmp $fileName
Write-Output "Downloading $Url"
Invoke-WebRequest -Uri $Url -OutFile $dll -UseBasicParsing

# ---- 2. Fingerprint ----
$sha = (Get-FileHash -Algorithm SHA256 $dll).Hash.ToLowerInvariant()

# ---- 3. Read [MelonInfo] and [MelonAdditionalDependencies] from the metadata ----
# An attribute's arguments are stored as a "blob" of bytes: 01 00 (a marker), then each argument. A text argument is
# its length followed by its UTF-8 bytes (length FF = no text / null). This function reads one such text at $pos
# ([ref] lets the function move the caller's position forward).
function Read-SerString([byte[]]$b, [ref]$pos) {
    $first = $b[$pos.Value]
    if ($first -eq 0xFF) { $pos.Value++; return $null }
    # The length uses 1, 2 or 4 bytes; the top bits of the first byte say which (-band = bitwise AND, -shl = shift left).
    if (($first -band 0x80) -eq 0) { $len = $first; $pos.Value += 1 }
    elseif (($first -band 0xC0) -eq 0x80) { $len = (($first -band 0x3F) -shl 8) -bor $b[$pos.Value + 1]; $pos.Value += 2 }
    else { $len = (($first -band 0x1F) -shl 24) -bor ($b[$pos.Value + 1] -shl 16) -bor ($b[$pos.Value + 2] -shl 8) -bor $b[$pos.Value + 3]; $pos.Value += 4 }
    $s = [Text.Encoding]::UTF8.GetString($b, $pos.Value, $len)
    $pos.Value += $len
    return $s
}

$info = $null; $deps = @(); $game = $null
$stream = [IO.File]::OpenRead($dll)
try {
    $pe = [Reflection.PortableExecutable.PEReader]::new($stream)
    $md = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
    foreach ($h in $md.GetAssemblyDefinition().GetCustomAttributes()) {
        $ca = $md.GetCustomAttribute($h)
        # The attribute's constructor points (through a "member reference") to its type; we only need the type name.
        if ($ca.Constructor.Kind -ne 'MemberReference') { continue }
        $parent = $md.GetMemberReference([Reflection.Metadata.MemberReferenceHandle]$ca.Constructor).Parent
        if ($parent.Kind -ne 'TypeReference') { continue }
        $type = $md.GetString($md.GetTypeReference([Reflection.Metadata.TypeReferenceHandle]$parent).Name)
        $blob = $md.GetBlobBytes($ca.Value)
        $pos = 2   # skip the 01 00 marker
        switch ($type) {
            'MelonInfoAttribute' {
                # MelonInfo(Type, name, version, author, downloadLink): the Type is stored as its name (text too).
                $null = Read-SerString $blob ([ref]$pos)
                $info = [pscustomobject]@{ Name = (Read-SerString $blob ([ref]$pos)); Version = (Read-SerString $blob ([ref]$pos)); Author = (Read-SerString $blob ([ref]$pos)) }
            }
            'MelonAdditionalDependenciesAttribute' {
                # params string[]: a 4-byte count, then that many texts.
                $count = [BitConverter]::ToInt32($blob, $pos); $pos += 4
                $deps = @(for ($i = 0; $i -lt $count; $i++) { Read-SerString $blob ([ref]$pos) })
            }
            'MelonGameAttribute' { $null = Read-SerString $blob ([ref]$pos); $game = Read-SerString $blob ([ref]$pos) }
        }
    }
    $pe.Dispose()
}
catch { Write-Output "Couldn't read the mod's details from the DLL ($($_.Exception.Message)); give -Id -Version -Author yourself." }
finally { $stream.Dispose() }

if (-not $info -and -not ($Id -and $Version)) { Write-Output 'This DLL has no [MelonInfo], so it is probably not a MelonLoader mod. Stopped.'; exit 1 }
if ($game -and $game -ne 'FNAF Online Multiplayer') { Write-Output "WARNING: this mod says it is for the game '$game', not FNAF Online Multiplayer." }

# The -options win over what the DLL says ("if ($Id) { $Id } else { ... }" picks the first one that is set).
$entry = [ordered]@{
    id       = $(if ($Id) { $Id } else { $info.Name })
    name     = $(if ($Name) { $Name } elseif ($Id) { $Id } else { $info.Name })
    author   = $(if ($Author) { $Author } else { "$($info.Author)" })
    version  = $(if ($Version) { $Version } else { $info.Version })
    about    = $About
    file     = $fileName
    url      = $Url
    sha256   = $sha
    requires = @($(if ($null -ne $Requires) { $Requires } else { $deps }) | Where-Object { $_ })
    page     = $Page
}

# ---- 4. Write the list ----
if (Test-Path $listPath) { $list = Get-Content $listPath -Raw | ConvertFrom-Json }
else { New-Item -ItemType Directory -Force (Split-Path $listPath) | Out-Null; $list = [pscustomobject]@{ format = 1; mods = @() } }
# Keep every other mod; an entry with the same id is replaced. The new entry keeps the old one's -About/-Page when
# they weren't given this time, so updating a mod only needs -Url.
$old = @($list.mods | Where-Object { $_.id -eq $entry.id })
if ($old.Count) {
    if (-not $About) { $entry.about = $old[0].about }
    if (-not $Page) { $entry.page = $old[0].page }
}
$mods = @($list.mods | Where-Object { $_.id -ne $entry.id }) + [pscustomobject]$entry
$list.mods = @($mods | Sort-Object id)
# ConvertTo-Json: -Depth 5 so nested lists are written out in full; the file is saved as UTF-8 without a BOM.
[IO.File]::WriteAllText($listPath, ($list | ConvertTo-Json -Depth 5) + "`n", [Text.UTF8Encoding]::new($false))

Remove-Item -Recurse -Force $tmp   # our own temporary folder from step 1
Write-Output ''
Write-Output $(if ($old.Count) { "UPDATED $($entry.id): $($old[0].version) -> $($entry.version)" } else { "ADDED $($entry.id) $($entry.version)" })
[pscustomobject]$entry | Format-List | Out-String | Write-Output
Write-Output "Now commit registry\mods.json and push it."
