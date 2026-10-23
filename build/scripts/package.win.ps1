Remove-Item -Path build\SourceGit\*.pdb -Force
Compress-Archive -Path build\SourceGit -DestinationPath "build\rama_${env:VERSION}.${env:RUNTIME}.zip" -Force