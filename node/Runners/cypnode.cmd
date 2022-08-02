@echo off
pushd %USERPROFILE%\.cypher & dotnet cyphernetworknode.dll %* & popd