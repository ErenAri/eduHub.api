$ErrorActionPreference = 'Stop'

$baseUrl = $env:SMOKE_BASE_URL
$orgSlug = $env:SMOKE_ORG_SLUG
if ([string]::IsNullOrWhiteSpace($orgSlug)) {
    $orgSlug = $env:SMOKE_ORG_ID
}
$adminUser = $env:SMOKE_ADMIN_USERNAME
$adminPass = $env:SMOKE_ADMIN_PASSWORD

if ([string]::IsNullOrWhiteSpace($baseUrl)) {
    Write-Error 'SMOKE_BASE_URL is required.'
    exit 1
}
if ([string]::IsNullOrWhiteSpace($orgSlug)) {
    Write-Error 'SMOKE_ORG_SLUG (or SMOKE_ORG_ID) is required.'
    exit 1
}
if ([string]::IsNullOrWhiteSpace($adminUser) -or [string]::IsNullOrWhiteSpace($adminPass)) {
    Write-Error 'SMOKE_ADMIN_USERNAME and SMOKE_ADMIN_PASSWORD are required.'
    exit 1
}

$baseUrl = $baseUrl.TrimEnd('/')
$failed = $false
$cleanup = New-Object System.Collections.Generic.List[scriptblock]

function Get-ErrorDetail {
    param($err)
    $response = $err.Exception.Response
    if (-not $response) {
        return $err.Exception.Message
    }

    $status = $response.StatusCode.value__
    try {
        $stream = $response.GetResponseStream()
        if ($stream) {
            $reader = New-Object System.IO.StreamReader($stream)
            $body = $reader.ReadToEnd()
            if (-not [string]::IsNullOrWhiteSpace($body)) {
                return "HTTP $status $body"
            }
        }
    } catch {
        return "HTTP $status"
    }

    return "HTTP $status"
}

function Invoke-Json {
    param(
        [string]$Method,
        [string]$Path,
        [string]$Token,
        [hashtable]$Headers,
        $Body,
        [switch]$SkipTenant
    )
    $uri = $baseUrl + $Path
    $finalHeaders = @{}
    if (-not $SkipTenant) {
        $finalHeaders['x-tenant-slug'] = $orgSlug
    }
    if ($Token) {
        $finalHeaders['Authorization'] = "Bearer $Token"
    }
    if ($Headers) {
        foreach ($key in $Headers.Keys) {
            $finalHeaders[$key] = $Headers[$key]
        }
    }

    if ($null -ne $Body) {
        $json = $Body | ConvertTo-Json -Depth 6
        return Invoke-RestMethod -Method $Method -Uri $uri -Headers $finalHeaders -ContentType 'application/json' -Body $json
    }

    return Invoke-RestMethod -Method $Method -Uri $uri -Headers $finalHeaders
}

function Invoke-Step {
    param(
        [string]$Name,
        [scriptblock]$Action,
        [switch]$Optional
    )
    Write-Host ">> $Name"
    try {
        return & $Action
    } catch {
        $detail = Get-ErrorDetail $_
        if ($Optional) {
            Write-Warning "$Name failed: $detail"
            $script:failed = $true
            return $null
        }
        throw "$Name failed: $detail"
    }
}

try {
    $publicOrgs = Invoke-Step 'Public org list' {
        Invoke-Json -Method Get -Path '/api/public/organizations' -SkipTenant
    } -Optional

    if ($publicOrgs -and $publicOrgs.Count -gt 0) {
        $slug = $publicOrgs[0].slug
        Invoke-Step 'Public org detail' {
            Invoke-Json -Method Get -Path ("/api/public/organizations/$slug") -SkipTenant
        } -Optional | Out-Null
    } else {
        Write-Warning 'Public org list returned no entries.'
        $failed = $true
    }

    $adminLogin = Invoke-Step 'Admin login' {
        Invoke-Json -Method Post -Path '/api/org/auth/login' -Body @{
            userNameOrEmail = $adminUser
            password = $adminPass
        }
    }

    $token = $adminLogin.accessToken
    if ([string]::IsNullOrWhiteSpace($token)) {
        throw 'Admin login returned no access token.'
    }

    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $building = Invoke-Step 'Create building' {
        Invoke-Json -Method Post -Path '/api/org/buildings' -Token $token -Body @{
            name = "Smoke Building $stamp"
        }
    }
    $cleanup.Add({ Invoke-Json -Method Delete -Path ("/api/org/buildings/$($building.id)") -Token $token | Out-Null }) | Out-Null

    $room = Invoke-Step 'Create room' {
        Invoke-Json -Method Post -Path '/api/org/rooms' -Token $token -Body @{
            code = "SM-$($building.id)"
            name = 'Smoke Room'
            capacity = 6
            buildingId = $building.id
        }
    }
    $cleanup.Add({ Invoke-Json -Method Delete -Path ("/api/org/rooms/$($room.id)") -Token $token | Out-Null }) | Out-Null

    $windows = @()
    0..6 | ForEach-Object { $windows += @{ dayOfWeek = $_; startMinute = 0; endMinute = 1439 } }

    Invoke-Step 'Update building hours' {
        Invoke-Json -Method Put -Path "/api/org/availability/buildings/$($building.id)/hours" -Token $token -Body $windows
    } | Out-Null

    Invoke-Step 'Get building hours' {
        Invoke-Json -Method Get -Path "/api/org/availability/buildings/$($building.id)/hours" -Token $token
    } | Out-Null

    Invoke-Step 'Update room hours' {
        Invoke-Json -Method Put -Path "/api/org/availability/rooms/$($room.id)/hours" -Token $token -Body $windows
    } | Out-Null

    $slotStart = (Get-Date).ToUniversalTime().AddHours(2)
    $slotEnd = $slotStart.AddHours(1)
    $slotStartIso = $slotStart.ToString('o')
    $slotEndIso = $slotEnd.ToString('o')
    $slotQuery = "/api/org/availability/rooms/$($room.id)/slots?startTimeUtc=$([uri]::EscapeDataString($slotStartIso))&endTimeUtc=$([uri]::EscapeDataString($slotEndIso))"

    Invoke-Step 'Room availability slots' {
        Invoke-Json -Method Get -Path $slotQuery -Token $token
    } | Out-Null

    $blackout = Invoke-Step 'Create blackout' {
        Invoke-Json -Method Post -Path '/api/org/availability/blackouts' -Token $token -Body @{
            roomId = $room.id
            startTimeUtc = $slotStartIso
            endTimeUtc = $slotEndIso
            reason = 'Smoke blackout'
        }
    }
    $cleanup.Add({ Invoke-Json -Method Delete -Path ("/api/org/availability/blackouts/$($blackout.id)") -Token $token | Out-Null }) | Out-Null

    Invoke-Step 'Get blackouts' {
        Invoke-Json -Method Get -Path "/api/org/availability/blackouts?roomId=$($room.id)" -Token $token
    } | Out-Null
} catch {
    Write-Error $_
    $failed = $true
} finally {
    $cleanupFailed = $false
    for ($i = $cleanup.Count - 1; $i -ge 0; $i--) {
        try {
            & $cleanup[$i]
        } catch {
            Write-Warning "Cleanup failed: $(Get-ErrorDetail $_)"
            $cleanupFailed = $true
        }
    }

    if ($cleanupFailed) {
        $failed = $true
    }
}

if ($failed) {
    Write-Error 'Smoke check failed.'
    exit 1
}

Write-Host 'Smoke check passed.'
