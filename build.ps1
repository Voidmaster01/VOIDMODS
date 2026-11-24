<#
  build.ps1 - builds one VOIDMOD from this repo into the game's Mods folder.

  HOW TO RUN (PowerShell 7, game closed):
    pwsh -NoProfile -File build.ps1 -Mod Voidcons              builds Voidcons\*.cs into <game>\Mods\Voidcons.dll
    pwsh -NoProfile -File build.ps1 -Mod ModConfigs -Out x.dll  builds somewhere else (a test build)
    pwsh -NoProfile -File build-all.ps1                         builds every mod, in the right order

  WHAT IT NEEDS
    - PowerShell 7 (it ships Roslyn, Microsoft's C# compiler, as a library this script calls).
    - The .NET 6 runtime (C:\Program Files\dotnet\shared\Microsoft.NETCore.App\6.0.x).
    - The game with MelonLoader installed and started once (that generates MelonLoader\Il2CppAssemblies).
    - Where the game is: the -Game option, or the FOMP_GAME environment variable, or a file game-path.txt next to
      this script holding the folder. game-path.txt is in .gitignore, so your own folder names never reach GitHub.

  WHAT IT DOES
    1. Collects the libraries a mod can use: .NET, MelonLoader, the game's generated code, and (for every mod
       except ModConfigs itself) Mods\ModConfigs.dll, the shared config hub that VOIDMODS build on.
    2. Parses every .cs file in the mod's folder.
    3. Embeds the files in <Mod>\Resources\ and branding\voidmod.png into the DLL, so the mod needs no extra files.
    4. Compiles in memory and only writes the DLL if the build succeeded (a failed build never breaks a working mod).
#>
param(
    [Parameter(Mandatory)][string]$Mod,
    [string]$Game = '',
    [string]$Out = ''
)
$ErrorActionPreference = 'Stop'

# ---- Find the game folder: -Game, then FOMP_GAME, then game-path.txt ----
if (-not $Game) { $Game = $env:FOMP_GAME }
if (-not $Game -and (Test-Path "$PSScriptRoot\game-path.txt")) { $Game = (Get-Content "$PSScriptRoot\game-path.txt" -Raw).Trim() }
if (-not $Game -or -not (Test-Path "$Game\MelonLoader\Il2CppAssemblies")) {
    Write-Output "Can't find the game. Put the game folder in game-path.txt next to build.ps1 (MelonLoader must have run once)."
    exit 1
}
$modDir = Join-Path $PSScriptRoot $Mod
if (-not (Test-Path $modDir)) { Write-Output "No folder '$Mod' in the repo."; exit 1 }
if (-not $Out) { $Out = "$Game\Mods\$Mod.dll" }

# ---- 1. Libraries ----
# The newest installed .NET 6 runtime folder (Sort-Object on the version number, then take the last one).
$runtime = Get-ChildItem 'C:\Program Files\dotnet\shared\Microsoft.NETCore.App' -Directory |
    Where-Object { $_.Name -like '6.*' } | Sort-Object { [version]$_.Name } | Select-Object -Last 1
if (-not $runtime) { Write-Output 'The .NET 6 runtime is not installed.'; exit 1 }
$dlls = @(Get-ChildItem "$($runtime.FullName)\*.dll") + @(Get-ChildItem "$Game\MelonLoader\net6\*.dll") + @(Get-ChildItem "$Game\MelonLoader\Il2CppAssemblies\*.dll")
if ($Mod -ne 'ModConfigs') {
    if (-not (Test-Path "$Game\Mods\ModConfigs.dll")) { Write-Output 'Build ModConfigs first (build-all.ps1 does that).'; exit 1 }
    $dlls += Get-Item "$Game\Mods\ModConfigs.dll"
}
# Keep only real .NET libraries; GetAssemblyName fails on plain Windows DLLs and try/catch skips those.
$managed = foreach ($dll in $dlls) { try { [void][Reflection.AssemblyName]::GetAssemblyName($dll.FullName); $dll.FullName } catch { } }
$references = [Microsoft.CodeAnalysis.MetadataReference[]]@($managed | ForEach-Object { [Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($_) })

# ---- 2. Source files (the file path is passed along so errors name the file) ----
$trees = foreach ($cs in Get-ChildItem "$modDir\*.cs") {
    [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText((Get-Content $cs.FullName -Raw), $null, $cs.FullName)
}

# ---- 3. Embedded files ("manifest resources"), read at runtime with Assembly.GetManifestResourceStream(name) ----
# A ResourceDescription needs a function that opens the file; [Func[IO.Stream]] turns a script block into one.
$files = @(Get-ChildItem "$modDir\Resources\*" -File -ErrorAction SilentlyContinue)
if (Test-Path "$PSScriptRoot\branding\voidmod.png") { $files += Get-Item "$PSScriptRoot\branding\voidmod.png" }
$resources = [System.Collections.Generic.List[Microsoft.CodeAnalysis.ResourceDescription]]::new()
foreach ($f in $files) {
    $path = $f.FullName
    $open = [Func[IO.Stream]]{ [IO.File]::OpenRead($path) }.GetNewClosure()   # GetNewClosure keeps this loop's $path
    $resources.Add([Microsoft.CodeAnalysis.ResourceDescription]::new($f.Name, $open, $true))
}

# ---- 4. Compile in memory, then write ----
# Settings: a library (DLL), release optimization, and "unsafe" allowed because some game functions in the
# references use raw pointers (byte*).
$options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary)
$options = $options.WithOptimizationLevel([Microsoft.CodeAnalysis.OptimizationLevel]::Release).WithAllowUnsafe($true)
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create($Mod, [Microsoft.CodeAnalysis.SyntaxTree[]]@($trees), $references, $options)
$pe = [IO.MemoryStream]::new()
# Emit's 5th setting is the list of embedded files; $null means "default" for the others.
$result = $compilation.Emit($pe, $null, $null, $null, $resources, $null, $null, $null, $null, $null, [Threading.CancellationToken]::None)
$result.Diagnostics | Where-Object { $_.Severity -ge 'Warning' } | ForEach-Object { $_.ToString() }
if (-not $result.Success) { exit 1 }
[IO.File]::WriteAllBytes($Out, $pe.ToArray())
"Built $Out" + $(if ($files.Count) { " (embedded: $(($files | ForEach-Object Name) -join ', '))" } else { '' })
