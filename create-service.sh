#!/usr/bin/env bash
FILE="/etc/systemd/system/cypnode.service"

/bin/cat <<EOM >$FILE
[Unit]
Description=cypnode

[Service]
WorkingDirectory=$HOME/.cypher/dist
ExecStart=dotnet $HOME/.cypher/dist/TGMNode.dll "$@"
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=cypnode
User=$USER
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

[Install]
WantedBy=multi-user.target
EOM

systemctl daemon-reload
systemctl enable cypnode.service
systemctl start cypnode.service

echo "service installed! verify with:"
echo "> sudo systemctl status cypnode.service"
