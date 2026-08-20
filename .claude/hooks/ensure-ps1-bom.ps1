<#
.SYNOPSIS
    PostToolUse hook: keep every .ps1 saved as UTF-8 with BOM.

.DESCRIPTION
    Windows PowerShell 5.1 decodes BOM-less files with the system ANSI codepage (cp936 here),
    so a Chinese comment can swallow the following newline and silently absorb the next line of
    code. The Write tool emits UTF-8 without BOM, so re-stamp the BOM after every write.
    Comments in this file stay ASCII so it cannot break itself.
#>
$ErrorActionPreference = "Stop"

try {
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }
    $payload = $raw | ConvertFrom-Json

    $path = $payload.tool_response.filePath
    if ([string]::IsNullOrWhiteSpace($path)) { $path = $payload.tool_input.file_path }
    if ([string]::IsNullOrWhiteSpace($path)) { exit 0 }
    if ([System.IO.Path]::GetExtension($path) -ne ".ps1") { exit 0 }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { exit 0 }

    $bytes = [System.IO.File]::ReadAllBytes($path)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { exit 0 }

    $text = [System.Text.Encoding]::UTF8.GetString($bytes)
    [System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding $true))

    # ASCII only: this file cannot repair its own encoding before it runs.
    $name = [System.IO.Path]::GetFileName($path)
    @{ systemMessage = "Re-stamped UTF-8 BOM on $name (PowerShell 5.1 reads BOM-less files as GBK)" } |
        ConvertTo-Json -Compress
}
catch {
    # Never fail the tool call because of the hook.
    exit 0
}
