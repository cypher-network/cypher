using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using CypherNetwork.Models;
using Spectre.Console;
using Config = CypherNetwork.Models.Config;

namespace CypherNetworkNode.Configuration;

/// <summary>
/// 
/// </summary>
public class Utility
{
    private const string ManuallyEnterNodeName = "Manually enter node name";
    private const string AutomaticallyGenerateANodeName = "Automatically generate a node name";

    private const string ManuallyEnterIpAddress = "Manually enter IP address";
    private const string FindIpAddressAutomatically = "Find IP address automatically";

    private const string UseDefaultPort = "Use default port";
    private const string ManuallyEnterPort = "Manually enter port";

    private const string UseDefaultSyncTime = "Use default sync time";
    private const string ManuallyEnterSyncTime = "Manually enter sync time";

    private readonly Node _node;
    private readonly IList<IPService> _ipServices = IPServices.Services;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="config"></param>
    public Utility(Config config)
    {
        _node = config.Node;

        if (!AnsiConsole.Confirm($"Continue with the config utility"))
        {
            return;
        }

        WriteDivider("Node Name");
        NodeName();
        WriteDivider("Public IP address");
        StepIpAddress();
        WriteDivider("Public TCP Port");
        TcpPort();
        WriteDivider("Public Peer Discovery Port");
        DiscoveryPort();
        WriteDivider("Public Web Socket Port");
        WebSocketPort();
        WriteDivider("Public HTTP Port");
        HttpPort();
        WriteDivider("Auto Sync Time");
        AutoSyncTime();
        var jsonWriteOptions = new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        jsonWriteOptions.Converters.Add(new JsonStringEnumConverter());
        var newJson = JsonSerializer.Serialize(config, jsonWriteOptions);
        var appSettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
        try
        {
            File.WriteAllText(appSettingsPath, newJson);
            WriteDivider("Settings");
            AnsiConsole.Write(
                new Table()
                    .AddColumn(new TableColumn("Setting").Centered())
                    .AddColumn(new TableColumn("Value").Centered())
                    .AddRow("Name", _node.Name)
                    .AddRow("IP Address", _node.EndPoint.Address.ToString())
                    .AddRow("Http Port", _node.Network.HttpPort.ToString())
                    .AddRow("Tcp Port", _node.Network.P2P.TcpPort.ToString())
                    .AddRow("Web Socket Port", _node.Network.P2P.WsPort.ToString())
                    .AddRow("Discovery Peer Port", _node.Network.P2P.DsPort.ToString())
                    .AddRow("Auto Sync Time", _node.Network.AutoSyncEveryMinutes.ToString()));
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine(ex.Message);
            throw;
        }
    }

    /// <summary>
    /// 
    /// </summary>
    private void NodeName()
    {
        var nodeNameChoice = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title(
                "Your [green]node[/] is identified by a name, which can be a human-readable name or a randomly generated name. The node name must be unique in the network.")
            .AddChoices(new[] { ManuallyEnterNodeName, AutomaticallyGenerateANodeName }));
        if (nodeNameChoice == ManuallyEnterNodeName)
        {
            var name = AnsiConsole.Prompt(
                new TextPrompt<string>(
                        "Enter [bold green]node name[/] (1 - 32 characters, allowed characters: a-z, A-Z, 0-9, \"_\" and \"-\"):")
                    .ValidationErrorMessage("").Validate(name =>
                    {
                        var pass = name.Length is >= 1 and <= 32 && name.All(character =>
                            char.IsLetterOrDigit(character) || character.Equals('_') || character.Equals('-'));
                        return pass switch
                        {
                            true => ValidationResult.Success(),
                            false => ValidationResult.Error("[red]Something went wrong[/]")
                        };
                    }));
            _node.Name = name;
        }
        else
        {
            var bytes = new byte[10];
            var randomNumberGenerator = RandomNumberGenerator.Create();
            randomNumberGenerator.GetBytes(bytes);
            _node.Name = $"cypher-{BitConverter.ToString(bytes).Replace("-", "").ToLower()}";
        }
    }

    /// <summary>
    /// 
    /// </summary>
    private void StepIpAddress()
    {
        var ipAddressChoice = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title(
                "Your [green]node[/] needs to be able to communicate with other nodes on the internet. For this you " +
                "need to broadcast your public IP address, which is in many cases not the same as your local network " +
                "address. Addresses starting with 10.x.x.x, 172.16.x.x and 192.168.x.x are local addresses and " +
                "should not be broadcast to the network. When you do not know your public IP address, you can find " +
                "it by searching for 'what is my ip address'. This does not work if you configure a remote node, " +
                "like for example a VPS. You can also choose to find your public IP address automatically.")
            .AddChoices(new[] { ManuallyEnterIpAddress, FindIpAddressAutomatically }));
        if (ipAddressChoice == ManuallyEnterIpAddress)
        {
            var foundIpAddress = CypherNetwork.Helper.Util.GetIpAddress();
            if (AnsiConsole.Confirm($"IP address found {foundIpAddress}"))
            {
                _node.EndPoint = new(foundIpAddress, 0);
                return;
            }

            var ipAddress = AnsiConsole.Prompt(new TextPrompt<string>("Enter IP address (e.g. 123.1.23.123):")
                .ValidationErrorMessage("").Validate(ip =>
                {
                    var pass = IPAddress.TryParse(ip, out _);
                    return pass switch
                    {
                        true => ValidationResult.Success(),
                        false => ValidationResult.Error("[red]Something went wrong[/]")
                    };
                }));
            _node.EndPoint = new(IPAddress.Parse(ipAddress), 0);
        }
        else
        {
            var serviceChoice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("Please choose the service to use for automatic IP address detection")
                .AddChoices(new[] { "ident.me", "ipify.org", "my-ip.io", "seeip.org" }));
            var selectedIpAddressService = _ipServices.First(service => service.Name == serviceChoice);
            _node.EndPoint = new(selectedIpAddressService.Read(), 0);
        }
    }

    /// <summary>
    /// 
    /// </summary>
    private void TcpPort()
    {
        var tcpPortChoice = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("Your node exposes an API which needs to be accessible by other nodes in the network. This " +
                   "API listens on a configurable public TCP port, the default tcp port number is " +
                   "[bold yellow]7946[/]. You have to make sure that this port is properly." +
                   "configured in your firewall or router.").AddChoices(UseDefaultPort, ManuallyEnterPort));
        if (tcpPortChoice == UseDefaultPort)
        {
            _node.Network.P2P = new P2P { TcpPort = 7946 };
        }
        else
        {
            var port = AnsiConsole.Prompt(new TextPrompt<int>("Enter [bold green]port[/]:").ValidationErrorMessage("")
                .Validate(p =>
                {
                    var pass = p is >= -1 and <= int.MaxValue;
                    return pass switch
                    {
                        true => ValidationResult.Success(),
                        false => ValidationResult.Error("[red]Something went wrong[/]")
                    };
                }));
            _node.Network.P2P = new P2P { TcpPort = port };
        }
    }

    /// <summary>
    /// 
    /// </summary>
    private void DiscoveryPort()
    {
        var tcpPortChoice = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("Your node exposes an API which needs to be accessible by other nodes in the network. This " +
                   "API listens on a configurable public Peer discovery port, the default Peer discovery port number is " +
                   "[bold yellow]5146[/]. You have to make sure that this port is properly." +
                   "configured in your firewall or router.").AddChoices(UseDefaultPort, ManuallyEnterPort));
        if (tcpPortChoice == UseDefaultPort)
        {
            _node.Network.P2P = _node.Network.P2P with { DsPort = 5146 };
        }
        else
        {
            var port = AnsiConsole.Prompt(new TextPrompt<int>("Enter [bold green]port[/]:").ValidationErrorMessage("")
                .Validate(p =>
                {
                    var pass = p is >= -1 and <= int.MaxValue;
                    return pass switch
                    {
                        true => ValidationResult.Success(),
                        false => ValidationResult.Error("[red]Something went wrong[/]")
                    };
                }));
            _node.Network.P2P = _node.Network.P2P with { DsPort = port };
        }
    }

    /// <summary>
    /// 
    /// </summary>
    private void WebSocketPort()
    {
        var tcpPortChoice = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("Your node exposes an API which needs to be accessible by other nodes in the network. This " +
                   "API listens on a configurable public TCP port, the default web socket port number is " +
                   "[bold yellow]7947[/]. You have to make sure that this port is properly." +
                   "configured in your firewall or router.").AddChoices(UseDefaultPort, ManuallyEnterPort));
        if (tcpPortChoice == UseDefaultPort)
        {
            _node.Network.P2P = _node.Network.P2P with { WsPort = 7947 };
        }
        else
        {
            var port = AnsiConsole.Prompt(new TextPrompt<int>("Enter [bold green]port[/]:").ValidationErrorMessage("")
                .Validate(p =>
                {
                    var pass = p is >= -1 and <= int.MaxValue && _node.Network.P2P.TcpPort != p;
                    return pass switch
                    {
                        true => ValidationResult.Success(),
                        false => ValidationResult.Error("[red]Something went wrong[/]")
                    };
                }));
            _node.Network.P2P = _node.Network.P2P with { WsPort = port };
        }
    }

    /// <summary>
    /// 
    /// </summary>
    private void HttpPort()
    {
        var tcpPortChoice = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("Your node exposes an API which needs to be accessible by other nodes in the network. This " +
                   "API listens on a configurable public TCP port, the default http port number is " +
                   "[bold yellow]48655[/]. You have to make sure that this port is properly." +
                   "configured in your firewall or router.").AddChoices(UseDefaultPort, ManuallyEnterPort));
        if (tcpPortChoice == UseDefaultPort)
        {
            _node.Network.HttpPort = 48655;
        }
        else
        {
            var port = AnsiConsole.Prompt(new TextPrompt<int>("Enter [bold green]port[/]:").ValidationErrorMessage("")
                .Validate(p =>
                {
                    var pass = p is >= -1 and <= int.MaxValue;
                    return pass switch
                    {
                        true => ValidationResult.Success(),
                        false => ValidationResult.Error("[red]Something went wrong[/]")
                    };
                }));
            _node.Network.HttpPort = port;
        }
    }

    /// <summary>
    /// 
    /// </summary>
    private void HttpsPort()
    {
        var tcpPortChoice = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("Your node exposes an API which needs to be accessible by other nodes in the network. This " +
                   "API listens on a configurable public TCP port, the default https port number is " +
                   "[bold yellow]44333[/]. You have to make sure that this port is properly." +
                   "configured in your firewall or router.").AddChoices(UseDefaultPort, ManuallyEnterPort));
        if (tcpPortChoice == UseDefaultPort)
        {
            _node.Network.HttpsPort = 44333;
        }
        else
        {
            var port = AnsiConsole.Prompt(new TextPrompt<int>("Enter [bold green]port[/]:").ValidationErrorMessage("")
                .Validate(p =>
                {
                    var pass = p is >= -1 and <= int.MaxValue;
                    return pass switch
                    {
                        true => ValidationResult.Success(),
                        false => ValidationResult.Error("[red]Something went wrong[/]")
                    };
                }));
            _node.Network.HttpsPort = port;
        }
    }

    /// <summary>
    /// 
    /// </summary>
    private void AutoSyncTime()
    {
        var syncTimeChoice = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("Node automatically starts syncing with network peers on discovery " +
                   "By default, the auto sync time is set to [bold yellow]10 minutes[/].").AddChoices(UseDefaultSyncTime, ManuallyEnterSyncTime));
        if (syncTimeChoice == UseDefaultSyncTime)
        {
            _node.Network.AutoSyncEveryMinutes = 10;
        }
        else
        {
            var time = AnsiConsole.Prompt(new TextPrompt<int>("Enter [bold green]sync time[/]:").ValidationErrorMessage("")
                .Validate(p =>
                {
                    var pass = p is >= -1 and <= int.MaxValue;
                    return pass switch
                    {
                        true => ValidationResult.Success(),
                        false => ValidationResult.Error("[red]Something went wrong[/]")
                    };
                }));
            _node.Network.AutoSyncEveryMinutes = time;
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="text"></param>
    private static void WriteDivider(string text)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule($"[yellow]{text}[/]").RuleStyle("grey").LeftAligned());
    }
}