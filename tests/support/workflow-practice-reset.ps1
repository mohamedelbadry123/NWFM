param(
    [Parameter(Mandatory = $true)][string]$SnapshotPath,
    [Parameter(Mandatory = $true)][string]$OutputPath
)
# Offline transformation only. The caller must snapshot and save through the revision-checked API.
$ErrorActionPreference = 'Stop'
$snapshot = Get-Content -LiteralPath $SnapshotPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($snapshot.status -ne 'Draft' -or [string]::IsNullOrWhiteSpace($snapshot.revision)) {
    throw 'A saved draft with its current revision is required.'
}
[xml]$xml = $snapshot.xmlContent
$workspace = $xml.DocumentElement.GetAttribute('workspaceJson') | ConvertFrom-Json
if ($workspace.kind -ne 'Child' -or $workspace.designerVersion -ne 4) {
    throw 'Only a version 4 child practice draft can be reset.'
}
$activities = @($xml.SelectNodes('/*/*[local-name()="Activities"]/*'))
$starts = @($activities | Where-Object { $_.GetAttribute('type') -eq 'Start' })
$registration = @($activities | Where-Object { $_.GetAttribute('type') -eq 'UserTask' -and $_.GetAttribute('name') -eq 'Registration' })
if ($starts.Count -ne 1 -or $registration.Count -ne 1) { throw 'Exactly one Start and one Registration Step are required.' }
$gates = @('ExclusiveGateway', 'ParallelGateway', 'InclusiveGateway', 'JoinGateway')
$removed = @($activities | Where-Object { $gates -contains $_.GetAttribute('type') } | ForEach-Object { $_.GetAttribute('nodeKey') })
function Clear-RoutingConfiguration([string]$value) {
    if ([string]::IsNullOrWhiteSpace($value)) { return $value }
    $config = $value | ConvertFrom-Json
    foreach ($key in @('routing', 'triggerBinding')) { $config.PSObject.Properties.Remove($key) }
    # Retain unrelated authoring metadata; remove only routing repair records.
    if ($config.authoring) {
        $config.authoring.PSObject.Properties.Remove('repairs')
    }
    foreach ($form in @($config.formSteps)) {
        if ($null -eq $form) { continue }
        $form.PSObject.Properties.Remove('rules')
        $form.PSObject.Properties.Remove('otherwise')
    }
    return ConvertTo-Json -InputObject $config -Depth 100 -Compress
}
foreach ($activity in $activities) {
    if ($removed -contains $activity.GetAttribute('nodeKey')) {
        [void]$activity.ParentNode.RemoveChild($activity)
    } elseif ($activity.HasAttribute('configurationJson')) {
        $activity.SetAttribute('configurationJson', (Clear-RoutingConfiguration $activity.GetAttribute('configurationJson')))
    }
}
$transitions = $xml.SelectSingleNode('/*/*[local-name()="Transitions"]')
if ($null -eq $transitions) { throw 'Missing transitions container.' }
$entry = @($transitions.ChildNodes | Where-Object { $_.NodeType -eq 'Element' -and $_.GetAttribute('from') -eq $starts[0].GetAttribute('nodeKey') -and $_.GetAttribute('to') -eq $registration[0].GetAttribute('nodeKey') })
if ($entry.Count -ne 1) { throw 'The original Start to Registration connection must exist exactly once.' }
foreach ($transition in @($transitions.ChildNodes)) {
    if ($transition -ne $entry[0]) { [void]$transitions.RemoveChild($transition) }
}
$designer = $snapshot.designerJson | ConvertFrom-Json
$designer.nodes = @($designer.nodes | Where-Object { $removed -notcontains $_.nodeKey })
foreach ($node in $designer.nodes) {
    $node.configurationJson = Clear-RoutingConfiguration $node.configurationJson
}
$startId = @($designer.nodes | Where-Object { $_.nodeKey -eq $starts[0].GetAttribute('nodeKey') })[0].id
$registrationId = @($designer.nodes | Where-Object { $_.nodeKey -eq $registration[0].GetAttribute('nodeKey') })[0].id
$designer.edges = @($designer.edges | Where-Object { $_.fromNodeId -eq $startId -and $_.toNodeId -eq $registrationId })
if ($designer.edges.Count -ne 1) { throw 'The canvas entry connection does not match the executable definition.' }
$payload = [ordered]@{
    xmlContent = $xml.OuterXml
    designerJson = (ConvertTo-Json -InputObject $designer -Depth 100 -Compress)
    expectedRevision = $snapshot.revision
}
$result = ConvertTo-Json -InputObject $payload -Depth 100
[System.IO.File]::WriteAllText([System.IO.Path]::GetFullPath($OutputPath), $result, [System.Text.UTF8Encoding]::new($false))
Write-Output ('Prepared reset: removed {0} routing nodes; preserved {1} nodes and Start to Registration.' -f $removed.Count, $designer.nodes.Count)
