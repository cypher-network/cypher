# This resource file contains a list of variables used throughout cypnode testing. These variables can be overridden on the command line, by means of the command-line parameter --variable <name>:<value>

*** Variables ***
# cypnode executable path. Override with `robot --variable cypnode_path:<executable path>`
${cypnode_path}              ../../publish

# default appsettings.json
${appsettings.default.json}  resources/appsettings.default.json

${api_baseurl}               http://127.0.0.1:7000


*** Keywords ***
Start node
    [Arguments]  ${appsettings}
    Copy File   ${appsettings}  appsettings.json
    Remove Directory  ${cypnode_path}/keys     recursive=True
    Remove Directory  ${cypnode_path}/storedb  recursive=True
    ${cypnode_handle} =  Start Process  ${cypnode_path}/cypnode  stdout=stdout.txt  stderr=stderr.txt
    Sleep  10s
    Process Should Be Running  ${cypnode_handle}
    [return]  ${cypnode_handle}

Stop node
    [Arguments]  ${cypnode_handle}
    Send Signal To Process  SIGINT  ${cypnode_handle}
    ${cypnode_result} =  Wait For Process  ${cypnode_handle}
    Should Not Contain  ${cypnode_result.stdout}  [FTL]
