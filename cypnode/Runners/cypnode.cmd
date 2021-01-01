@echo off
pushd %USERPROFILE%\.cypher\dist & dotnet TGMNode.dll %* & popd