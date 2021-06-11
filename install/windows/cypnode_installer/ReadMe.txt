Hello tangram user, this is a small read me file to help you get going with your tangram node installation.

Installer features:

The installers comprises of 3 features:

- Cypher Node ( mandatory )
- Cypher Node configuration ( optional )
- Cypher Node as a service ( optional )

-- Cypher Node ( mandatory )

This is the main feature of the installer and it cannot be unselected during installation. It installs the cypher node which is responsible for communication with
the mainnet.

-- Cypher Node configuration ( optional )

If this feature is installed ( by default set to true ), then an interactive utility will be launched during installation, during which you will have to configure your
tangram node. If you're installing yoru node for the first time then this step is recommended. More about configuration at the end of this read me file.

-- Cypher Node as a service ( optional )

If this feature is installed ( by default set to true ) and if the node is either pre-configured or it is configured during installation, then after the node is installed
it will run as a background service in windows. The service will have the nane "Cypher Node Service" and it is set to automatically start after installation,
and every time you restart your system for convenience.

You can install any of the optional feature via the interactive .msi installer, or if you're deploying per command line you can choose which features to install like this:

msiexec /i <cypher_node_installer>.msi /quiet /qn /norestart ADDLOCAL=ALL

The above command line will install all optional features.

msiexec /i <cypher_node_installer>.msi /quiet /qn /norestart ADDLOCAL=CypherNodeConfiguration,CypherNodeService

This again installs all features. If you omit one of the comma separated features, then that feature will not be installed.

Be aware that whenever the feature CypherNodeConfiguration is installed either explicitly or implicitly ( ADDLOCAL=ALL ) then supervision of the installation process is required
due to the interactive configuration utility.

Configuration Utility properties:

If you chose not to run the interactive configuration utility during installation, you can create a configuration settings file based on the following tempalte:

C:\Program Files\Cypher\Node\Configuration\Templates\appsettings.json

In this file you'd need to change the following settings to have a working node:

"Listening": "<API_ENDPOINT_BIND>"
"Advertise": "<API_ENDPOINT_PUBLIC>"
"Advertise": "<SERF_ENDPOINT_PUBLIC>"
"Listening": "<SERF_ENDPOINT_BIND>"
"NodeName": "<SERF_NODE_NAME>"

You can find more Information about these settings here: https://github.com/cypher-network/cypher/wiki/Configuration

Uninstalling

Please stop the service manually before uninstalling or upgrading this product to avoid having to reboot the machine.