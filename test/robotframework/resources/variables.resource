# This resource file contains a list of variables used throughout cypnode testing. These variables can be overridden on the command line, by means of the command-line parameter --variable <name>:<value>

*** Variables ***
# cypnode executable path. Override with `robot --variable cypnode:<executable path>`
${cypnode_path}  ../../publish


# default appsettings.json
${appsettings.default.json}  resources/appsettings.default.json
