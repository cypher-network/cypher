@echo off
pushd %USERPROFILE%\.cypher & dotnet TGMNode.dll %* & popd