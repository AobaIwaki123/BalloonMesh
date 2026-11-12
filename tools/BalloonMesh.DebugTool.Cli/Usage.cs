namespace BalloonMesh.DebugTool.Cli;

/// <summary>Help text and the version string.</summary>
public static class Usage
{
    public const string CommandName = "balloonmesh-debug";
    public const string Version = "0.1.0";

    public static void Write(TextWriter writer, IReadOnlyList<string> path)
    {
        var command = path.Count == 0 ? string.Empty : path[0].ToLowerInvariant();
        switch (command)
        {
            case "led":
                WriteLed(writer);
                break;
            case "gateway":
                WriteGateway(writer);
                break;
            case "protocol":
                WriteProtocol(writer);
                break;
            case "show":
                WriteShow(writer);
                break;
            case "health":
            case "state":
                WriteGeneral(writer);
                break;
            default:
                WriteGeneral(writer);
                break;
        }
    }

    private static void WriteGeneral(TextWriter writer)
    {
        writer.WriteLine($"""
            BalloonMesh debug CLI {Version}

            Usage:
              {CommandName} [global options] <command> [command options]

            Commands:
              health                    Query the server health endpoint.
              state                     Print the fleet state.
              show                      List scheduled show steps (or --cancel <id>).
              led now|set|cancel|reset  Send LED commands through the debug API.
              gateway run               Run a fake gateway and log mesh traffic.
              protocol decode|encode|ops
                                        Work with Protocol B vendor messages offline.
              version                   Print the CLI version.

            Global options:
              --server <url>         Server base URL (default http://localhost:5080).
              --role <role>          Development mock login: Admin, Operator or Viewer.
              --cookie <value>       Raw Cookie header for servers without mock login.
              --gateway-token <token>  X-Gateway-Token for gateway endpoints.
              --json                 Emit JSON instead of human-readable text.
              --verbose              Include raw JSON frames in text output.
              -h, --help             Show this help.

            Environment variables:
              BALLOONMESH_SERVER, BALLOONMESH_ROLE, BALLOONMESH_COOKIE,
              BALLOONMESH_GATEWAY_TOKEN

            Examples:
              {CommandName} --role Admin health
              {CommandName} --role Admin state
              {CommandName} --role Admin led now --target-kind balloon --target 0x0101 --color #FF8000
              {CommandName} --role Admin show --limit 20
              {CommandName} --role Admin --gateway-token secret gateway run --id cli-gw
              {CommandName} protocol encode --op LED_SET --at +10s --color #00FF00
            """);
    }

    private static void WriteShow(TextWriter writer)
    {
        writer.WriteLine($"""
            Usage:
              {CommandName} [global options] show [--limit <n>]
              {CommandName} [global options] show --cancel <id>

            Options:
              --limit <n>    Number of steps to fetch (1..200, default 50).
              --cancel <id>  Cancel one scheduled show step.
            """);
    }

    private static void WriteLed(TextWriter writer)
    {
        writer.WriteLine($"""
            Usage:
              {CommandName} [global options] led now    --color <color> [--target-kind all|balloon] [--target all|0x0101]
              {CommandName} [global options] led set    --color <color> [--at <time>] [--target-kind ...] [--target ...]
              {CommandName} [global options] led cancel --at <time>        [--target-kind ...] [--target ...]
              {CommandName} [global options] led reset  [--cutoff <time>] [--set-now --color <color>] [--target-kind ...]

            Options:
              --color <#RRGGBB|r,g,b>  LED color (also accepts --r/--g/--b bytes).
              --at <time>              ISO-8601 time, "now", or a relative offset like +10s.
              --cutoff <time>          RESET cutoff; defaults to now on the server.
              --set-now                Send RESET_AND_SET instead of RESET.
              --target-kind <kind>     all (default), everyone, balloon or node.
              --target <value>         all (default) or a balloon mesh address.

            Times default to +10s for `led set` and now for the immediate commands.
            """);
    }

    private static void WriteGateway(TextWriter writer)
    {
        writer.WriteLine($"""
            Usage:
              {CommandName} [global options] gateway run --id <gatewayId> [options]

            Options:
              --id <gatewayId>       Gateway id to register (default debug-gateway).
              --transport <mode>     websocket (default) or http.
              --poll-ms <n>          HTTP long-poll wait in ms (default 10000).
              --firmware <text>      Reported firmware string (default debugtool/1.0).
              --spool <n>            Reported spool backlog.
              --no-provisioned       Report the gateway as not provisioned.
              --event <spec>         Mesh event to inject after connecting. Repeatable.
              --event-delay <ms>     Delay before/between injected events (default 500).
              --duration <seconds>   Stop after this many seconds (default: wait for Ctrl+C).

            Event specs:
              raw:opcode=0xC102E5,payload=...,src=0x0101,dst=0xC011,rssi=-45,ttl=3
              ledset:r=255,g=128,b=0,at=+10s
              lednow:r=255,g=255,b=255
              ledcancel:at=2026-01-01T00:00:10Z
              ftm:anchor=1,distance=3000,batch=1
              queue:kind=nearfull,count=401
              neighbor:batch=1,index=0,neighbor=0x0102@-45,neighbor=0x0103@-60
              time:at=now | neighbor_request | neighbor_probe | time_request

            Examples:
              {CommandName} --role Admin --gateway-token secret gateway run --id cli-gw --duration 60
              {CommandName} --gateway-token secret gateway run --id cli-gw --transport http \
                --event ftm:anchor=1,distance=3000,batch=1
            """);
    }

    private static void WriteProtocol(TextWriter writer)
    {
        writer.WriteLine($"""
            Usage:
              {CommandName} protocol ops
              {CommandName} protocol decode --opcode <0xC102E5> [--payload <hex>]
              {CommandName} protocol encode --op <NAME|0xNN> [options]

            Encode options:
              --at <time>             Tick time for LED/TIME messages (default now, +10s for LED_SET).
              --cutoff <time>         Cutoff for LED_RESET_ALL / LED_RESET_AND_SET.
              --color <#RRGGBB|r,g,b> LED color (also --r/--g/--b).
              --anchor <id>           FTM anchor id (default 1).
              --distance <cm>         FTM distance in centimetres (required).
              --batch <id>            FTM/NEIGHBOR batch id (default 1).
              --timestamp <uint>      FTM timestamp (default: current low 32 bits of tick40).
              --kind <nearfull|recovered>  QUEUE_STATUS kind.
              --count <n>             QUEUE_STATUS queue depth (required).
              --index <n>             NEIGHBOR_REPORT packet index (default 0).
              --neighbor <0x0102@-45> NEIGHBOR_REPORT entry (repeatable, 1..2).
              --payload <hex>         Raw payload when --op is a numeric vendor opcode.

            Examples:
              {CommandName} protocol decode --opcode 0xC102E5 --payload 0100000000010203
              {CommandName} protocol encode --op FTM_REPORT --anchor 1 --distance 3000 --batch 2
              {CommandName} protocol encode --op NEIGHBOR_REPORT --neighbor 0x0102@-45 --neighbor 0x0103@-60
              {CommandName} protocol encode --op 0x0A --payload 0000000000000000
            """);
    }
}
