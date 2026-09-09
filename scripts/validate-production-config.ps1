<#
.SYNOPSIS
  Pre-deployment gate for the Gradify (Motiva) IIS release.

.DESCRIPTION
  The published package deliberately ships NO configuration of its own -
  Server/appsettings.json and appsettings.Development.json are excluded from
  publish (CopyToPublishDirectory="Never") because they hold real development
  secrets. Every production value must therefore be supplied on the server, in
  appsettings.Production.json or as environment variables, BEFORE IIS starts.

  When that step is skipped the app starts and then dies with an opaque 500:

      System.ArgumentNullException: Value cannot be null. (Parameter 'ClientId')
      at Microsoft.AspNetCore.Authentication.OAuth.OAuthOptions.Validate()

  This script makes that step impossible to overlook. Run it on the server after
  publish and before the first IIS start. It reports every required production
  key, tells you which are genuinely required vs optional, and (given the
  published folder) verifies the package is safe to upload - no secrets, no
  base-config file.

  It reads configuration only. It NEVER prints a secret value, only whether a
  key is set, and it changes nothing.

.PARAMETER PublishDir
  The published output folder (e.g. bin\Release\net10.0\publish). When given,
  the script also runs the publish-output safety checks and defaults
  -ConfigFile to <PublishDir>\appsettings.Production.json.

.PARAMETER ConfigFile
  Path to the server's appsettings.Production.json. Optional - keys already
  supplied as environment variables are counted as satisfied.

.PARAMETER SkipEnv
  Do not consider environment variables; validate the config file only.

.EXAMPLE
  # On the server, before starting IIS:
  powershell -ExecutionPolicy Bypass -File scripts\validate-production-config.ps1 -PublishDir C:\inetpub\wwwroot\FinalProject_NoaOfir

.EXAMPLE
  # Validate just a config file you are preparing:
  powershell -File scripts\validate-production-config.ps1 -ConfigFile .\appsettings.Production.json

.NOTES
  Exit code 0 = safe to start / safe to upload. Non-zero = a required key is
  missing or the publish output is unsafe. Suitable for a CI / release gate.

  ASCII-only on purpose: operators launch it with `powershell -File`, and a .ps1
  saved as UTF-8 without a BOM would have any non-ASCII byte misread under the
  console's ANSI code page. Keep it ASCII.
#>

[CmdletBinding()]
param(
    [string] $PublishDir,
    [string] $ConfigFile,
    [switch] $SkipEnv
)

$ErrorActionPreference = 'Stop'

# --- Key catalogue ----------------------------------------------------------
# Sensitivity is documented in docs/DEPLOYMENT.md section 3. "Required" here
# means the app will not start, or authentication is broken, without it -
# verified against Server/Program.cs, not guessed:
#
#   Authentication:Google:ClientId / ClientSecret
#       -> OAuthOptions.Validate() throws ArgumentNullException at startup.
#          This is the exact failure this script exists to prevent.
#   JWTSettings:securityKey
#       -> new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)) throws on a
#          null/empty key when the JWT bearer options are built.
#   JWTSettings:validIssuer
#       -> no crash, but ValidateIssuer/ValidateAudience are on, so every token
#          fails validation and nobody can stay signed in. Ships with a default.
#
# Everything else is a feature that degrades safely when left blank, or has a
# working default. None of them stop the app from starting.

$Required = @(
    @{ Key = 'Authentication:Google:ClientId';     Note = 'Google login + Calendar. Startup fails (OAuthOptions.Validate) if unset.' }
    @{ Key = 'Authentication:Google:ClientSecret'; Note = 'Google login + Calendar. Startup fails (OAuthOptions.Validate) if unset.' }
    @{ Key = 'JWTSettings:securityKey';            Note = 'Token signing key. Startup fails when JWT options are built if unset. Use a NEW random value.' }
    @{ Key = 'JWTSettings:validIssuer';            Note = 'Issuer AND audience. Tokens never validate if blank. Template default: ./issuer' }
)

$Recommended = @(
    @{ Key = 'ConnectionStrings:DefaultConnection'; Note = 'Path only. Blank => <contentRoot>\FinalProjectDB.db. Absolute path outside the deploy dir is preferred. The DB FILE must exist - the app fails fast in Production if it is missing.' }
    @{ Key = 'DataProtection:KeysDirectory';        Note = 'Path only. Blank => <contentRoot>\App_Data\DataProtection-Keys. Must be writable; point it outside the deploy dir so a redeploy does not lose the key ring.' }
    @{ Key = 'App:BaseUrl';                         Note = 'Absolute origin incl. the sub-path, no trailing slash. Only used for links in outgoing email. Blank => email sends without clickable links.' }
)

$Optional = @(
    @{ Key = 'Email:UserName';     Note = 'SMTP sending address. Blank => outgoing email disabled.' }
    @{ Key = 'Email:Password';     Note = 'SMTP / Gmail app password. Blank => outgoing email disabled.' }
    @{ Key = 'Airtable:Token';     Note = 'Airtable personal access token. Blank => Airtable sync disabled.' }
    @{ Key = 'Airtable:BaseId';    Note = 'Airtable base id. Blank => Airtable sync disabled.' }
    @{ Key = 'Slack:ClientId';     Note = 'Slack integration (off by default).' }
    @{ Key = 'Slack:ClientSecret'; Note = 'Slack integration (off by default).' }
    @{ Key = 'OpenAI:Key';         Note = 'Unused by shipped code.' }
    @{ Key = 'ExternalApi:ApiKey'; Note = 'Innovation-team webhook, if used.' }
)

# Keys that MUST be empty in the committed template. A non-empty value here means
# a real secret was placed in a tracked file - sanitize it and rotate the value.
$MustBeBlankInTemplate = @(
    'JWTSettings:securityKey'
    'Email:Password'
    'Authentication:Google:ClientId'
    'Authentication:Google:ClientSecret'
    'Airtable:Token'
    'Slack:ClientId'
    'Slack:ClientSecret'
    'OpenAI:Key'
    'ExternalApi:ApiKey'
)

# --- Helpers ----------------------------------------------------------------

function Read-JsonFile {
    param([string] $Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    $raw = Get-Content -LiteralPath $Path -Raw
    # ASP.NET's JSON config provider tolerates // line comments; strip whole
    # comment lines so a hand-edited config file still parses. Object keys like
    # "//howToUse" are ordinary JSON strings and are left untouched (a // that is
    # not at the start of a trimmed line is not matched).
    $stripped = ($raw -split "`n") | ForEach-Object {
        if ($_ -match '^\s*//') { '' } else { $_ }
    }
    return ($stripped -join "`n" | ConvertFrom-Json)
}

function Get-ConfigValue {
    param($Root, [string] $KeyPath)
    if ($null -eq $Root) { return $null }
    $cur = $Root
    foreach ($part in ($KeyPath -split ':')) {
        if ($null -eq $cur) { return $null }
        $prop = $cur.PSObject.Properties[$part]
        if ($null -eq $prop) { return $null }
        $cur = $prop.Value
    }
    return $cur
}

function Get-EnvScope {
    param([string] $KeyPath)
    if ($SkipEnv) { return $null }
    $name = $KeyPath -replace ':', '__'
    foreach ($scope in 'Process', 'User', 'Machine') {
        $v = [Environment]::GetEnvironmentVariable($name, $scope)
        if (-not [string]::IsNullOrWhiteSpace($v)) { return $scope }
    }
    return $null
}

function Test-Provided {
    # Returns a source string ('file' / 'env:<scope>') or $null if not provided.
    param($Root, [string] $KeyPath)
    $fromFile = Get-ConfigValue -Root $Root -KeyPath $KeyPath
    if ($fromFile -is [string]) {
        if (-not [string]::IsNullOrWhiteSpace($fromFile)) { return 'file' }
    } elseif ($null -ne $fromFile) {
        return 'file'   # non-empty non-string (unlikely for these keys)
    }
    $envScope = Get-EnvScope -KeyPath $KeyPath
    if ($envScope) { return "env:$envScope" }
    return $null
}

$script:Fail = $false
function Write-Status {
    param([string] $Tag, [ConsoleColor] $Color, [string] $Line)
    Write-Host ('  [{0}] ' -f $Tag) -ForegroundColor $Color -NoNewline
    Write-Host $Line
}

# --- Resolve inputs ---------------------------------------------------------

if ($PublishDir -and -not $ConfigFile) {
    $ConfigFile = Join-Path $PublishDir 'appsettings.Production.json'
}

Write-Host ''
Write-Host '=== Gradify - production configuration validation ===' -ForegroundColor Cyan
Write-Host ''

$config = $null
if ($ConfigFile) {
    if (Test-Path -LiteralPath $ConfigFile) {
        Write-Host "Config file : $ConfigFile  [FOUND]"
        try {
            $config = Read-JsonFile -Path $ConfigFile
        } catch {
            Write-Host "  Could not parse the config file as JSON: $($_.Exception.Message)" -ForegroundColor Red
            $script:Fail = $true
        }
    } else {
        Write-Host "Config file : $ConfigFile  [NOT FOUND]" -ForegroundColor Yellow
        Write-Host '  Create it from appsettings.Production.template.json, or supply values as environment variables.'
    }
} else {
    Write-Host 'Config file : (none supplied - checking environment variables only)'
}
if ($SkipEnv) { Write-Host 'Env vars    : skipped (-SkipEnv)' } else { Write-Host 'Env vars    : checked (Process, User, Machine scopes)' }
Write-Host ''

# --- Required ---------------------------------------------------------------

Write-Host 'REQUIRED for startup (app will not start / auth is broken without these):' -ForegroundColor White
foreach ($k in $Required) {
    $src = Test-Provided -Root $config -KeyPath $k.Key
    if ($src) {
        Write-Status 'OK  ' Green ('{0,-42} (set via {1})' -f $k.Key, $src)
    } else {
        $script:Fail = $true
        $envName = $k.Key -replace ':', '__'
        Write-Status 'MISS' Red  ('{0,-42} -> set in appsettings.Production.json or env {1}' -f $k.Key, $envName)
        Write-Host   ('         {0}' -f $k.Note) -ForegroundColor DarkGray
    }
}
Write-Host ''

# --- Recommended ------------------------------------------------------------

Write-Host 'RECOMMENDED (safe defaults exist, but review for production):' -ForegroundColor White
foreach ($k in $Recommended) {
    $src = Test-Provided -Root $config -KeyPath $k.Key
    if ($src) {
        Write-Status 'OK  ' Green ('{0,-42} (set via {1})' -f $k.Key, $src)
    } else {
        Write-Status 'DFLT' Yellow ('{0,-42} using default' -f $k.Key)
        Write-Host   ('         {0}' -f $k.Note) -ForegroundColor DarkGray
    }
}
Write-Host ''

# --- Optional ---------------------------------------------------------------

Write-Host 'OPTIONAL (feature is simply disabled when left blank - not an error):' -ForegroundColor White
foreach ($k in $Optional) {
    $src = Test-Provided -Root $config -KeyPath $k.Key
    if ($src) {
        Write-Status 'ON  ' Green ('{0,-42} (set via {1})' -f $k.Key, $src)
    } else {
        Write-Status ' -- ' DarkGray ('{0,-42} {1}' -f $k.Key, $k.Note)
    }
}
Write-Host ''

# --- Publish-output safety (only when a publish folder is supplied) ----------

if ($PublishDir) {
    Write-Host '=== Publish output safety ===' -ForegroundColor Cyan
    if (-not (Test-Path -LiteralPath $PublishDir)) {
        Write-Status 'MISS' Red "Publish directory not found: $PublishDir"
        $script:Fail = $true
    } else {
        # 1. base/dev config must NOT be in the package (they hold real secrets).
        foreach ($f in 'appsettings.json', 'appsettings.Development.json') {
            $p = Join-Path $PublishDir $f
            if (Test-Path -LiteralPath $p) {
                Write-Status 'FAIL' Red "$f IS present - it must be excluded from publish. DO NOT upload this package."
                $script:Fail = $true
            } else {
                Write-Status 'OK  ' Green "$f is NOT present"
            }
        }

        # 2. the placeholder template SHOULD travel with the package as the
        #    operator checklist.
        $tpl = Join-Path $PublishDir 'appsettings.Production.template.json'
        if (Test-Path -LiteralPath $tpl) {
            Write-Status 'OK  ' Green 'appsettings.Production.template.json IS present'

            # 3. and it must contain placeholders only.
            try {
                $tplJson = Read-JsonFile -Path $tpl
                $leaked = @()
                foreach ($key in $MustBeBlankInTemplate) {
                    $v = Get-ConfigValue -Root $tplJson -KeyPath $key
                    if ($v -is [string] -and -not [string]::IsNullOrWhiteSpace($v)) { $leaked += $key }
                }
                if ($leaked.Count -gt 0) {
                    Write-Status 'FAIL' Red 'template contains non-placeholder values - a real secret may have been committed:'
                    foreach ($key in $leaked) { Write-Host "         - $key  (sanitize the template AND rotate this credential)" -ForegroundColor Red }
                    $script:Fail = $true
                } else {
                    Write-Status 'OK  ' Green 'template contains placeholders only (no filled secrets)'
                }
            } catch {
                Write-Status 'WARN' Yellow "could not parse the template to scan it: $($_.Exception.Message)"
            }
        } else {
            Write-Status 'WARN' Yellow 'appsettings.Production.template.json is NOT present (expected in the package as the operator checklist)'
        }
    }
    Write-Host ''
}

# --- Verdict ----------------------------------------------------------------

if ($script:Fail) {
    Write-Host 'RESULT: FAIL - resolve the items above BEFORE starting IIS / uploading.' -ForegroundColor Red
    Write-Host ''
    exit 1
} else {
    Write-Host 'RESULT: PASS - required configuration present; publish output safe to upload.' -ForegroundColor Green
    Write-Host ''
    exit 0
}
