# Genera src/Rama/Resources/Locales/es_CL.Casing.axaml: pasa a "sentence case" las cadenas TODO-MAYUSCULAS de en_US/es_ES.
$root = Split-Path $PSScriptRoot -Parent
$keep = @('URL','LFS','SHA','HEAD','GPG','SSH','CLI','ID','CI','HTTP','HTTPS','PR','SVN','GIT','UI','OK','API','CRLF','LF','PAT','JSON','XML','CSV','RGB','UTC')
function Read-Map($p) {
  $m = [ordered]@{}
  foreach ($l in Get-Content -LiteralPath $p -Encoding UTF8) {
    if ($l -match '<x:String x:Key="([^"]+)" xml:space="preserve">(.*)</x:String>') { $m[$Matches[1]] = $Matches[2] }
  }
  $m
}
$eff = Read-Map "$root/src/Resources/Locales/en_US.axaml"
(Read-Map "$root/src/Resources/Locales/es_ES.axaml").GetEnumerator() | ForEach-Object { $eff[$_.Key] = $_.Value }
$manual = @{ 'Text.WorkingCopy.Unstaged'='Sin preparar'; 'Text.WorkingCopy.Staged'='Preparados'; 'Text.WorkingCopy.Commit'='Confirmar' }
function To-Sentence($v) {
  $words = $v -split ' '
  $out = for ($i=0; $i -lt $words.Count; $i++) {
    $w = $words[$i]
    if (($keep -contains ($w -replace '[^A-Za-z]',''))) { $w } else { $w.ToLower() }
  }
  $s = $out -join ' '
  if ($s.Length -gt 0) { $s = $s.Substring(0,1).ToUpper() + $s.Substring(1) }
  $s
}
$lines = foreach ($k in $eff.Keys) {
  $v = $eff[$k]
  $new = $null
  if ($manual.ContainsKey($k)) { $new = $manual[$k] }
  elseif ($v -cmatch '\p{Lu}{3}' -and $v -cnotmatch '\p{Ll}' -and $v -notmatch '^\W*$') { $new = To-Sentence $v }
  if ($new -and $new -cne $v) { "  <x:String x:Key=`"$k`" xml:space=`"preserve`">$new</x:String>" }
}
$hdr = '<ResourceDictionary xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">' + "`n  <!-- GENERADO por tools/gen-casing.ps1: cadenas en MAYUSCULAS pasadas a sentence case (la spec de diseno las prohibe). -->"
$txt = $hdr + "`n" + ($lines -join "`n") + "`n</ResourceDictionary>`n"
[IO.File]::WriteAllText("$root/src/Rama/Resources/Locales/es_CL.Casing.axaml", $txt, (New-Object Text.UTF8Encoding($false)))
"$($lines.Count) claves"
