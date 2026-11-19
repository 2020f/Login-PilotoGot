function Get-EnvirapidoHttpRedirectChecks {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Probes,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Redirects
    )
    $complete = $Probes.Count -gt 0 -and $Redirects.Count -eq $Probes.Count
    $expectedSpoofs = @($Probes | Where-Object { $_.SpoofForwardedProto }).Count
    $actualSpoofs = @($Redirects | Where-Object { $_.spoofedForwardedProto }).Count
    [ordered]@{
        httpToHttpsSameHost443PreservesPathQuery = $complete -and @($Redirects | Where-Object { !$_.passed }).Count -eq 0
        httpRedirectsPreserveMethod = $complete -and @($Redirects | Where-Object { !$_.methodPreserving }).Count -eq 0
        spoofedForwardedProtoCannotBypassRedirect = $complete -and $expectedSpoofs -gt 0 -and
            $actualSpoofs -eq $expectedSpoofs -and @($Redirects | Where-Object { $_.spoofedForwardedProto -and !$_.passed }).Count -eq 0
    }
}
