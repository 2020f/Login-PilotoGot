$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'PublicHttpsChecks.ps1')
$probes=@(@{SpoofForwardedProto=$false},@{SpoofForwardedProto=$false},@{SpoofForwardedProto=$true})
function Good-Results {
    @(@{passed=$true;methodPreserving=$true;spoofedForwardedProto=$false},
      @{passed=$true;methodPreserving=$true;spoofedForwardedProto=$false},
      @{passed=$true;methodPreserving=$true;spoofedForwardedProto=$true})
}
function Assert-Policy([bool]$Condition,[string]$Message){if(!$Condition){throw $Message}}
$checks=Get-EnvirapidoHttpRedirectChecks -Probes $probes -Redirects (Good-Results)
Assert-Policy (@($checks.Values|Where-Object {!$_}).Count -eq 0) 'A complete safe redirect set must pass.'
$bad=Good-Results;$bad[0].passed=$false;$bad[0].methodPreserving=$false
$checks=Get-EnvirapidoHttpRedirectChecks -Probes $probes -Redirects $bad
Assert-Policy (!$checks.httpToHttpsSameHost443PreservesPathQuery -and !$checks.httpRedirectsPreserveMethod) 'A root served with200 must fail.'
$checks=Get-EnvirapidoHttpRedirectChecks -Probes $probes -Redirects @((Good-Results)[0])
Assert-Policy (@($checks.Values|Where-Object {$_}).Count -eq 0) 'A truncated response set must never pass.'
$bad=Good-Results;$bad[2].spoofedForwardedProto=$false
$checks=Get-EnvirapidoHttpRedirectChecks -Probes $probes -Redirects $bad
Assert-Policy (!$checks.spoofedForwardedProtoCannotBypassRedirect) 'Missing anti-spoof evidence must fail.'
$bad=Good-Results;$bad[2].passed=$false
$checks=Get-EnvirapidoHttpRedirectChecks -Probes $probes -Redirects $bad
Assert-Policy (!$checks.spoofedForwardedProtoCannotBypassRedirect) 'A spoofed request served overHTTP must fail.'
$bad=Good-Results;$bad[1].methodPreserving=$false
$checks=Get-EnvirapidoHttpRedirectChecks -Probes $probes -Redirects $bad
Assert-Policy (!$checks.httpRedirectsPreserveMethod) 'A redirect that may changePOST toGET must fail.'
$checks=Get-EnvirapidoHttpRedirectChecks -Probes @() -Redirects @()
Assert-Policy (@($checks.Values|Where-Object {$_}).Count -eq 0) 'No probes is not a successful verification.'
$checks=Get-EnvirapidoHttpRedirectChecks -Probes @(@{SpoofForwardedProto=$false}) -Redirects @((Good-Results)[0])
Assert-Policy (!$checks.spoofedForwardedProtoCannotBypassRedirect) 'Removing the spoof probe must not silently pass.'
@{passed=8;failed=0;networkRequests=0;scope='Decision policy only; does not attest Cloudflare deployment.'}|ConvertTo-Json
